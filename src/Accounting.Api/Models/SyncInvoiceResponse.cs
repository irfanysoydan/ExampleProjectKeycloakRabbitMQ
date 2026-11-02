namespace Accounting.Api.Models;

public sealed record SyncInvoiceResponse(Guid EventId, string InvoiceId, string Status);
