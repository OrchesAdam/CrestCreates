using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.Persistence;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

[Collection(PostgreSqlRuntimeCollection.Name)]
public sealed class PostgreSqlRuntimeIntegrationTests(PostgreSqlRuntimeCollectionFixture fixture)
{
    [Fact]
    public async Task ValidationOnly_OnEmptyDatabase_ShouldFailWithoutCreatingSchemaOrHistory()
    {
        var schema = $"itest_{Guid.NewGuid():N}";
        var options = new PostgreSqlRuntimePersistenceOptions { ConnectionString = fixture.ConnectionString, Schema = schema };
        var runner = new PostgreSqlRuntimeMigrationRunner(options);

        var act = () => runner.ApplyAsync(new PostgreSqlRuntimeMigrationOptions { ApplyMigrations = false });
        await act.Should().ThrowAsync<InvalidOperationException>();

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select exists (select 1 from information_schema.schemata where schema_name=@schema);", connection);
        command.Parameters.AddWithValue("schema", schema);
        (await command.ExecuteScalarAsync()).Should().Be(false);
    }

    [Fact]
    public async Task Migrations_ShouldCreateSchemaAndReapplyWithoutMutation()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        var runner = new PostgreSqlRuntimeMigrationRunner(lease.Options);

        await runner.ApplyAsync(new PostgreSqlRuntimeMigrationOptions { ApplyMigrations = true });
        await runner.ApplyAsync(new PostgreSqlRuntimeMigrationOptions { ApplyMigrations = false });

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select count(*) from information_schema.tables where table_schema=@schema and table_name like 'runtime_%';", connection);
        command.Parameters.AddWithValue("schema", lease.Options.Schema);
        (await command.ExecuteScalarAsync()).Should().Be(4L);
    }

    [Fact]
    public async Task MigrationHistoryTable_WithUnexpectedShape_ShouldFailClosed()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                $"alter table \"{lease.Options.Schema}\".crest_runtime_schema_migrations add column unexpected text;", connection);
            await command.ExecuteNonQueryAsync();
        }

        var runner = new PostgreSqlRuntimeMigrationRunner(lease.Options);
        var act = () => runner.ApplyAsync(new PostgreSqlRuntimeMigrationOptions { ApplyMigrations = false });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ChangedAppliedMigrationChecksum_ShouldFailClosed()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                $"update \"{lease.Options.Schema}\".crest_runtime_schema_migrations set checksum='tampered' where version='V001';", connection);
            await command.ExecuteNonQueryAsync();
        }

        var runner = new PostgreSqlRuntimeMigrationRunner(lease.Options);
        var act = () => runner.ApplyAsync(new PostgreSqlRuntimeMigrationOptions { ApplyMigrations = false });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SuspensionCommit_ShouldAtomicallyPersistWorkflowHumanTaskAndReceiptAcrossFreshProvider()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        using var provider = BuildProvider(lease.Options);
        var workflows = provider.GetRequiredService<IWorkflowInstanceStore>();
        var tasks = provider.GetRequiredService<IHumanTaskInstanceStore>();
        var receipts = provider.GetRequiredService<IWorkflowSuspensionReceiptStore>();
        var coordinator = provider.GetRequiredService<IRuntimeTransactionCoordinator>();
        var workflow = NewWorkflow("tenant-a", "workflow-1");
        var task = NewTask("tenant-a", "task-1", workflow.Key);
        await workflows.AddAsync(workflow);
        var persisted = (await workflows.GetAsync(workflow.Key))!;
        var suspended = persisted.Snapshot();
        suspended.Status = WorkflowInstanceStatus.Suspended;
        suspended.WaitingHumanTaskKey = task.Key;
        var receipt = NewReceipt(persisted, suspended, task, "suspend-1");

        var rollback = () => coordinator.ExecuteAsync(async cancellationToken =>
        {
            await tasks.AddAsync(task, cancellationToken);
            await workflows.UpdateAsync(suspended, persisted.Revision, cancellationToken);
            await receipts.AddAsync(receipt, cancellationToken);
            throw new InvalidOperationException("simulate failure after all writes");
        }).AsTask();
        await rollback.Should().ThrowAsync<InvalidOperationException>();
        (await tasks.GetAsync(task.Key)).Should().BeNull();
        (await workflows.GetAsync(workflow.Key))!.Status.Should().Be(WorkflowInstanceStatus.Running);
        (await receipts.GetAsync(new RuntimeTenantScope("tenant-a"), receipt.SuspensionOperationId)).Should().BeNull();

        await coordinator.ExecuteAsync(async cancellationToken =>
        {
            await tasks.AddAsync(task, cancellationToken);
            await workflows.UpdateAsync(suspended, persisted.Revision, cancellationToken);
            (await receipts.AddAsync(receipt, cancellationToken)).Status.Should().Be(WorkflowSuspensionReceiptWriteStatus.Accepted);
        });

        using var restarted = BuildProvider(lease.Options);
        var recoveredWorkflow = await restarted.GetRequiredService<IWorkflowInstanceStore>().GetAsync(workflow.Key);
        var recoveredTask = await restarted.GetRequiredService<IHumanTaskInstanceStore>().GetAsync(task.Key);
        var recoveredReceipt = await restarted.GetRequiredService<IWorkflowSuspensionReceiptStore>()
            .GetAsync(new RuntimeTenantScope("tenant-a"), receipt.SuspensionOperationId);
        recoveredWorkflow.Should().NotBeNull();
        recoveredWorkflow!.Status.Should().Be(WorkflowInstanceStatus.Suspended);
        recoveredWorkflow.WaitingHumanTaskKey.Should().Be(task.Key);
        recoveredWorkflow.Revision.Should().Be(2);
        recoveredTask.Should().NotBeNull();
        recoveredTask!.Input.Should().Be(task.Input);
        recoveredTask.Revision.Should().Be(1);
        recoveredReceipt.Should().BeEquivalentTo(receipt);
    }

    [Fact]
    public async Task TenantScopedKeysAndReceiptIntegrity_ShouldRemainIsolatedAndClassifyConflicts()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        using var provider = BuildProvider(lease.Options);
        var workflows = provider.GetRequiredService<IWorkflowInstanceStore>();
        var receipts = provider.GetRequiredService<IWorkflowSuspensionReceiptStore>();
        var host = NewWorkflow(null, "same");
        var tenant = NewWorkflow("tenant-a", "same");
        await workflows.AddAsync(host);
        await workflows.AddAsync(tenant);

        (await workflows.GetAsync(host.Key)).Should().NotBeNull();
        (await workflows.GetAsync(tenant.Key)).Should().NotBeNull();

        var coordinator = provider.GetRequiredService<IRuntimeTransactionCoordinator>();
        var tasks = provider.GetRequiredService<IHumanTaskInstanceStore>();
        var tenantAWorkflow = NewWorkflow("tenant-a", "waiter");
        var tenantBWorkflow = NewWorkflow("tenant-b", "waiter");
        await workflows.AddAsync(tenantAWorkflow);
        await workflows.AddAsync(tenantBWorkflow);
        var tenantAStored = (await workflows.GetAsync(tenantAWorkflow.Key))!;
        var tenantBStored = (await workflows.GetAsync(tenantBWorkflow.Key))!;
        var tenantATask = NewTask("tenant-a", "shared-task", tenantAWorkflow.Key);
        var tenantBTask = NewTask("tenant-b", "shared-task", tenantBWorkflow.Key);
        var tenantASuspended = tenantAStored.Snapshot();
        tenantASuspended.Status = WorkflowInstanceStatus.Suspended;
        tenantASuspended.WaitingHumanTaskKey = tenantATask.Key;
        var tenantBSuspended = tenantBStored.Snapshot();
        tenantBSuspended.Status = WorkflowInstanceStatus.Suspended;
        tenantBSuspended.WaitingHumanTaskKey = tenantBTask.Key;
        await coordinator.ExecuteAsync(async cancellationToken =>
        {
            await tasks.AddAsync(tenantATask, cancellationToken);
            await workflows.UpdateAsync(tenantASuspended, tenantAStored.Revision, cancellationToken);
            await tasks.AddAsync(tenantBTask, cancellationToken);
            await workflows.UpdateAsync(tenantBSuspended, tenantBStored.Revision, cancellationToken);
        });
        (await workflows.GetByWaitingHumanTaskAsync(tenantATask.Key))!.Key.Should().Be(tenantAWorkflow.Key);
        (await workflows.GetByWaitingHumanTaskAsync(tenantBTask.Key))!.Key.Should().Be(tenantBWorkflow.Key);

        var task = NewTask("tenant-a", "task-1", tenant.Key);
        var receipt = NewReceipt(tenant, tenant, task, "op-1");
        (await receipts.AddAsync(receipt)).Status.Should().Be(WorkflowSuspensionReceiptWriteStatus.Accepted);
        (await receipts.AddAsync(receipt)).Status.Should().Be(WorkflowSuspensionReceiptWriteStatus.Duplicate);
        var conflicting = receipt with { Integrity = Hash("different", "Integrity") };
        (await receipts.AddAsync(conflicting)).Status.Should().Be(WorkflowSuspensionReceiptWriteStatus.Conflict);
    }

    [Fact]
    public async Task SnapshotAndAuditSink_ShouldPersistDuplicateAndConflictSemanticsAcrossRestart()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        var snapshot = new DescriptorSnapshot
        {
            SnapshotId = "snapshot-1",
            PackageId = "package",
            PackageVersion = "1.0.0",
            CreatedAt = DateTimeOffset.UnixEpoch,
            Descriptors =
            [
                new SnapshotEntry
                {
                    Ref = new DescriptorRef("workflow", "approval", 1),
                    DescriptorName = "Approval",
                    Kind = DescriptorKind.Workflow,
                    State = DescriptorState.Active,
                    ContractHash = "contract",
                    DefinitionHash = "definition"
                }
            ]
        };
        var envelope = NewEnvelope("audit-1", Hash("audit-a", "AuditIntegrity"));

        using (var provider = BuildProvider(lease.Options))
        {
            var snapshots = provider.GetRequiredService<IDescriptorSnapshotStore>();
            var sink = provider.GetRequiredService<IAuditSink>();
            (await snapshots.WriteAsync(snapshot)).Status.Should().Be(DescriptorSnapshotWriteStatus.Accepted);
            (await snapshots.WriteAsync(snapshot)).Status.Should().Be(DescriptorSnapshotWriteStatus.Duplicate);
            (await snapshots.WriteAsync(new DescriptorSnapshot
            {
                SnapshotId = snapshot.SnapshotId,
                PackageId = snapshot.PackageId,
                PackageVersion = "2.0.0",
                CreatedAt = snapshot.CreatedAt,
                Descriptors = snapshot.Descriptors,
                Relationships = snapshot.Relationships
            })).Status
                .Should().Be(DescriptorSnapshotWriteStatus.Conflict);
            (await sink.WriteAsync(envelope)).Status.Should().Be(AuditSinkWriteStatus.Accepted);
        }

        using var restarted = BuildProvider(lease.Options);
        var restartedSnapshots = restarted.GetRequiredService<IDescriptorSnapshotStore>();
        var restartedSink = restarted.GetRequiredService<IAuditSink>();
        (await restartedSnapshots.GetEntryAsync("snapshot-1", new DescriptorRef("workflow", "approval", 1)))
            .Should().NotBeNull();
        (await restartedSink.WriteAsync(envelope)).Status.Should().Be(AuditSinkWriteStatus.Duplicate);
        (await restartedSink.WriteAsync(NewEnvelope("audit-1", Hash("audit-b", "AuditIntegrity")))).Status
            .Should().Be(AuditSinkWriteStatus.Conflict);
    }

    private static ServiceProvider BuildProvider(PostgreSqlRuntimePersistenceOptions options)
        => new ServiceCollection().AddCrestCreatesPostgreSqlRuntimePersistence(options).BuildServiceProvider();

    private static WorkflowInstance NewWorkflow(string? tenantId, string id) => new()
    {
        Key = new RuntimeInstanceKey(tenantId, id),
        WorkflowPin = Pin("workflow", "approval", "Workflow"),
        Status = WorkflowInstanceStatus.Running,
        StartedAt = DateTimeOffset.UnixEpoch,
        Variables = new Dictionary<string, RuntimeStateValue>(StringComparer.Ordinal)
        {
            ["amount"] = new RuntimeStateValue { TypeId = "test.decimal", JsonPayload = "12.50" }
        },
        StepVariables = new Dictionary<string, RuntimeStateValue>(StringComparer.Ordinal)
        {
            ["review"] = new RuntimeStateValue { TypeId = "test.string", JsonPayload = "\"pending\"" }
        },
        StepResults =
        [
            new WorkflowStepResult
            {
                StepId = "begin",
                StepName = "Begin",
                Status = StepExecutionStatus.Completed,
                Output = new RuntimeStateValue { TypeId = "test.string", JsonPayload = "\"ok\"" },
                ExecutedAt = DateTimeOffset.UnixEpoch,
                Duration = TimeSpan.FromSeconds(1)
            }
        ]
    };

    private static HumanTaskInstance NewTask(string? tenantId, string id, RuntimeInstanceKey workflowKey) => new()
    {
        Key = new RuntimeInstanceKey(tenantId, id),
        HumanTaskPin = Pin("humantask", "review", "HumanTask"),
        Status = HumanTaskInstanceStatus.Assigned,
        WorkflowKey = workflowKey,
        WorkflowStepId = "review",
        AssigneeUserId = "reviewer",
        CandidateUserIds = ["reviewer", "backup"],
        CandidateRoleIds = ["approver"],
        Input = new RuntimeStateValue { TypeId = "test.string", JsonPayload = "\"input\"" },
        CreatedAt = DateTimeOffset.UnixEpoch
    };

    private static WorkflowSuspensionReceipt NewReceipt(
        WorkflowInstance before,
        WorkflowInstance suspended,
        HumanTaskInstance task,
        string operationId) => new()
    {
        Scope = new RuntimeTenantScope(before.TenantId),
        SuspensionOperationId = operationId,
        Integrity = Hash(operationId, "Integrity"),
        WorkflowKey = before.Key,
        HumanTaskKey = task.Key,
        WorkflowFromRevision = before.Revision,
        WorkflowToRevision = before.Revision + 1,
        WorkflowPin = suspended.WorkflowPin,
        HumanTaskPin = task.HumanTaskPin,
        AcceptedAt = DateTimeOffset.UnixEpoch
    };

    private static RuntimeDescriptorPin Pin(string @namespace, string id, string kind) => new()
    {
        Ref = new DescriptorRef(@namespace, id, 1),
        ContractHash = Hash($"{id}-contract", "Contract", kind),
        DefinitionHash = Hash($"{id}-definition", "Definition", kind)
    };

    private static CanonicalHash Hash(string value, string purpose, string? descriptorKind = null) => new()
    {
        Value = value,
        Algorithm = "SHA-256",
        AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = descriptorKind is null ? "Runtime" : "Descriptor",
        DescriptorKind = descriptorKind,
        Scope = "InternalFull",
        Purpose = purpose,
        ContractVersion = "canonical-hash-v1",
        CanonicalShapeVersion = "canonical-shape-v1"
    };

    private static AuditEnvelope NewEnvelope(string auditId, CanonicalHash integrity) => new()
    {
        AuditId = auditId,
        OccurredAt = DateTimeOffset.UnixEpoch,
        CorrelationId = "correlation-1",
        Actor = new AuditActor { Kind = "system", Id = "system" },
        Action = new AuditAction { Kind = "workflow", Name = "suspend" },
        Target = new AuditTarget { Kind = "workflow", Id = "workflow-1" },
        Outcome = new AuditOutcome { Status = "succeeded" },
        Integrity = integrity
    };
}
