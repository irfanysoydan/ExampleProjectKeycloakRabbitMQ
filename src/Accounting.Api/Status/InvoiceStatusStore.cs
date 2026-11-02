using Accounting.Contracts;
using Microsoft.Extensions.Caching.Memory;

namespace Accounting.Api.Status;

public sealed class InvoiceStatusStore(IMemoryCache cache)
{
    private static readonly TimeSpan Retention = TimeSpan.FromHours(1);

    public void Save(InvoiceStatusEvent status) => cache.Set(status.EventId, status, Retention);

    public InvoiceStatusEvent? Find(Guid eventId) => cache.Get<InvoiceStatusEvent>(eventId);
}
