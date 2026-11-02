using RabbitMQ.Client;

namespace Accounting.Messaging;

public static class RabbitMqTopology
{
    public const string Exchange = "accounting.events";

    public const string InvoiceQueue = "accounting.invoice-sync";
    public const string RetryQueue = "accounting.invoice-sync.retry";
    public const string DeadLetterQueue = "accounting.invoice-sync.dead";

    public const string InvoiceRoutingKey = "invoice.created";
    public const string RetryRoutingKey = "invoice.retry";
    public const string DeadLetterRoutingKey = "invoice.dead";

    public const int RetryDelayMilliseconds = 5000;

    public static async Task DeclareAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Direct, durable: true, cancellationToken: cancellationToken);

        // A rejected invoice waits in the retry queue, then expires back into the invoice queue.
        await DeclareQueueAsync(channel, InvoiceQueue, InvoiceRoutingKey, new Dictionary<string, object?>
        {
            ["x-dead-letter-routing-key"] = RetryRoutingKey,
            ["x-dead-letter-exchange"] = Exchange,
            ["x-dead-letter-strategy"] = "at-least-once",
            ["x-overflow"] = "reject-publish",
        }, cancellationToken);

        await DeclareQueueAsync(channel, RetryQueue, RetryRoutingKey, new Dictionary<string, object?>
        {
            ["x-message-ttl"] = RetryDelayMilliseconds,
            ["x-dead-letter-routing-key"] = InvoiceRoutingKey,
            ["x-dead-letter-exchange"] = Exchange,
            ["x-dead-letter-strategy"] = "at-least-once",
            ["x-overflow"] = "reject-publish",
        }, cancellationToken);

        await DeclareQueueAsync(channel, DeadLetterQueue, DeadLetterRoutingKey, [], cancellationToken);
    }

    private static async Task DeclareQueueAsync(
        IChannel channel,
        string queue,
        string routingKey,
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        arguments["x-queue-type"] = "quorum";

        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, arguments: arguments, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(queue, Exchange, routingKey, cancellationToken: cancellationToken);
    }
}
