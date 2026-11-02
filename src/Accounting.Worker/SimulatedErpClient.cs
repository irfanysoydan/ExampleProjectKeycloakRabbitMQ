using Accounting.Contracts;

namespace Accounting.Worker;

public sealed class ErpUnavailableException(string message) : Exception(message);

public static class SimulatedErpClient
{
    public const string FlakyErp = "Demo-Flaky";
    public const string DownErp = "Demo-Down";

    public static async Task TransferAsync(InvoiceCreatedEvent invoice, int attempt, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);

        if (invoice.ErpSystem == DownErp || (invoice.ErpSystem == FlakyErp && attempt < 3))
        {
            throw new ErpUnavailableException($"{invoice.ErpSystem} did not respond.");
        }
    }
}
