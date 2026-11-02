namespace Accounting.Contracts;

public sealed record InvoiceCreatedEvent(
    Guid EventId,
    string InvoiceId,
    string TenantId,
    decimal Amount,
    string ErpSystem,
    string? RequestedBy,
    DateTimeOffset OccurredAtUtc);
