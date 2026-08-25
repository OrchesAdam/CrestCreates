using CrestCreates.Runtime.Delivery.Abstractions.Stores;

namespace CrestCreates.Runtime.Persistence.Testing.Cases;

public static class OutboxDispatchContractCases
{
    public static async Task EmptyClaimUsesProviderClockAsync(IOutboxDispatchStore store)
    {
        var now = await store.GetProviderUtcNowAsync();
        if (now == default)
            throw new InvalidOperationException("Outbox provider clock must return a UTC instant.");

        var claims = await store.ClaimAsync(new OutboxClaimRequest
        {
            OwnerId = "shared-outbox-contract",
            BatchSize = 1,
            LeaseDuration = TimeSpan.FromMinutes(1),
            SupportedContractIds = new HashSet<string>(StringComparer.Ordinal),
            SupportedRequiredConsumerIds = new HashSet<string>(StringComparer.Ordinal)
        });
        if (claims.Count != 0)
            throw new InvalidOperationException("An empty outbox must produce no claims.");
    }
}
