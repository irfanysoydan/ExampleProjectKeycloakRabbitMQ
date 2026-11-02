using System.Text;
using System.Text.Json;
using Accounting.Contracts;
using Accounting.Messaging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Accounting.Worker;

public sealed class AccountingSyncWorker(
    IOptions<RabbitMqOptions> options,
    ILogger<AccountingSyncWorker> logger) : BackgroundService
{
    private const int MaxAttempts = 3;

    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "RabbitMQ is unavailable. Reconnecting in {Delay}.", ReconnectDelay);
                await Task.Delay(ReconnectDelay, stoppingToken);
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        await using var connection = await options.Value.ConnectAsync("accounting-worker", stoppingToken);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            stoppingToken);

        await RabbitMqTopology.DeclareAsync(channel, stoppingToken);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => HandleDeliveryAsync(channel, delivery, stoppingToken);
        await channel.BasicConsumeAsync(RabbitMqTopology.InvoiceQueue, autoAck: false, consumer, stoppingToken);

        logger.LogInformation("[Accounting Hub] Background Worker is listening on queue {Queue}.", RabbitMqTopology.InvoiceQueue);

        var closed = new TaskCompletionSource();
        channel.ChannelShutdownAsync += (_, _) =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };

        await closed.Task.WaitAsync(stoppingToken);
        throw new InvalidOperationException("The RabbitMQ channel was closed.");
    }

    private async Task HandleDeliveryAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        InvoiceCreatedEvent? invoice = null;
        try
        {
            invoice = JsonSerializer.Deserialize<InvoiceCreatedEvent>(delivery.Body.Span);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Message {MessageId} is not a valid invoice event.", delivery.BasicProperties.MessageId);
        }

        if (invoice is null)
        {
            await MoveToDeadLetterQueueAsync(channel, delivery);
            return;
        }

        var attempt = GetAttempt(delivery.BasicProperties);
        logger.LogInformation(
            "[Accounting Hub] Processing invoice {InvoiceId} for tenant {TenantId}, attempt {Attempt} of {MaxAttempts}.",
            invoice.InvoiceId, invoice.TenantId, attempt, MaxAttempts);

        try
        {
            await SimulatedErpClient.TransferAsync(invoice, attempt, stoppingToken);
        }
        catch (ErpUnavailableException ex) when (attempt < MaxAttempts)
        {
            logger.LogWarning("[Accounting Hub] Invoice {InvoiceId} will be retried: {Reason}", invoice.InvoiceId, ex.Message);
            await NotifyAsync(channel, delivery, invoice, InvoiceSyncStatus.Retrying, attempt);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false);
            return;
        }
        catch (ErpUnavailableException ex)
        {
            logger.LogError("[Accounting Hub] Invoice {InvoiceId} moved to the dead-letter queue: {Reason}", invoice.InvoiceId, ex.Message);
            await MoveToDeadLetterQueueAsync(channel, delivery);
            await NotifyAsync(channel, delivery, invoice, InvoiceSyncStatus.DeadLettered, attempt);
            return;
        }

        logger.LogInformation(
            "[Accounting Hub] Successfully synchronized invoice {InvoiceId} to ERP/Accounting system via Background Worker.",
            invoice.InvoiceId);

        await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false);
        await NotifyAsync(channel, delivery, invoice, InvoiceSyncStatus.Synchronized, attempt);
    }

    private static async Task MoveToDeadLetterQueueAsync(IChannel channel, BasicDeliverEventArgs delivery)
    {
        await channel.BasicPublishAsync(
            RabbitMqTopology.Exchange,
            RabbitMqTopology.DeadLetterRoutingKey,
            mandatory: true,
            new BasicProperties(delivery.BasicProperties),
            delivery.Body);

        await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false);
    }

    private async Task NotifyAsync(
        IChannel channel,
        BasicDeliverEventArgs delivery,
        InvoiceCreatedEvent invoice,
        InvoiceSyncStatus status,
        int attempt)
    {
        var replyTo = delivery.BasicProperties.ReplyTo;
        if (string.IsNullOrEmpty(replyTo))
        {
            return;
        }

        var statusEvent = new InvoiceStatusEvent(invoice.EventId, invoice.InvoiceId, invoice.TenantId, status, attempt, DateTimeOffset.UtcNow);
        try
        {
            await channel.BasicPublishAsync(string.Empty, replyTo, JsonSerializer.SerializeToUtf8Bytes(statusEvent));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not report status {Status} for invoice {InvoiceId}.", status, invoice.InvoiceId);
        }
    }

    private static int GetAttempt(IReadOnlyBasicProperties properties)
    {
        if (properties.Headers is null
            || !properties.Headers.TryGetValue("x-death", out var header)
            || header is not IEnumerable<object> deaths)
        {
            return 1;
        }

        var rejections = deaths
            .OfType<IDictionary<string, object?>>()
            .Where(death => death["queue"] is byte[] queue && Encoding.UTF8.GetString(queue) == RabbitMqTopology.InvoiceQueue)
            .Sum(death => (long)death["count"]!);

        return (int)rejections + 1;
    }
}
