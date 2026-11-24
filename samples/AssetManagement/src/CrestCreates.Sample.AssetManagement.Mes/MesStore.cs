using CrestCreates.Sample.AssetManagement.Contracts.Mes;
using Microsoft.Data.Sqlite;

namespace CrestCreates.Sample.AssetManagement.Mes;

internal readonly record struct ProductionLockKey(string TenantId, Guid EquipmentId, string WorkOrderId);

internal sealed class MesRejection(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
}

internal sealed class MesStore(string connectionString, TimeProvider clock, TimeSpan evidenceMaxAge)
{
    private const string ObservationColumns = "o.tenant_id, o.equipment_id, o.work_order_id, o.observation_id, o.source_event_id, o.outcome, o.observed_at";

    public void Initialize()
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = Command(connection, transaction, """
            CREATE TABLE IF NOT EXISTS production_locks (
                tenant_id TEXT NOT NULL,
                equipment_id TEXT NOT NULL,
                work_order_id TEXT NOT NULL,
                registered_at INTEGER NOT NULL,
                PRIMARY KEY (tenant_id, equipment_id, work_order_id),
                UNIQUE (tenant_id, work_order_id)
            );
            CREATE TABLE IF NOT EXISTS trial_observations (
                tenant_id TEXT NOT NULL,
                equipment_id TEXT NOT NULL,
                work_order_id TEXT NOT NULL,
                observation_id TEXT NOT NULL,
                source_event_id TEXT NOT NULL UNIQUE,
                outcome INTEGER NOT NULL CHECK (outcome IN (1, 2)),
                observed_at INTEGER NOT NULL,
                PRIMARY KEY (tenant_id, observation_id),
                UNIQUE (tenant_id, equipment_id, work_order_id, observation_id, source_event_id),
                FOREIGN KEY (tenant_id, equipment_id, work_order_id)
                    REFERENCES production_locks (tenant_id, equipment_id, work_order_id)
            );
            CREATE TABLE IF NOT EXISTS production_releases (
                tenant_id TEXT NOT NULL,
                equipment_id TEXT NOT NULL,
                work_order_id TEXT NOT NULL,
                operation_id TEXT NOT NULL,
                observation_id TEXT NOT NULL,
                source_event_id TEXT NOT NULL,
                released_at INTEGER NOT NULL,
                PRIMARY KEY (tenant_id, equipment_id, work_order_id),
                UNIQUE (tenant_id, operation_id),
                FOREIGN KEY (tenant_id, equipment_id, work_order_id, observation_id, source_event_id)
                    REFERENCES trial_observations (tenant_id, equipment_id, work_order_id, observation_id, source_event_id)
            );
            """);
        command.ExecuteNonQuery();

        // Existing observations already carry a durable source event ID and start unacknowledged.
        using var hasAcknowledgementColumn = Command(connection, transaction, """
            SELECT 1 FROM pragma_table_info('trial_observations') WHERE name = 'event_acknowledged_at'
            """);
        if (hasAcknowledgementColumn.ExecuteScalar() is null)
        {
            using var upgrade = Command(connection, transaction, """
                ALTER TABLE trial_observations ADD COLUMN event_acknowledged_at INTEGER
                """);
            upgrade.ExecuteNonQuery();
        }

        using var pendingIndex = Command(connection, transaction, """
            CREATE INDEX IF NOT EXISTS trial_observations_pending_events ON trial_observations (tenant_id)
            WHERE event_acknowledged_at IS NULL
            """);
        pendingIndex.ExecuteNonQuery();
        transaction.Commit();
    }

    public ProductionLockState Register(ProductionLockKey key)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = LockCommand(connection, transaction, key, """
            INSERT INTO production_locks (tenant_id, equipment_id, work_order_id, registered_at)
            VALUES ($tenant, $equipment, $workOrder, $registeredAt)
            ON CONFLICT (tenant_id, work_order_id) DO NOTHING
            """);
        command.Parameters.AddWithValue("$registeredAt", clock.GetUtcNow().UtcTicks);
        command.ExecuteNonQuery();
        var state = FindLock(connection, transaction, key)
            ?? throw new MesRejection(StatusCodes.Status409Conflict, "work_order_equipment_conflict");
        transaction.Commit();
        return state;
    }

    public TrialObservation RecordTrial(ProductionLockKey key, Guid observationId, RecordTrialRequest request)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        var observation = FindObservation(connection, transaction, key.TenantId, observationId);
        if (observation is not null)
        {
            if (!Matches(key, observation) || observation.Outcome != request.Outcome || observation.ObservedAt != request.ObservedAt)
                throw new MesRejection(StatusCodes.Status409Conflict, "observation_id_conflict");

            return observation;
        }

        var state = FindLock(connection, transaction, key) ?? throw LockNotFound();
        if (!state.IsLocked)
            throw new MesRejection(StatusCodes.Status409Conflict, "lock_already_released");

        var observedAt = request.ObservedAt.ToUniversalTime();
        if (observedAt < state.RegisteredAt || observedAt > clock.GetUtcNow())
            throw new MesRejection(StatusCodes.Status409Conflict, "observation_time_outside_lock");

        var sourceEventId = Guid.NewGuid();
        using var command = LockCommand(connection, transaction, key, """
            INSERT INTO trial_observations
                (tenant_id, equipment_id, work_order_id, observation_id, source_event_id, outcome, observed_at)
            VALUES ($tenant, $equipment, $workOrder, $observation, $sourceEvent, $outcome, $observedAt)
            """);
        AddId(command, "$observation", observationId);
        AddId(command, "$sourceEvent", sourceEventId);
        command.Parameters.AddWithValue("$outcome", (int)request.Outcome);
        command.Parameters.AddWithValue("$observedAt", observedAt.UtcTicks);
        command.ExecuteNonQuery();
        transaction.Commit();
        return new TrialObservation(key.TenantId, key.EquipmentId, key.WorkOrderId, observationId, sourceEventId, request.Outcome, observedAt);
    }

    public TrialObservation ValidateReleaseEvidence(ProductionLockKey key, Guid observationId)
    {
        using var connection = Open();
        var state = FindLock(connection, null, key) ?? throw LockNotFound();
        if (!state.IsLocked)
            throw new MesRejection(StatusCodes.Status409Conflict, "lock_already_released");
        var evidence = RequireMatchingEvidence(key, FindObservation(connection, null, key.TenantId, observationId));
        if (evidence.Outcome != TrialOutcome.Passed)
            throw new MesRejection(StatusCodes.Status409Conflict, "evidence_failed");
        var now = clock.GetUtcNow().ToUniversalTime();
        if (evidence.ObservedAt < state.RegisteredAt || evidence.ObservedAt > now || now - evidence.ObservedAt > evidenceMaxAge)
            throw new MesRejection(StatusCodes.Status409Conflict, "evidence_outside_freshness_window");
        return evidence;
    }

    public ProductionReleaseReceipt Release(ProductionLockKey key, ReleaseProductionLockRequest request)
    {
        using var connection = Open();
        // SQLite serializes writers before either the idempotency lookup or the evidence checks.
        using var transaction = connection.BeginTransaction(deferred: false);
        var replay = FindRelease(connection, transaction, key.TenantId, request.OperationId);
        if (replay is not null)
        {
            if (!Matches(key, replay.Evidence) || !Matches(request, replay.Evidence))
                throw new MesRejection(StatusCodes.Status409Conflict, "operation_id_conflict");

            return replay;
        }

        var state = FindLock(connection, transaction, key) ?? throw LockNotFound();
        if (!state.IsLocked)
            throw new MesRejection(StatusCodes.Status409Conflict, "lock_already_released");

        var evidence = RequireMatchingEvidence(key, FindObservation(connection, transaction, key.TenantId, request.ObservationId));
        if (!Matches(request, evidence))
            throw EvidenceNotFound();
        if (evidence.Outcome != TrialOutcome.Passed)
            throw new MesRejection(StatusCodes.Status409Conflict, "evidence_failed");

        var now = clock.GetUtcNow().ToUniversalTime();
        if (evidence.ObservedAt < state.RegisteredAt || evidence.ObservedAt > now || now - evidence.ObservedAt > evidenceMaxAge)
            throw new MesRejection(StatusCodes.Status409Conflict, "evidence_outside_freshness_window");

        using var command = LockCommand(connection, transaction, key, """
            INSERT INTO production_releases
                (tenant_id, equipment_id, work_order_id, operation_id, observation_id, source_event_id, released_at)
            VALUES ($tenant, $equipment, $workOrder, $operation, $observation, $sourceEvent, $releasedAt)
            """);
        AddId(command, "$operation", request.OperationId);
        AddId(command, "$observation", evidence.ObservationId);
        AddId(command, "$sourceEvent", evidence.SourceEventId);
        command.Parameters.AddWithValue("$releasedAt", now.UtcTicks);
        command.ExecuteNonQuery();
        transaction.Commit();
        return new ProductionReleaseReceipt(request.OperationId, evidence, now);
    }

    public ProductionReleaseReceipt? FindRelease(ProductionLockKey key, Guid operationId)
    {
        using var connection = Open();
        return FindRelease(connection, null, key.TenantId, operationId);
    }

    public ProductionLockState GetLock(ProductionLockKey key)
    {
        using var connection = Open();
        return FindLock(connection, null, key) ?? throw LockNotFound();
    }

    public TrialObservation GetObservation(ProductionLockKey key, Guid observationId)
    {
        using var connection = Open();
        return RequireMatchingEvidence(key, FindObservation(connection, null, key.TenantId, observationId));
    }

    public TrialObservation[] ReadPendingEvents(string tenantId, Guid? after, int limit)
    {
        using var connection = Open();
        var afterRowId = 0L;
        if (after is { } sourceEventId)
        {
            using var cursor = Command(connection, null, """
                SELECT rowid FROM trial_observations WHERE tenant_id = $tenant AND source_event_id = $sourceEvent
                """);
            cursor.Parameters.AddWithValue("$tenant", tenantId);
            AddId(cursor, "$sourceEvent", sourceEventId);
            afterRowId = cursor.ExecuteScalar() is long rowId ? rowId : throw EventNotFound();
        }

        using var command = Command(connection, null, $"""
            SELECT {ObservationColumns} FROM trial_observations o
            WHERE o.tenant_id = $tenant AND o.event_acknowledged_at IS NULL AND o.rowid > $after
            ORDER BY o.rowid
            LIMIT $limit
            """);
        command.Parameters.AddWithValue("$tenant", tenantId);
        command.Parameters.AddWithValue("$after", afterRowId);
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        var observations = new List<TrialObservation>(limit);
        while (reader.Read())
            observations.Add(ReadObservation(reader, 0));

        return observations.ToArray();
    }

    public void AcknowledgeEvent(string tenantId, Guid sourceEventId)
    {
        using var connection = Open();
        using var command = Command(connection, null, """
            UPDATE trial_observations
            SET event_acknowledged_at = COALESCE(event_acknowledged_at, $acknowledgedAt)
            WHERE tenant_id = $tenant AND source_event_id = $sourceEvent
            """);
        command.Parameters.AddWithValue("$tenant", tenantId);
        AddId(command, "$sourceEvent", sourceEventId);
        command.Parameters.AddWithValue("$acknowledgedAt", clock.GetUtcNow().UtcTicks);
        if (command.ExecuteNonQuery() == 0)
            throw EventNotFound();
    }

    private static ProductionLockState? FindLock(SqliteConnection connection, SqliteTransaction? transaction, ProductionLockKey key)
    {
        using var command = LockCommand(connection, transaction, key, $"""
            SELECT l.registered_at, r.operation_id, r.released_at, {ObservationColumns}
            FROM production_locks l
            LEFT JOIN production_releases r USING (tenant_id, equipment_id, work_order_id)
            LEFT JOIN trial_observations o USING (tenant_id, equipment_id, work_order_id, observation_id, source_event_id)
            WHERE l.tenant_id = $tenant AND l.equipment_id = $equipment AND l.work_order_id = $workOrder
            """);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        var receipt = reader.IsDBNull(1) ? null : ReadReceipt(reader, 1);
        return new ProductionLockState(key.TenantId, key.EquipmentId, key.WorkOrderId, ReadTime(reader, 0), receipt);
    }

    private static TrialObservation? FindObservation(SqliteConnection connection, SqliteTransaction? transaction, string tenantId, Guid observationId)
    {
        using var command = Command(connection, transaction, $"""
            SELECT {ObservationColumns} FROM trial_observations o
            WHERE o.tenant_id = $tenant AND o.observation_id = $observation
            """);
        command.Parameters.AddWithValue("$tenant", tenantId);
        AddId(command, "$observation", observationId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadObservation(reader, 0) : null;
    }

    private static ProductionReleaseReceipt? FindRelease(SqliteConnection connection, SqliteTransaction? transaction, string tenantId, Guid operationId)
    {
        using var command = Command(connection, transaction, $"""
            SELECT r.operation_id, r.released_at, {ObservationColumns}
            FROM production_releases r
            JOIN trial_observations o USING (tenant_id, equipment_id, work_order_id, observation_id, source_event_id)
            WHERE r.tenant_id = $tenant AND r.operation_id = $operation
            """);
        command.Parameters.AddWithValue("$tenant", tenantId);
        AddId(command, "$operation", operationId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadReceipt(reader, 0) : null;
    }

    private static ProductionReleaseReceipt ReadReceipt(SqliteDataReader reader, int offset) =>
        new(reader.GetGuid(offset), ReadObservation(reader, offset + 2), ReadTime(reader, offset + 1));

    private static TrialObservation ReadObservation(SqliteDataReader reader, int offset) =>
        new(reader.GetString(offset), reader.GetGuid(offset + 1), reader.GetString(offset + 2),
            reader.GetGuid(offset + 3), reader.GetGuid(offset + 4), (TrialOutcome)reader.GetInt32(offset + 5), ReadTime(reader, offset + 6));

    private static DateTimeOffset ReadTime(SqliteDataReader reader, int column) => new(reader.GetInt64(column), TimeSpan.Zero);

    private static bool Matches(ProductionLockKey key, TrialObservation evidence) =>
        key.TenantId == evidence.TenantId && key.EquipmentId == evidence.EquipmentId && key.WorkOrderId == evidence.WorkOrderId;

    private static bool Matches(ReleaseProductionLockRequest request, TrialObservation evidence) =>
        request.ObservationId == evidence.ObservationId && request.SourceEventId == evidence.SourceEventId;

    private static TrialObservation RequireMatchingEvidence(ProductionLockKey key, TrialObservation? evidence) =>
        evidence is not null && Matches(key, evidence) ? evidence : throw EvidenceNotFound();

    private static MesRejection LockNotFound() => new MesRejection(StatusCodes.Status404NotFound, "lock_not_found");

    private static MesRejection EvidenceNotFound() => new(StatusCodes.Status404NotFound, "evidence_not_found");

    private static MesRejection EventNotFound() => new(StatusCodes.Status404NotFound, "trial_event_not_found");

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    private static SqliteCommand LockCommand(SqliteConnection connection, SqliteTransaction? transaction, ProductionLockKey key, string sql)
    {
        var command = Command(connection, transaction, sql);
        command.Parameters.AddWithValue("$tenant", key.TenantId);
        AddId(command, "$equipment", key.EquipmentId);
        command.Parameters.AddWithValue("$workOrder", key.WorkOrderId);
        return command;
    }

    private static void AddId(SqliteCommand command, string parameter, Guid id) => command.Parameters.AddWithValue(parameter, id.ToString("D"));
}
