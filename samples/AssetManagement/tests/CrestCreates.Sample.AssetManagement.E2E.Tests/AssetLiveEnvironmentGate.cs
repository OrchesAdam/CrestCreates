using Xunit;

namespace CrestCreates.Sample.AssetManagement.E2E.Tests;

/// <summary>
/// Issue #133 R3: the opt-in live entry and the offline isolation regressions touch the same
/// process-wide live-evaluation variables. Every read of those variables by a live entry and every
/// temporary change made by an offline regression happens under this gate, so a live reader sees
/// either the caller's configuration or the restored configuration — never the value a regression
/// installs while it runs. The gate also holds for callers that invoke the two entries directly and
/// concurrently, i.e. outside the xUnit scheduler.
/// </summary>
internal static class AssetLiveEnvironmentGate
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly TimeSpan AcquisitionTimeout = TimeSpan.FromMinutes(2);

    internal static async Task<IAsyncDisposable> HoldAsync(string reader)
    {
        if (!await Gate.WaitAsync(AcquisitionTimeout).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"Timed out after {AcquisitionTimeout.TotalMinutes:0.#} minutes waiting for the live-evaluation " +
                $"environment gate ({reader}). Another reader or writer is still holding it.");
        }

        return new Releaser();
    }

    private sealed class Releaser : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            Gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// Serial scheduling for the test classes that read or temporarily change the live-evaluation
/// variables. They must not overlap each other even though xUnit parallelizes collections by default.
/// </summary>
[CollectionDefinition(Name)]
public sealed class AssetLiveEnvironmentCollection
{
    public const string Name = "Asset live evaluation environment";
}
