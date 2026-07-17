using Microsoft.Data.Sqlite;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Samples.DescriptorControlPlane;

public sealed class SqliteWorkflowInstanceStore : IWorkflowInstanceStore
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteWorkflowInstanceStore(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task SaveAsync(WorkflowInstance instance, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);

        try
        {
            // Check if instance exists (protected by transaction)
            using var checkCmd = connection.CreateCommand();
            checkCmd.Transaction = transaction;
            checkCmd.CommandText = "SELECT concurrency_stamp FROM workflow_instances WHERE instance_id = @id";
            checkCmd.Parameters.AddWithValue("@id", instance.InstanceId);
            var existingStamp = await checkCmd.ExecuteScalarAsync(ct);

            var newStamp = Guid.NewGuid().ToString("N");
            var updatedAt = DateTimeOffset.UtcNow;

            if (existingStamp is null)
            {
                // Insert
                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = """
                    INSERT INTO workflow_instances
                        (instance_id, workflow_descriptor_id, workflow_descriptor_version,
                         workflow_selection_mode, workflow_expected_contract_hash,
                         status, current_step_id, step_index, waiting_human_task_id,
                         started_at, updated_at, completed_at,
                         variables, step_variables, step_results, error_message, concurrency_stamp)
                    VALUES
                        (@instance_id, @workflow_descriptor_id, @workflow_descriptor_version,
                         @workflow_selection_mode, @workflow_expected_contract_hash,
                         @status, @current_step_id, @step_index, @waiting_human_task_id,
                         @started_at, @updated_at, @completed_at,
                         @variables, @step_variables, @step_results, @error_message, @concurrency_stamp)
                    """;
                AddWorkflowParameters(cmd, instance, newStamp, updatedAt);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            else
            {
                // Update with optimistic concurrency
                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = """
                    UPDATE workflow_instances
                    SET workflow_descriptor_id = @workflow_descriptor_id,
                        workflow_descriptor_version = @workflow_descriptor_version,
                        workflow_selection_mode = @workflow_selection_mode,
                        workflow_expected_contract_hash = @workflow_expected_contract_hash,
                        status = @status,
                        current_step_id = @current_step_id,
                        step_index = @step_index,
                        waiting_human_task_id = @waiting_human_task_id,
                        started_at = @started_at,
                        updated_at = @updated_at,
                        completed_at = @completed_at,
                        variables = @variables,
                        step_variables = @step_variables,
                        step_results = @step_results,
                        error_message = @error_message,
                        concurrency_stamp = @concurrency_stamp
                    WHERE instance_id = @instance_id
                      AND concurrency_stamp = @expected_stamp
                    """;
                cmd.Parameters.AddWithValue("@expected_stamp", instance.ConcurrencyStamp);
                AddWorkflowParameters(cmd, instance, newStamp, updatedAt);

                var affected = await cmd.ExecuteNonQueryAsync(ct);
                if (affected == 0)
                {
                    // Re-check to distinguish not-found vs concurrency conflict
                    using var recheckCmd = connection.CreateCommand();
                    recheckCmd.Transaction = transaction;
                    recheckCmd.CommandText = "SELECT concurrency_stamp FROM workflow_instances WHERE instance_id = @id";
                    recheckCmd.Parameters.AddWithValue("@id", instance.InstanceId);
                    var currentStamp = await recheckCmd.ExecuteScalarAsync(ct);
                    if (currentStamp is null)
                        throw new RuntimeEntityNotFoundException(
                            $"WorkflowInstance '{instance.InstanceId}' not found.");
                    throw new RuntimeConcurrencyException(
                        $"Concurrency conflict for WorkflowInstance '{instance.InstanceId}'. " +
                        $"Expected stamp '{instance.ConcurrencyStamp}', actual '{currentStamp}'.");
                }
            }

            await transaction.CommitAsync(ct);

            // Sync stamp and timestamp back to the caller's instance
            instance.ConcurrencyStamp = newStamp;
            instance.UpdatedAt = updatedAt;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<WorkflowInstance?> GetAsync(string instanceId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT instance_id, workflow_descriptor_id, workflow_descriptor_version,
                   workflow_selection_mode, workflow_expected_contract_hash,
                   status, current_step_id, step_index, waiting_human_task_id,
                   started_at, updated_at, completed_at,
                   variables, step_variables, step_results, error_message, concurrency_stamp
            FROM workflow_instances
            WHERE instance_id = @id
            """;
        cmd.Parameters.AddWithValue("@id", instanceId);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return ReadWorkflowInstance(reader);
    }

    public async Task<WorkflowInstance?> GetByWaitingHumanTaskId(
        string humanTaskId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT instance_id, workflow_descriptor_id, workflow_descriptor_version,
                   workflow_selection_mode, workflow_expected_contract_hash,
                   status, current_step_id, step_index, waiting_human_task_id,
                   started_at, updated_at, completed_at,
                   variables, step_variables, step_results, error_message, concurrency_stamp
            FROM workflow_instances
            WHERE waiting_human_task_id = @human_task_id
              AND status = @suspended_status
            """;
        cmd.Parameters.AddWithValue("@human_task_id", humanTaskId);
        cmd.Parameters.AddWithValue("@suspended_status", (int)WorkflowInstanceStatus.Suspended);

        var matches = new List<WorkflowInstance>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            matches.Add(ReadWorkflowInstance(reader));

        if (matches.Count > 1)
            throw new WorkflowCorrelationException(
                $"Multiple suspended instances found for HumanTask '{humanTaskId}'.");

        return matches.SingleOrDefault();
    }

    private static void AddWorkflowParameters(
        SqliteCommand cmd, WorkflowInstance instance, string newStamp, DateTimeOffset updatedAt)
    {
        cmd.Parameters.AddWithValue("@instance_id", instance.InstanceId);
        cmd.Parameters.AddWithValue("@workflow_descriptor_id", instance.Workflow.Id);
        cmd.Parameters.AddWithValue("@workflow_descriptor_version", instance.Workflow.Version);
        cmd.Parameters.AddWithValue("@workflow_selection_mode", (int)instance.Workflow.SelectionMode);
        cmd.Parameters.AddWithValue("@workflow_expected_contract_hash",
            (object?)instance.Workflow.ExpectedContractHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", (int)instance.Status);
        cmd.Parameters.AddWithValue("@current_step_id", (object?)instance.CurrentStepId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@step_index", instance.StepIndex);
        cmd.Parameters.AddWithValue("@waiting_human_task_id", (object?)instance.WaitingHumanTaskId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@started_at", instance.StartedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@updated_at", updatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@completed_at", instance.CompletedAt.HasValue
            ? instance.CompletedAt.Value.ToString("O") : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@variables",
            SampleSqliteJsonContext.SerializeDictionary(instance.Variables) ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@step_variables",
            SampleSqliteJsonContext.SerializeDictionary(instance.StepVariables) ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@step_results",
            SampleSqliteJsonContext.SerializeStepResults(instance.StepResults) ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@error_message", (object?)instance.ErrorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@concurrency_stamp", newStamp);
    }

    private static WorkflowInstance ReadWorkflowInstance(SqliteDataReader reader)
    {
        return new WorkflowInstance
        {
            InstanceId = reader.GetString(0),
            Workflow = new VersionedDescriptorRef<WorkflowDescriptor>(
                reader.GetString(1),
                reader.GetInt32(2),
                (VersionSelectionMode)reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)),
            Status = (WorkflowInstanceStatus)reader.GetInt32(5),
            CurrentStepId = reader.IsDBNull(6) ? null : reader.GetString(6),
            StepIndex = reader.GetInt32(7),
            WaitingHumanTaskId = reader.IsDBNull(8) ? null : reader.GetString(8),
            StartedAt = DateTimeOffset.Parse(reader.GetString(9), null, System.Globalization.DateTimeStyles.RoundtripKind),
            UpdatedAt = reader.IsDBNull(10) ? null : DateTimeOffset.Parse(reader.GetString(10), null, System.Globalization.DateTimeStyles.RoundtripKind),
            CompletedAt = reader.IsDBNull(11) ? null : DateTimeOffset.Parse(reader.GetString(11), null, System.Globalization.DateTimeStyles.RoundtripKind),
            Variables = SampleSqliteJsonContext.DeserializeDictionary(reader.IsDBNull(12) ? null : reader.GetString(12)),
            StepVariables = SampleSqliteJsonContext.DeserializeDictionary(reader.IsDBNull(13) ? null : reader.GetString(13)),
            StepResults = SampleSqliteJsonContext.DeserializeStepResults(reader.IsDBNull(14) ? null : reader.GetString(14)),
            ErrorMessage = reader.IsDBNull(15) ? null : reader.GetString(15),
            ConcurrencyStamp = reader.GetString(16),
        };
    }
}
