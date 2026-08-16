using Npgsql;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

/// <summary>
/// Deterministic transaction-scoped advisory lock management for Agent Memory
/// durable identities. Lock identity text is exactly
/// <c>agent-memory | tenant | artifact-kind | artifact-id</c>; multiple
/// identities are ordinally sorted and de-duplicated before acquisition.
/// </summary>
internal sealed class PostgreSqlAgentMemoryLockManager
{
    private readonly PostgreSqlRuntimePersistenceOptions _options;
    private readonly PostgreSqlRuntimeTransactionCoordinator _coordinator;

    public PostgreSqlAgentMemoryLockManager(
        PostgreSqlRuntimePersistenceOptions options,
        PostgreSqlRuntimeTransactionCoordinator coordinator)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    public async ValueTask AcquireAsync(
        PostgreSqlRuntimeSession session,
        string tenantId,
        string artifactKind,
        IReadOnlyList<string> artifactIds,
        CancellationToken cancellationToken = default)
    {
        if (artifactIds.Count == 0)
            return;
        var identities = artifactIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .Select(id => $"agent-memory | {tenantId} | {artifactKind} | {id}")
            .ToArray();
        if (identities.Length == 0)
            return;

        await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, BuildLockSql(identities.Length));
        for (var index = 0; index < identities.Length; index++)
            command.Parameters.AddWithValue($"key{index}", identities[index]);
        using var lease = session.EnterCommand();
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string BuildLockSql(int count)
    {
        var clauses = new List<string>(count);
        for (var index = 0; index < count; index++)
            clauses.Add($"select pg_advisory_xact_lock(hashtextextended(@key{index}, 0));");
        return string.Join(Environment.NewLine, clauses);
    }
}
