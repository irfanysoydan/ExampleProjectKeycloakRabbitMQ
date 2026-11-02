using System.Text.Json.Serialization;

namespace Accounting.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<InvoiceSyncStatus>))]
public enum InvoiceSyncStatus
{
    Retrying,
    Synchronized,
    DeadLettered,
}

public sealed record InvoiceStatusEvent(
    Guid EventId,
    string InvoiceId,
    string TenantId,
    InvoiceSyncStatus Status,
    int Attempt,
    DateTimeOffset OccurredAtUtc);
