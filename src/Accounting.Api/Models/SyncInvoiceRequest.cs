using System.ComponentModel.DataAnnotations;

namespace Accounting.Api.Models;

public sealed record SyncInvoiceRequest(
    [Required, StringLength(64), RegularExpression("^[A-Za-z0-9_-]+$")] string InvoiceId,
    [Required, StringLength(64), RegularExpression("^[A-Za-z0-9_-]+$")] string TenantId,
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")] decimal Amount,
    [Required, StringLength(64)] string ErpSystem);
