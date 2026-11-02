using Accounting.Contracts;

namespace Accounting.Api.Publishing;

public interface IEventPublisher
{
    Task PublishAsync(InvoiceCreatedEvent invoiceCreated, CancellationToken cancellationToken);
}

public sealed class EventPublishException(string message, Exception innerException)
    : Exception(message, innerException);
