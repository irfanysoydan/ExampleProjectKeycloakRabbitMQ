namespace Accounting.Api.Models;

public sealed record InvoiceStatusResponse(Guid EventId, string Status, int? Attempt, DateTimeOffset? UpdatedAtUtc);
