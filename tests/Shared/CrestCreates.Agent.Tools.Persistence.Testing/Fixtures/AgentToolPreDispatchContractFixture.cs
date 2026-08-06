using CrestCreates.Agent.Tools;

namespace CrestCreates.Agent.Tools.Persistence.Testing.Fixtures;

public abstract class AgentToolPreDispatchContractFixture : IAsyncDisposable
{
    protected AgentToolPreDispatchContractFixture(string scopeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        ScopeId = scopeId;
    }

    public string ScopeId { get; }

    public abstract ValueTask ResetAsync(CancellationToken cancellationToken = default);

    public virtual async ValueTask DisposeAsync()
    {
        await ResetAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
