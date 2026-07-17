using System.Text.Json;
using Microsoft.Data.Sqlite;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Samples.DescriptorControlPlane;

public sealed class SqliteHumanTaskInstanceStore : IHumanTaskInstanceStore
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteHumanTaskInstanceStore(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task SaveAsync(HumanTaskInstance instance, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);

        try
        {
            // Check if instance exists (protected by transaction)
            using var checkCmd = connection.CreateCommand();
            checkCmd.Transaction = transaction;
            checkCmd.CommandText = "SELECT concurrency_stamp FROM human_task_instances WHERE id = @id";
            checkCmd.Parameters.AddWithValue("@id", instance.Id);
            var existingStamp = await checkCmd.ExecuteScalarAsync(ct);

            var newStamp = Guid.NewGuid().ToString("N");
            var updatedAt = DateTimeOffset.UtcNow;

            if (existingStamp is null)
            {
                // Insert
                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = """
                    INSERT INTO human_task_instances
                        (id, human_task_id, human_task_version, status, tenant_id,
                         assignee_user_id, assignee_role_id,
                         workflow_instance_id, workflow_step_id,
                         input, output, outcome,
                         created_at, updated_at, completed_at, cancelled_at, cancellation_reason,
                         candidate_user_ids, candidate_role_ids,
                         organization_unit_id, position_id, assignee_resolution_reason,
                         concurrency_stamp)
                    VALUES
                        (@id, @human_task_id, @human_task_version, @status, @tenant_id,
                         @assignee_user_id, @assignee_role_id,
                         @workflow_instance_id, @workflow_step_id,
                         @input, @output, @outcome,
                         @created_at, @updated_at, @completed_at, @cancelled_at, @cancellation_reason,
                         @candidate_user_ids, @candidate_role_ids,
                         @organization_unit_id, @position_id, @assignee_resolution_reason,
                         @concurrency_stamp)
                    """;
                AddHumanTaskParameters(cmd, instance, newStamp, updatedAt);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            else
            {
                // Update with optimistic concurrency
                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = """
                    UPDATE human_task_instances
                    SET human_task_id = @human_task_id,
                        human_task_version = @human_task_version,
                        status = @status,
                        tenant_id = @tenant_id,
                        assignee_user_id = @assignee_user_id,
                        assignee_role_id = @assignee_role_id,
                        workflow_instance_id = @workflow_instance_id,
                        workflow_step_id = @workflow_step_id,
                        input = @input,
                        output = @output,
                        outcome = @outcome,
                        created_at = @created_at,
                        updated_at = @updated_at,
                        completed_at = @completed_at,
                        cancelled_at = @cancelled_at,
                        cancellation_reason = @cancellation_reason,
                        candidate_user_ids = @candidate_user_ids,
                        candidate_role_ids = @candidate_role_ids,
                        organization_unit_id = @organization_unit_id,
                        position_id = @position_id,
                        assignee_resolution_reason = @assignee_resolution_reason,
                        concurrency_stamp = @concurrency_stamp
                    WHERE id = @id
                      AND concurrency_stamp = @expected_stamp
                    """;
                cmd.Parameters.AddWithValue("@expected_stamp", instance.ConcurrencyStamp);
                AddHumanTaskParameters(cmd, instance, newStamp, updatedAt);

                var affected = await cmd.ExecuteNonQueryAsync(ct);
                if (affected == 0)
                {
                    using var recheckCmd = connection.CreateCommand();
                    recheckCmd.Transaction = transaction;
                    recheckCmd.CommandText = "SELECT concurrency_stamp FROM human_task_instances WHERE id = @id";
                    recheckCmd.Parameters.AddWithValue("@id", instance.Id);
                    var currentStamp = await recheckCmd.ExecuteScalarAsync(ct);
                    if (currentStamp is null)
                        throw new RuntimeEntityNotFoundException(
                            $"HumanTaskInstance '{instance.Id}' not found.");
                    throw new RuntimeConcurrencyException(
                        $"Concurrency conflict for HumanTaskInstance '{instance.Id}'. " +
                        $"Expected stamp '{instance.ConcurrencyStamp}', actual '{currentStamp}'.");
                }
            }

            await transaction.CommitAsync(ct);

            instance.ConcurrencyStamp = newStamp;
            instance.UpdatedAt = updatedAt;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<HumanTaskInstance?> GetByIdAsync(
        string instanceId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, human_task_id, human_task_version, status, tenant_id,
                   assignee_user_id, assignee_role_id,
                   workflow_instance_id, workflow_step_id,
                   input, output, outcome,
                   created_at, updated_at, completed_at, cancelled_at, cancellation_reason,
                   candidate_user_ids, candidate_role_ids,
                   organization_unit_id, position_id, assignee_resolution_reason,
                   concurrency_stamp
            FROM human_task_instances
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("@id", instanceId);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return ReadHumanTaskInstance(reader);
    }

    public async Task<IReadOnlyList<HumanTaskInstance>> GetPendingByAssigneeAsync(
        string assigneeUserId, CancellationToken ct = default)
    {
        return await QueryPendingAsync("assignee_user_id = @assignee_user_id",
            cmd => cmd.Parameters.AddWithValue("@assignee_user_id", assigneeUserId), ct);
    }

    public async Task<IReadOnlyList<HumanTaskInstance>> GetPendingByWorkflowAsync(
        string workflowInstanceId, CancellationToken ct = default)
    {
        return await QueryPendingAsync("workflow_instance_id = @workflow_instance_id",
            cmd => cmd.Parameters.AddWithValue("@workflow_instance_id", workflowInstanceId), ct);
    }

    public async Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateUserAsync(
        string userId, CancellationToken ct = default)
    {
        return await QueryPendingAsync("candidate_user_ids LIKE @pattern",
            cmd => cmd.Parameters.AddWithValue("@pattern", $"%\"{userId}\"%"), ct);
    }

    public async Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateRoleAsync(
        string roleId, CancellationToken ct = default)
    {
        return await QueryPendingAsync("candidate_role_ids LIKE @pattern",
            cmd => cmd.Parameters.AddWithValue("@pattern", $"%\"{roleId}\"%"), ct);
    }

    public async Task<IReadOnlyList<HumanTaskInstance>> GetPendingByOrganizationAsync(
        string organizationUnitId, CancellationToken ct = default)
    {
        return await QueryPendingAsync("organization_unit_id = @org_id",
            cmd => cmd.Parameters.AddWithValue("@org_id", organizationUnitId), ct);
    }

    public async Task<IReadOnlyList<HumanTaskInstance>> GetPendingByPositionAsync(
        string positionId, CancellationToken ct = default)
    {
        return await QueryPendingAsync("position_id = @position_id",
            cmd => cmd.Parameters.AddWithValue("@position_id", positionId), ct);
    }

    private async Task<IReadOnlyList<HumanTaskInstance>> QueryPendingAsync(
        string whereClause, Action<SqliteCommand> addParams, CancellationToken ct)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT id, human_task_id, human_task_version, status, tenant_id,
                   assignee_user_id, assignee_role_id,
                   workflow_instance_id, workflow_step_id,
                   input, output, outcome,
                   created_at, updated_at, completed_at, cancelled_at, cancellation_reason,
                   candidate_user_ids, candidate_role_ids,
                   organization_unit_id, position_id, assignee_resolution_reason,
                   concurrency_stamp
            FROM human_task_instances
            WHERE status IN ({(int)HumanTaskInstanceStatus.Created}, {(int)HumanTaskInstanceStatus.Assigned})
              AND {whereClause}
            """;
        addParams(cmd);

        var results = new List<HumanTaskInstance>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(ReadHumanTaskInstance(reader));

        return results.AsReadOnly();
    }

    private static void AddHumanTaskParameters(
        SqliteCommand cmd, HumanTaskInstance instance, string newStamp, DateTimeOffset updatedAt)
    {
        cmd.Parameters.AddWithValue("@id", instance.Id);
        cmd.Parameters.AddWithValue("@human_task_id", instance.HumanTaskId);
        cmd.Parameters.AddWithValue("@human_task_version", instance.HumanTaskVersion);
        cmd.Parameters.AddWithValue("@status", (int)instance.Status);
        cmd.Parameters.AddWithValue("@tenant_id", (object?)instance.TenantId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@assignee_user_id", (object?)instance.AssigneeUserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@assignee_role_id", (object?)instance.AssigneeRoleId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@workflow_instance_id", (object?)instance.WorkflowInstanceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@workflow_step_id", (object?)instance.WorkflowStepId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@input", SerializeObject(instance.Input) ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@output", SerializeObject(instance.Output) ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@outcome", (object?)instance.Outcome ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@created_at", instance.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@updated_at", updatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@completed_at", instance.CompletedAt.HasValue
            ? instance.CompletedAt.Value.ToString("O") : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@cancelled_at", instance.CancelledAt.HasValue
            ? instance.CancelledAt.Value.ToString("O") : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@cancellation_reason", (object?)instance.CancellationReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@candidate_user_ids",
            SerializeStringList(instance.CandidateUserIds) ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@candidate_role_ids",
            SerializeStringList(instance.CandidateRoleIds) ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@organization_unit_id", (object?)instance.OrganizationUnitId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@position_id", (object?)instance.PositionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@assignee_resolution_reason",
            (object?)instance.AssigneeResolutionReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@concurrency_stamp", newStamp);
    }

    private static HumanTaskInstance ReadHumanTaskInstance(SqliteDataReader reader)
    {
        return new HumanTaskInstance
        {
            Id = reader.GetString(0),
            HumanTaskId = reader.GetString(1),
            HumanTaskVersion = reader.GetInt32(2),
            Status = (HumanTaskInstanceStatus)reader.GetInt32(3),
            TenantId = reader.IsDBNull(4) ? null : reader.GetString(4),
            AssigneeUserId = reader.IsDBNull(5) ? null : reader.GetString(5),
            AssigneeRoleId = reader.IsDBNull(6) ? null : reader.GetString(6),
            WorkflowInstanceId = reader.IsDBNull(7) ? null : reader.GetString(7),
            WorkflowStepId = reader.IsDBNull(8) ? null : reader.GetString(8),
            Input = DeserializeObject(reader.IsDBNull(9) ? null : reader.GetString(9)),
            Output = DeserializeObject(reader.IsDBNull(10) ? null : reader.GetString(10)),
            Outcome = reader.IsDBNull(11) ? null : reader.GetString(11),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(12), null, System.Globalization.DateTimeStyles.RoundtripKind),
            UpdatedAt = reader.IsDBNull(13) ? null : DateTimeOffset.Parse(reader.GetString(13), null, System.Globalization.DateTimeStyles.RoundtripKind),
            CompletedAt = reader.IsDBNull(14) ? null : DateTimeOffset.Parse(reader.GetString(14), null, System.Globalization.DateTimeStyles.RoundtripKind),
            CancelledAt = reader.IsDBNull(15) ? null : DateTimeOffset.Parse(reader.GetString(15), null, System.Globalization.DateTimeStyles.RoundtripKind),
            CancellationReason = reader.IsDBNull(16) ? null : reader.GetString(16),
            CandidateUserIds = DeserializeStringList(reader.IsDBNull(17) ? null : reader.GetString(17)),
            CandidateRoleIds = DeserializeStringList(reader.IsDBNull(18) ? null : reader.GetString(18)),
            OrganizationUnitId = reader.IsDBNull(19) ? null : reader.GetString(19),
            PositionId = reader.IsDBNull(20) ? null : reader.GetString(20),
            AssigneeResolutionReason = reader.IsDBNull(21) ? null : reader.GetString(21),
            ConcurrencyStamp = reader.GetString(22),
        };
    }

    private static string? SerializeObject(object? value)
    {
        if (value is null) return null;
        return JsonSerializer.Serialize(value, value.GetType(), SampleSqliteJsonContext.ReflectionOptions);
    }

    private static object? DeserializeObject(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        var raw = JsonSerializer.Deserialize<object?>(json, SampleSqliteJsonContext.ReflectionOptions);
        return SampleSqliteJsonContext.ConvertJsonElement(raw);
    }

    private static string? SerializeStringList(IReadOnlyList<string> list)
    {
        if (list.Count == 0) return null;
        return JsonSerializer.Serialize(list, SampleSqliteJsonContext.ReflectionOptions);
    }

    private static IReadOnlyList<string> DeserializeStringList(string? json)
    {
        if (string.IsNullOrEmpty(json)) return Array.Empty<string>();
        return (IReadOnlyList<string>?)JsonSerializer.Deserialize<List<string>>(json, SampleSqliteJsonContext.ReflectionOptions)
            ?? Array.Empty<string>();
    }
}
