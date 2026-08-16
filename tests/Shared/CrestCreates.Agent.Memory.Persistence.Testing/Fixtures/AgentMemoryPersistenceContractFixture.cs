namespace CrestCreates.Agent.Memory.Persistence.Testing.Fixtures;

/// <summary>
/// Runner-free fixture contract for one provider backend. Concrete runners
/// derive from this to compose their real stores and reset durable state.
/// </summary>
public abstract class AgentMemoryPersistenceContractFixture : IAsyncDisposable
{
    protected AgentMemoryPersistenceContractFixture(string scopeId)
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
