using System.Security.Claims;
using Accounting.Api.Models;
using Accounting.Api.Publishing;
using Accounting.Api.Status;
using Accounting.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Controllers;

[ApiController]
[Authorize(Policy = SubmitPolicy)]
[Route("api/v1/accounting/invoices")]
public sealed class InvoicesController(
    IEventPublisher publisher,
    InvoiceStatusStore statusStore,
    ILogger<InvoicesController> logger) : ControllerBase
{
    public const string SubmitPolicy = "InvoiceSubmitter";

    private string? CallerTenantId => User.FindFirstValue("tenant_id");

    [HttpPost("sync")]
    public async Task<IActionResult> Sync(SyncInvoiceRequest request, CancellationToken cancellationToken)
    {
        if (request.TenantId != CallerTenantId)
        {
            return Problem(
                title: $"You can only submit invoices for your own hotel ({CallerTenantId ?? "none"}).",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var invoiceCreated = new InvoiceCreatedEvent(
            Guid.NewGuid(),
            request.InvoiceId,
            request.TenantId,
            request.Amount,
            request.ErpSystem,
            User.Identity?.Name,
            DateTimeOffset.UtcNow);

        try
        {
            await publisher.PublishAsync(invoiceCreated, cancellationToken);
        }
        catch (EventPublishException ex)
        {
            logger.LogError(ex, "Could not publish invoice {InvoiceId} for tenant {TenantId}.", request.InvoiceId, request.TenantId);
            return Problem(
                title: "The invoice could not be queued. Please retry.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Accepted(new SyncInvoiceResponse(invoiceCreated.EventId, request.InvoiceId, "Queued"));
    }

    [HttpGet("events/{eventId:guid}/status")]
    public IActionResult GetStatus(Guid eventId)
    {
        var status = statusStore.Find(eventId);
        if (status is null)
        {
            return Ok(new InvoiceStatusResponse(eventId, "Pending", null, null));
        }

        if (status.TenantId != CallerTenantId)
        {
            return NotFound();
        }

        return Ok(new InvoiceStatusResponse(eventId, status.Status.ToString(), status.Attempt, status.OccurredAtUtc));
    }
}
