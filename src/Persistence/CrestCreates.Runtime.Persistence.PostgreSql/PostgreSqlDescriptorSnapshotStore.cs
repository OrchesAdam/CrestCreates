using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Persistence;
using Npgsql;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

internal sealed class PostgreSqlDescriptorSnapshotStore : IDescriptorSnapshotStore
{
    private readonly PostgreSqlRuntimePersistenceOptions _options;
    private readonly PostgreSqlRuntimeTransactionCoordinator _coordinator;
    private readonly IDescriptorSnapshotPersistenceHasher _hasher;
    private readonly string _snapshots;
    private readonly string _entries;

    public PostgreSqlDescriptorSnapshotStore(
        PostgreSqlRuntimePersistenceOptions options,
        PostgreSqlRuntimeTransactionCoordinator coordinator,
        IDescriptorSnapshotPersistenceHasher hasher)
    {
        _options = options;
        _coordinator = coordinator;
        _hasher = hasher;
        _snapshots = PostgreSqlRuntimeStoreSupport.Table(options, "descriptor_snapshots");
        _entries = PostgreSqlRuntimeStoreSupport.Table(options, "descriptor_snapshot_entries");
    }

    public Task<DescriptorSnapshotWriteResult> WriteAsync(DescriptorSnapshot snapshot, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => new ValueTask<DescriptorSnapshotWriteResult>(WriteCoreAsync(snapshot, ct)), cancellationToken).AsTask();

    public Task<DescriptorSnapshot?> GetAsync(string snapshotId, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => new ValueTask<DescriptorSnapshot?>(GetCoreAsync(snapshotId, ct)), cancellationToken).AsTask();

    public Task<SnapshotEntry?> GetEntryAsync(string snapshotId, DescriptorRef descriptorRef, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => new ValueTask<SnapshotEntry?>(GetEntryCoreAsync(snapshotId, descriptorRef, ct)), cancellationToken).AsTask();

    private async Task<DescriptorSnapshotWriteResult> WriteCoreAsync(DescriptorSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var copy = snapshot.Snapshot();
        ArgumentException.ThrowIfNullOrWhiteSpace(copy.SnapshotId);
        var json = PostgreSqlRuntimeStoreSupport.Serialize(copy, PostgreSqlRuntimeJsonSerializerContext.Default.DescriptorSnapshot);
        var hash = _hasher.Compute(copy).Digest;
        bool accepted;
        {
            var session = _coordinator.RequireSession();
            using var lease = session.EnterCommand();
            await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options,
                $"insert into {_snapshots} (snapshot_id, content_hash, snapshot_json) values (@id, @hash, @json) on conflict (snapshot_id) do nothing returning snapshot_id;");
            command.Parameters.AddWithValue("id", copy.SnapshotId);
            command.Parameters.AddWithValue("hash", hash);
            PostgreSqlRuntimeStoreSupport.AddJson(command, "json", json);
            accepted = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
        }

        if (!accepted)
        {
            var existing = await GetHashAsync(copy.SnapshotId, cancellationToken).ConfigureAwait(false);
            return new DescriptorSnapshotWriteResult(
                string.Equals(existing, hash, StringComparison.Ordinal)
                    ? DescriptorSnapshotWriteStatus.Duplicate
                    : DescriptorSnapshotWriteStatus.Conflict,
                copy.SnapshotId);
        }

        foreach (var entry in copy.Descriptors.OrderBy(x => x.Ref.Namespace, StringComparer.Ordinal).ThenBy(x => x.Ref.Id, StringComparer.Ordinal).ThenBy(x => x.Ref.Version))
        {
            var session = _coordinator.RequireSession();
            using var lease = session.EnterCommand();
            await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options,
                $"insert into {_entries} (snapshot_id, descriptor_namespace, descriptor_id, descriptor_version, contract_hash, definition_hash) values (@snapshot, @namespace, @id, @version, @contract, @definition);");
            command.Parameters.AddWithValue("snapshot", copy.SnapshotId);
            command.Parameters.AddWithValue("namespace", entry.Ref.Namespace);
            command.Parameters.AddWithValue("id", entry.Ref.Id);
            command.Parameters.AddWithValue("version", entry.Ref.Version ?? 0);
            command.Parameters.AddWithValue("contract", entry.ContractHash);
            command.Parameters.AddWithValue("definition", entry.DefinitionHash);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        return new DescriptorSnapshotWriteResult(DescriptorSnapshotWriteStatus.Accepted, copy.SnapshotId);
    }

    private async Task<DescriptorSnapshot?> GetCoreAsync(string snapshotId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotId);
        var session = _coordinator.RequireSession();
        using var lease = session.EnterCommand();
        await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options,
            $"select snapshot_json::text from {_snapshots} where snapshot_id=@id;");
        command.Parameters.AddWithValue("id", snapshotId);
        var json = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        return json is null
            ? null
            : PostgreSqlRuntimeStoreSupport.Deserialize(json, PostgreSqlRuntimeJsonSerializerContext.Default.DescriptorSnapshot).Snapshot();
    }

    private async Task<SnapshotEntry?> GetEntryCoreAsync(string snapshotId, DescriptorRef descriptorRef, CancellationToken cancellationToken)
    {
        var snapshot = await GetCoreAsync(snapshotId, cancellationToken).ConfigureAwait(false);
        return snapshot?.Descriptors.FirstOrDefault(x => x.Ref == descriptorRef)?.Snapshot();
    }

    private async Task<string?> GetHashAsync(string snapshotId, CancellationToken cancellationToken)
    {
        var session = _coordinator.RequireSession();
        using var lease = session.EnterCommand();
        await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options,
            $"select content_hash from {_snapshots} where snapshot_id=@id;");
        command.Parameters.AddWithValue("id", snapshotId);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
    }
}
