namespace CrestCreates.Runtime.Persistence.Testing.Fixtures;

public abstract class RuntimePersistenceContractFixture : IAsyncDisposable
{
    protected RuntimePersistenceContractFixture(string scopeId)
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
