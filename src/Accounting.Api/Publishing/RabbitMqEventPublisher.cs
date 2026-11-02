using System.Text.Json;
using Accounting.Api.Status;
using Accounting.Contracts;
using Accounting.Messaging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Accounting.Api.Publishing;

public sealed class RabbitMqEventPublisher(
    IOptions<RabbitMqOptions> options,
    InvoiceStatusStore statusStore) : IEventPublisher, IAsyncDisposable
{
    // Direct reply-to lets the worker answer this channel without a status queue.
    private const string ReplyTo = "amq.rabbitmq.reply-to";

    private static readonly TimeSpan PublishTimeout = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public async Task PublishAsync(InvoiceCreatedEvent invoiceCreated, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(PublishTimeout);

        try
        {
            await _lock.WaitAsync(timeout.Token);
            try
            {
                var channel = await GetChannelAsync(timeout.Token);
                var properties = new BasicProperties
                {
                    MessageId = invoiceCreated.EventId.ToString(),
                    Type = nameof(InvoiceCreatedEvent),
                    ContentType = "application/json",
                    DeliveryMode = DeliveryModes.Persistent,
                    ReplyTo = ReplyTo,
                    Headers = new Dictionary<string, object?> { ["x-tenant-id"] = invoiceCreated.TenantId },
                };

                await channel.BasicPublishAsync(
                    RabbitMqTopology.Exchange,
                    RabbitMqTopology.InvoiceRoutingKey,
                    mandatory: true,
                    properties,
                    JsonSerializer.SerializeToUtf8Bytes(invoiceCreated),
                    timeout.Token);
            }
            finally
            {
                _lock.Release();
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new EventPublishException($"Failed to publish event {invoiceCreated.EventId}.", ex);
        }
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        _connection ??= await options.Value.ConnectAsync("accounting-api", cancellationToken);

        var channel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);

        await RabbitMqTopology.DeclareAsync(channel, cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) =>
        {
            var status = JsonSerializer.Deserialize<InvoiceStatusEvent>(delivery.Body.Span);
            if (status is not null)
            {
                statusStore.Save(status);
            }

            return Task.CompletedTask;
        };
        await channel.BasicConsumeAsync(ReplyTo, autoAck: true, consumer, cancellationToken);

        _channel = channel;
        return channel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _lock.Dispose();
    }
}
