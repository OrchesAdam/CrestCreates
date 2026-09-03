using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Accountability.Abstractions.Preparation;
using CrestCreates.Accountability.Bootstrap;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Accountability.Testing.Sinks;
using CrestCreates.Accountability.Abstractions.Identity;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.HumanTask;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Persistence;
using CrestCreates.Runtime.Delivery;
using CrestCreates.Runtime.Delivery.Abstractions.Messages;
using CrestCreates.Runtime.Delivery.Abstractions.Stores;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.Persistence;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;
using CrestCreates.Workflow;
using CrestCreates.Workflow.Abstractions;
using CrestCreates.Runtime.Persistence.Testing.Cases;
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
        (await command.ExecuteScalarAsync()).Should().Be(6L);
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
    public async Task ValidationOnly_WithMissingReciprocalForeignKey_ShouldFailClosed()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await ExecuteSchemaDdlAsync(lease.Options, "alter table runtime_workflow_instances drop constraint fk_workflow_waiting_task_reciprocal;");
        var act = () => new PostgreSqlRuntimeMigrationRunner(lease.Options).ApplyAsync(new PostgreSqlRuntimeMigrationOptions { ApplyMigrations = false });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ValidationOnly_WithChangedColumnType_ShouldFailClosed()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await ExecuteSchemaDdlAsync(lease.Options, "alter table runtime_workflow_instances alter column status type bigint;");
        var act = () => new PostgreSqlRuntimeMigrationRunner(lease.Options).ApplyAsync(new PostgreSqlRuntimeMigrationOptions { ApplyMigrations = false });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ValidationOnly_WithMissingActiveStepIndex_ShouldFailClosed()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await ExecuteSchemaDdlAsync(lease.Options, "drop index ux_runtime_human_task_active_step;");
        var act = () => new PostgreSqlRuntimeMigrationRunner(lease.Options).ApplyAsync(new PostgreSqlRuntimeMigrationOptions { ApplyMigrations = false });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ValidationOnly_WithChangedIndexPredicate_ShouldFailClosed()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await ExecuteSchemaDdlAsync(lease.Options, "drop index ux_runtime_human_task_active_step; create unique index ux_runtime_human_task_active_step on runtime_human_task_instances (tenant_scope_kind, tenant_id, workflow_instance_id, workflow_step_id) where workflow_instance_id is not null;");
        var act = () => new PostgreSqlRuntimeMigrationRunner(lease.Options).ApplyAsync(new PostgreSqlRuntimeMigrationOptions { ApplyMigrations = false });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ValidationOnly_WithMissingTenantScopeCheck_ShouldFailClosed()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await ExecuteSchemaDdlAsync(lease.Options, "alter table runtime_workflow_instances drop constraint ck_runtime_workflow_tenant_scope;");
        var act = () => new PostgreSqlRuntimeMigrationRunner(lease.Options).ApplyAsync(new PostgreSqlRuntimeMigrationOptions { ApplyMigrations = false });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ValidationOnly_WithLifecycleCheckReplacedByStatusOnlyCheck_ShouldFailClosed()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await ExecuteSchemaDdlAsync(lease.Options, """
            alter table runtime_human_task_instances drop constraint ck_runtime_human_task_lifecycle;
            alter table runtime_human_task_instances add constraint ck_runtime_human_task_lifecycle check (status >= 0);
            """);

        var act = () => new PostgreSqlRuntimeMigrationRunner(lease.Options).ApplyAsync(new PostgreSqlRuntimeMigrationOptions { ApplyMigrations = false });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ValidationOnly_WithTenantCheckSameTokensButChangedBooleanLogic_ShouldFailClosed()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await ExecuteSchemaDdlAsync(lease.Options, """
            alter table runtime_workflow_instances drop constraint ck_runtime_workflow_tenant_scope;
            alter table runtime_workflow_instances add constraint ck_runtime_workflow_tenant_scope
                check (
                    tenant_scope_kind = 'host'
                    and (
                        tenant_id = ''
                        or tenant_scope_kind = 'tenant'
                    )
                    and tenant_id <> ''
                );
            """);

        var act = () => new PostgreSqlRuntimeMigrationRunner(lease.Options).ApplyAsync(new PostgreSqlRuntimeMigrationOptions { ApplyMigrations = false });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ValidationOnly_WithExtraColumnInRequiredIndex_ShouldFailClosed()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await ExecuteSchemaDdlAsync(lease.Options, """
            drop index ux_runtime_human_task_active_step;
            create unique index ux_runtime_human_task_active_step
                on runtime_human_task_instances (tenant_scope_kind, tenant_id, workflow_instance_id, workflow_step_id, instance_id)
                where workflow_instance_id is not null
                  and workflow_step_id is not null
                  and completed_at is null
                  and cancelled_at is null;
            """);

        var act = () => new PostgreSqlRuntimeMigrationRunner(lease.Options).ApplyAsync(new PostgreSqlRuntimeMigrationOptions { ApplyMigrations = false });
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
    public async Task WorkflowAbort_ShouldAtomicallyCancelTaskFailWorkflowAndPublishCanonicalFailure()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        using var provider = BuildProvider(lease.Options);
        var workflows = provider.GetRequiredService<IWorkflowInstanceStore>();
        var tasks = provider.GetRequiredService<IHumanTaskInstanceStore>();
        var transaction = provider.GetRequiredService<IRuntimeTransactionCoordinator>();
        var workflow = NewWorkflow("tenant-a", "abort-workflow");
        var task = NewTask("tenant-a", "abort-task", workflow.Key);
        await workflows.AddAsync(workflow);
        var persisted = (await workflows.GetAsync(workflow.Key))!;
        var suspended = persisted.Snapshot();
        suspended.Status = WorkflowInstanceStatus.Suspended;
        suspended.WaitingHumanTaskKey = task.Key;
        await transaction.ExecuteAsync(async ct =>
        {
            await tasks.AddAsync(task, ct);
            await workflows.UpdateAsync(suspended, persisted.Revision, ct);
        });

        var publisher = new RecordingWorkflowLifecyclePublisher();
        var abort = new WorkflowAbortService(
            workflows,
            tasks,
            new StoreBackedHumanTaskRuntime(tasks),
            new DefaultWorkflowStateMachine(),
            new FixedWorkflowPinResolver(new WorkflowDescriptor { Id = "approval", Name = "Approval", Version = 1, State = DescriptorState.Active }),
            provider.GetRequiredService<IDescriptorSnapshotStore>(),
            transaction,
            provider.GetRequiredService<IWorkflowAbortReceiptStore>(),
            new WorkflowLifecycleEventFactory(new TestIdentity(), new TestHashBuilder()),
            publisher,
            new CrestCreates.Accountability.Context.AuditOperationContextAccessor(),
            new CrestCreates.Workflow.Accountability.WorkflowAccountabilityOutboxAppender(null, null, null));

        await abort.AbortAsync(workflow.Key, task.Key, "business store unavailable", "abort-success-1");

        var recoveredWorkflow = (await workflows.GetAsync(workflow.Key))!;
        var recoveredTask = (await tasks.GetAsync(task.Key))!;
        recoveredWorkflow.Status.Should().Be(WorkflowInstanceStatus.Failed);
        recoveredWorkflow.WaitingHumanTaskKey.Should().BeNull();
        recoveredWorkflow.ErrorMessage.Should().Be("business store unavailable");
        recoveredTask.Status.Should().Be(HumanTaskInstanceStatus.Cancelled);
        publisher.Events.Should().ContainSingle(eventItem =>
            eventItem.EventType == "workflow.failed"
            && eventItem.FromStatus == WorkflowInstanceStatus.Suspended
            && eventItem.ToStatus == WorkflowInstanceStatus.Failed
            && eventItem.ReasonCode == WorkflowLifecycleReasonCodes.Aborted);
    }

    [Fact]
    public async Task WorkflowAbort_WhenWorkflowWriteFails_RollsBackTaskCancellationOnPostgreSql()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        using var provider = BuildProvider(lease.Options);
        var workflows = provider.GetRequiredService<IWorkflowInstanceStore>();
        var tasks = provider.GetRequiredService<IHumanTaskInstanceStore>();
        var transaction = provider.GetRequiredService<IRuntimeTransactionCoordinator>();
        var workflow = NewWorkflow("tenant-a", "abort-rollback-workflow", includeAuditOrigin: true);
        var task = NewTask("tenant-a", "abort-rollback-task", workflow.Key);
        await workflows.AddAsync(workflow);
        var persisted = (await workflows.GetAsync(workflow.Key))!;
        var suspended = persisted.Snapshot();
        suspended.Status = WorkflowInstanceStatus.Suspended;
        suspended.WaitingHumanTaskKey = task.Key;
        await transaction.ExecuteAsync(async ct =>
        {
            await tasks.AddAsync(task, ct);
            await workflows.UpdateAsync(suspended, persisted.Revision, ct);
        });

        var abort = new WorkflowAbortService(
            new FailingWorkflowUpdateStore(workflows),
            tasks,
            new StoreBackedHumanTaskRuntime(tasks),
            new DefaultWorkflowStateMachine(),
            new FixedWorkflowPinResolver(new WorkflowDescriptor { Id = "approval", Name = "Approval", Version = 1, State = DescriptorState.Active }),
            provider.GetRequiredService<IDescriptorSnapshotStore>(),
            transaction,
            provider.GetRequiredService<IWorkflowAbortReceiptStore>(),
            new WorkflowLifecycleEventFactory(new TestIdentity(), new TestHashBuilder()),
            new RecordingWorkflowLifecyclePublisher(),
            new CrestCreates.Accountability.Context.AuditOperationContextAccessor(),
            new CrestCreates.Workflow.Accountability.WorkflowAccountabilityOutboxAppender(
                provider.GetRequiredService<IAuditEnvelopePreparer>(),
                provider.GetRequiredService<ITransactionalOutboxWriter>(),
                provider.GetRequiredService<IOutboxMessageFactory>()));

        var action = () => abort.AbortAsync(workflow.Key, task.Key, "business store unavailable", "abort-rollback-1");
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*forced workflow write failure*");
        (await workflows.GetAsync(workflow.Key))!.Status.Should().Be(WorkflowInstanceStatus.Suspended);
        (await tasks.GetAsync(task.Key))!.Status.Should().Be(HumanTaskInstanceStatus.Assigned);
        (await provider.GetRequiredService<IOutboxDispatchStore>().ClaimAsync(new OutboxClaimRequest
        {
            OwnerId = "abort-rollback-test",
            BatchSize = 10,
            SupportedContractIds = new HashSet<string>(["crest.accountability.audit-envelope/v1"], StringComparer.Ordinal),
            SupportedRequiredConsumerIds = new HashSet<string>(StringComparer.Ordinal)
        })).Should().BeEmpty();
    }

    [Fact]
    public async Task WorkflowAbort_ReplaysSameOperationAfterCommitAcknowledgementLossWithoutDuplicateLifecycleOrAccountability()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        using var provider = BuildProvider(lease.Options);
        var workflows = provider.GetRequiredService<IWorkflowInstanceStore>();
        var tasks = provider.GetRequiredService<IHumanTaskInstanceStore>();
        var transaction = provider.GetRequiredService<IRuntimeTransactionCoordinator>();
        var workflow = NewWorkflow("tenant-a", "abort-replay-workflow");
        var task = NewTask("tenant-a", "abort-replay-task", workflow.Key);
        await workflows.AddAsync(workflow);
        var persisted = (await workflows.GetAsync(workflow.Key))!;
        var suspended = persisted.Snapshot();
        suspended.Status = WorkflowInstanceStatus.Suspended;
        suspended.WaitingHumanTaskKey = task.Key;
        await transaction.ExecuteAsync(async ct =>
        {
            await tasks.AddAsync(task, ct);
            await workflows.UpdateAsync(suspended, persisted.Revision, ct);
        });

        var publisher = new RecordingWorkflowLifecyclePublisher();
        var abort = CreateAbort(provider, workflows, tasks, transaction, publisher);
        using (PostgreSqlRuntimeTestHooks.BlockAfterCommit(() => ValueTask.FromException(new InvalidOperationException("lost commit acknowledgement"))))
        {
            var uncertain = () => abort.AbortAsync(workflow.Key, task.Key, "business store unavailable", "abort-replay-1");
            await uncertain.Should().ThrowAsync<RuntimeTransactionCommitUnknownException>();
        }

        var replay = await abort.AbortAsync(workflow.Key, task.Key, "business store unavailable", "abort-replay-1");
        replay.Status.Should().Be(WorkflowAbortResultStatus.Duplicate);
        publisher.Events.Should().BeEmpty();
        (await workflows.GetAsync(workflow.Key))!.Status.Should().Be(WorkflowInstanceStatus.Failed);
        (await tasks.GetAsync(task.Key))!.Status.Should().Be(HumanTaskInstanceStatus.Cancelled);

        var conflict = () => abort.AbortAsync(workflow.Key, task.Key, "different abort", "abort-replay-1");
        await conflict.Should().ThrowAsync<RuntimePersistenceContractException>();
    }

    [Fact]
    public async Task WorkflowAbort_UsesComposedDefaultHumanTaskRuntimeInsideThePostgreSqlTransaction()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await using var provider = BuildComposedProvider(lease.Options);
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var workflows = services.GetRequiredService<IWorkflowInstanceStore>();
        var tasks = services.GetRequiredService<IHumanTaskInstanceStore>();
        var transaction = services.GetRequiredService<IRuntimeTransactionCoordinator>();
        var workflow = NewWorkflow("tenant-a", "abort-composed-workflow", includeAuditOrigin: true);
        var task = NewTask("tenant-a", "abort-composed-task", workflow.Key);
        await SeedSuspendedAsync(workflows, tasks, transaction, workflow, task);

        var result = await services.GetRequiredService<IWorkflowAbortService>()
            .AbortAsync(workflow.Key, task.Key, "business store unavailable", "abort-composed-1");

        result.Status.Should().Be(WorkflowAbortResultStatus.Accepted);
        (await workflows.GetAsync(workflow.Key))!.Status.Should().Be(WorkflowInstanceStatus.Failed);
        (await tasks.GetAsync(task.Key))!.Status.Should().Be(HumanTaskInstanceStatus.Cancelled);
        (await services.GetRequiredService<IWorkflowAbortReceiptStore>()
            .GetAsync(new RuntimeTenantScope("tenant-a"), "abort-composed-1")).Should().NotBeNull();
    }

    [Fact]
    public async Task WorkflowAbort_RejectsTamperedPersistedReceiptBeforeDuplicateReplay()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        using var provider = BuildProvider(lease.Options);
        var workflows = provider.GetRequiredService<IWorkflowInstanceStore>();
        var tasks = provider.GetRequiredService<IHumanTaskInstanceStore>();
        var transaction = provider.GetRequiredService<IRuntimeTransactionCoordinator>();
        var workflow = NewWorkflow("tenant-a", "abort-tampered-receipt-workflow");
        var task = NewTask("tenant-a", "abort-tampered-receipt-task", workflow.Key);
        await SeedSuspendedAsync(workflows, tasks, transaction, workflow, task);
        var abort = CreateAbort(provider, workflows, tasks, transaction, new RecordingWorkflowLifecyclePublisher());
        await abort.AbortAsync(workflow.Key, task.Key, "business store unavailable", "abort-tampered-1");

        await using (var connection = new NpgsqlConnection(lease.Options.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"""
                update "{lease.Options.Schema}".runtime_operation_receipts
                set receipt_json = jsonb_set(receipt_json, ARRAY['workflowPin', 'contractHash', 'value'], to_jsonb('tampered-contract'::text))
                where operation_id = 'workflow-abort:abort-tampered-1';
                """, connection);
            await command.ExecuteNonQueryAsync();
        }

        var receiptStore = provider.GetRequiredService<IWorkflowAbortReceiptStore>();
        var read = () => receiptStore.GetAsync(new RuntimeTenantScope("tenant-a"), "abort-tampered-1");
        var readFailure = await read.Should().ThrowAsync<RuntimePersistenceContractException>();
        readFailure.Which.Code.Should().Be(RuntimePersistenceContractErrorCode.PersistedInvariantViolation);

        var action = () => abort.AbortAsync(workflow.Key, task.Key, "business store unavailable", "abort-tampered-1");
        var failure = await action.Should().ThrowAsync<RuntimePersistenceContractException>();
        failure.Which.Code.Should().Be(RuntimePersistenceContractErrorCode.PersistedInvariantViolation);
    }

    [Fact]
    public async Task WorkflowAbort_ComposedAccountabilityIsCommittedAndRolledBackWithRuntimeState()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        using var provider = BuildProvider(lease.Options);
        var workflows = provider.GetRequiredService<IWorkflowInstanceStore>();
        var tasks = provider.GetRequiredService<IHumanTaskInstanceStore>();
        var transaction = provider.GetRequiredService<IRuntimeTransactionCoordinator>();
        var workflow = NewWorkflow("tenant-a", "abort-accountability-workflow", includeAuditOrigin: true);
        var task = NewTask("tenant-a", "abort-accountability-task", workflow.Key);
        await SeedSuspendedAsync(workflows, tasks, transaction, workflow, task);
        var publisher = new RecordingWorkflowLifecyclePublisher();
        var abort = CreateAbort(provider, workflows, tasks, transaction, publisher, useRealAccountability: true);

        (await abort.AbortAsync(workflow.Key, task.Key, "business store unavailable", "abort-accountability-1"))
            .Status.Should().Be(WorkflowAbortResultStatus.Accepted);
        var claims = await provider.GetRequiredService<IOutboxDispatchStore>().ClaimAsync(new OutboxClaimRequest
        {
            OwnerId = "abort-accountability-test",
            BatchSize = 10,
            SupportedContractIds = new HashSet<string>(["crest.accountability.audit-envelope/v1"], StringComparer.Ordinal),
            SupportedRequiredConsumerIds = new HashSet<string>(StringComparer.Ordinal)
        });
        claims.Should().ContainSingle(item => item.Message.Metadata.EventName == "workflow.failed");
        publisher.Events.Should().ContainSingle();
    }

    private static async Task SeedSuspendedAsync(
        IWorkflowInstanceStore workflows,
        IHumanTaskInstanceStore tasks,
        IRuntimeTransactionCoordinator transaction,
        WorkflowInstance workflow,
        HumanTaskInstance task)
    {
        await workflows.AddAsync(workflow);
        var persisted = (await workflows.GetAsync(workflow.Key))!;
        var suspended = persisted.Snapshot();
        suspended.Status = WorkflowInstanceStatus.Suspended;
        suspended.WaitingHumanTaskKey = task.Key;
        await transaction.ExecuteAsync(async ct =>
        {
            await tasks.AddAsync(task, ct);
            await workflows.UpdateAsync(suspended, persisted.Revision, ct);
        });
    }

    private static WorkflowAbortService CreateAbort(
        ServiceProvider provider,
        IWorkflowInstanceStore workflows,
        IHumanTaskInstanceStore tasks,
        IRuntimeTransactionCoordinator transaction,
        RecordingWorkflowLifecyclePublisher publisher,
        bool useRealAccountability = false)
        => new(
            workflows,
            tasks,
            new StoreBackedHumanTaskRuntime(tasks),
            new DefaultWorkflowStateMachine(),
            new FixedWorkflowPinResolver(new WorkflowDescriptor { Id = "approval", Name = "Approval", Version = 1, State = DescriptorState.Active }),
            provider.GetRequiredService<IDescriptorSnapshotStore>(),
            transaction,
            provider.GetRequiredService<IWorkflowAbortReceiptStore>(),
            new WorkflowLifecycleEventFactory(new TestIdentity(), new TestHashBuilder()),
            publisher,
            new CrestCreates.Accountability.Context.AuditOperationContextAccessor(),
            useRealAccountability
                ? new CrestCreates.Workflow.Accountability.WorkflowAccountabilityOutboxAppender(
                    provider.GetRequiredService<IAuditEnvelopePreparer>(),
                    provider.GetRequiredService<ITransactionalOutboxWriter>(),
                    provider.GetRequiredService<IOutboxMessageFactory>())
                : new CrestCreates.Workflow.Accountability.WorkflowAccountabilityOutboxAppender(null, null, null));

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
        var coordinator2 = provider.GetRequiredService<IRuntimeTransactionCoordinator>();
        await coordinator2.ExecuteAsync(async cancellationToken =>
        {
            await tasks.AddAsync(task, cancellationToken);
        });
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

    [Fact]
    public async Task SharedRuntimeContractKit_ShouldPass()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        using var driver = new PostgreSqlRuntimePersistenceContractDriver(lease.Options, $"shared-{Guid.NewGuid():N}");
        await RuntimePersistenceContractCases.DescriptorSnapshot_IdentityAndOrderingAsync(driver);
        await RuntimePersistenceContractCases.HumanTask_QueryOrderAsync(driver);
        await RuntimePersistenceContractCases.Workflow_RevisionAndTransactionAsync(driver);
    }

    [Fact]
    public async Task SharedAuditSinkContractKit_ShouldPass()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        using var provider = BuildProvider(lease.Options);
        var driver = new PostgreSqlAuditSinkContractDriver(
            provider.GetRequiredService<NpgsqlDataSource>(), lease.Options);
        await AuditSinkContractCases.AcceptsNewRecordAsync(driver);
        await AuditSinkContractCases.AcceptedThenDuplicateAsync(driver);
        await AuditSinkContractCases.DifferentIntegrityIsConflictAsync(driver);
        await AuditSinkContractCases.SnapshotsOnWriteAndReadAsync(driver);
        await AuditSinkContractCases.ConcurrentIdenticalWriteAsync(driver);
        await AuditSinkContractCases.DeterministicReadOrderAsync(driver);
    }

    [Fact]
    public async Task ConcurrentUseOfAmbientSession_ShouldFailClosed()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        using var provider = BuildProvider(lease.Options);
        var coordinator = provider.GetRequiredService<IRuntimeTransactionCoordinator>();
        var tasks = provider.GetRequiredService<IHumanTaskInstanceStore>();
        var workflow = NewWorkflow("tenant-a", "workflow-1");
        var task1 = NewTask("tenant-a", "task-1", workflow.Key, "review-1");
        var task2 = NewTask("tenant-a", "task-2", workflow.Key, "review-2");
        await provider.GetRequiredService<IWorkflowInstanceStore>().AddAsync(workflow);

        var act = async () =>
        {
            await coordinator.ExecuteAsync(async ct =>
            {
                using var firstLeaseEntered = new ManualResetEventSlim();
                using var releaseFirstLease = new ManualResetEventSlim();
                using var probe = PostgreSqlRuntimeTestHooks.BlockFirstCommand(() =>
                {
                    firstLeaseEntered.Set();
                    releaseFirstLease.Wait();
                });
                var first = Task.Run(() => tasks.AddAsync(task1, ct), ct);
                firstLeaseEntered.Wait(ct);
                try
                {
                    await tasks.AddAsync(task2, ct);
                }
                catch
                {
                    releaseFirstLease.Set();
                    await first;
                    throw;
                }
                finally
                {
                    releaseFirstLease.Set();
                }
                await first;
            });
        };

        await act.Should().ThrowAsync<RuntimePersistenceContractException>()
            .Where(ex => ex.Code == RuntimePersistenceContractErrorCode.ConcurrentAmbientUse);
        (await tasks.GetAsync(task1.Key)).Should().BeNull();
        (await tasks.GetAsync(task2.Key)).Should().BeNull();
    }

    [Fact]
    public async Task DirectStoreWrite_WithNonReciprocalTask_ShouldFail()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        using var provider = BuildProvider(lease.Options);
        var workflows = provider.GetRequiredService<IWorkflowInstanceStore>();
        var tasks = provider.GetRequiredService<IHumanTaskInstanceStore>();
        var coordinator = provider.GetRequiredService<IRuntimeTransactionCoordinator>();
        var workflowA = NewWorkflow("tenant-a", "workflow-a");
        var workflowB = NewWorkflow("tenant-a", "workflow-b");
        await workflows.AddAsync(workflowA);
        await workflows.AddAsync(workflowB);
        var task = NewTask("tenant-a", "task-a", workflowB.Key);
        await tasks.AddAsync(task);
        var waiting = (await workflows.GetAsync(workflowA.Key))!;
        waiting.Status = WorkflowInstanceStatus.Suspended;
        waiting.WaitingHumanTaskKey = task.Key;

        var act = () => coordinator.ExecuteAsync(ct => new ValueTask(workflows.UpdateAsync(waiting, waiting.Revision, ct))).AsTask();
        await act.Should().ThrowAsync<RuntimePersistenceContractException>()
            .Where(ex => ex.Code == RuntimePersistenceContractErrorCode.WaitingTaskCorrelationConflict);
    }

    [Fact]
    public async Task Receipt_WithTaskBelongingToAnotherWorkflow_ShouldFail()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        using var provider = BuildProvider(lease.Options);
        var workflows = provider.GetRequiredService<IWorkflowInstanceStore>();
        var tasks = provider.GetRequiredService<IHumanTaskInstanceStore>();
        var receipts = provider.GetRequiredService<IWorkflowSuspensionReceiptStore>();
        var workflowA = NewWorkflow("tenant-a", "workflow-a");
        var workflowB = NewWorkflow("tenant-a", "workflow-b");
        await workflows.AddAsync(workflowA);
        await workflows.AddAsync(workflowB);
        var task = NewTask("tenant-a", "task-a", workflowB.Key);
        await tasks.AddAsync(task);
        var act = () => receipts.AddAsync(NewReceipt(workflowA, workflowA, task, "wrong-workflow"));
        await act.Should().ThrowAsync<RuntimePersistenceContractException>()
            .Where(ex => ex.Code == RuntimePersistenceContractErrorCode.WaitingTaskCorrelationConflict);
    }

    [Fact]
    public Task AssignedTask_WithCompletedAt_ShouldFail()
        => AssertInvalidHumanTaskLifecycleAsync(HumanTaskInstanceStatus.Assigned, completedAt: true, cancelledAt: false);

    [Fact]
    public Task CompletedTask_WithoutCompletedAt_ShouldFail()
        => AssertInvalidHumanTaskLifecycleAsync(HumanTaskInstanceStatus.Completed, completedAt: false, cancelledAt: false);

    [Fact]
    public Task CancelledTask_WithoutCancelledAt_ShouldFail()
        => AssertInvalidHumanTaskLifecycleAsync(HumanTaskInstanceStatus.Cancelled, completedAt: false, cancelledAt: false);

    [Fact]
    public async Task DatabaseLifecycleCheck_ShouldRejectBypassedStoreWrite()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        using var provider = BuildProvider(lease.Options);
        var workflow = NewWorkflow("tenant-a", "workflow-1");
        var task = NewTask("tenant-a", "task-1", workflow.Key);
        await provider.GetRequiredService<IWorkflowInstanceStore>().AddAsync(workflow);
        await provider.GetRequiredService<IHumanTaskInstanceStore>().AddAsync(task);

        await using var connection = new NpgsqlConnection(lease.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"update \"{lease.Options.Schema}\".runtime_human_task_instances set completed_at=clock_timestamp() where tenant_scope_kind='tenant' and tenant_id='tenant-a' and instance_id='task-1';", connection);
        var act = () => command.ExecuteNonQueryAsync();
        await act.Should().ThrowAsync<PostgresException>()
            .Where(ex => ex.SqlState == PostgresErrorCodes.CheckViolation);
    }

    private async Task AssertInvalidHumanTaskLifecycleAsync(
        HumanTaskInstanceStatus status, bool completedAt, bool cancelledAt)
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        using var provider = BuildProvider(lease.Options);
        var workflow = NewWorkflow("tenant-a", "workflow-1");
        await provider.GetRequiredService<IWorkflowInstanceStore>().AddAsync(workflow);
        var task = NewTask("tenant-a", $"task-{status}", workflow.Key);
        task.Status = status;
        task.CompletedAt = completedAt ? DateTimeOffset.UtcNow : null;
        task.CancelledAt = cancelledAt ? DateTimeOffset.UtcNow : null;

        var act = () => provider.GetRequiredService<IHumanTaskInstanceStore>().AddAsync(task);
        await act.Should().ThrowAsync<RuntimePersistenceContractException>()
            .Where(ex => ex.Code == RuntimePersistenceContractErrorCode.PersistedInvariantViolation);
    }

    [Fact]
    public async Task CompletedTask_ShouldAllowNewActiveTaskForSameWorkflowStep()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        var runner = new PostgreSqlRuntimeMigrationRunner(lease.Options);
        await runner.ApplyAsync(new PostgreSqlRuntimeMigrationOptions { ApplyMigrations = true });
        using var provider = BuildProvider(lease.Options);
        var tx = provider.GetRequiredService<IRuntimeTransactionCoordinator>();
        var workflows = provider.GetRequiredService<IWorkflowInstanceStore>();
        var tasks = provider.GetRequiredService<IHumanTaskInstanceStore>();
        var workflow = NewWorkflow("tenant-a", "wf-1");
        var task1 = NewTask("tenant-a", "task-1", workflow.Key);
        task1.Status = HumanTaskInstanceStatus.Completed;
        task1.CompletedAt = DateTimeOffset.UtcNow;

        await tx.ExecuteAsync(async ct =>
        {
            await workflows.AddAsync(workflow, ct);
            await tasks.AddAsync(task1, ct);
        });

        var task2 = NewTask("tenant-a", "task-2", workflow.Key);
        var act = async () => await tx.ExecuteAsync(async ct => await tasks.AddAsync(task2, ct));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CancelledTask_ShouldAllowNewActiveTaskForSameWorkflowStep()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        var runner = new PostgreSqlRuntimeMigrationRunner(lease.Options);
        await runner.ApplyAsync(new PostgreSqlRuntimeMigrationOptions { ApplyMigrations = true });
        using var provider = BuildProvider(lease.Options);
        var tx = provider.GetRequiredService<IRuntimeTransactionCoordinator>();
        var workflows = provider.GetRequiredService<IWorkflowInstanceStore>();
        var tasks = provider.GetRequiredService<IHumanTaskInstanceStore>();
        var workflow = NewWorkflow("tenant-a", "wf-1");
        var task1 = NewTask("tenant-a", "task-1", workflow.Key);
        task1.Status = HumanTaskInstanceStatus.Cancelled;
        task1.CancelledAt = DateTimeOffset.UtcNow;

        await tx.ExecuteAsync(async ct =>
        {
            await workflows.AddAsync(workflow, ct);
            await tasks.AddAsync(task1, ct);
        });

        var task2 = NewTask("tenant-a", "task-2", workflow.Key);
        var act = async () => await tx.ExecuteAsync(async ct => await tasks.AddAsync(task2, ct));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Receipt_WithMissingWorkflowOrTask_ShouldFail()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        var runner = new PostgreSqlRuntimeMigrationRunner(lease.Options);
        await runner.ApplyAsync(new PostgreSqlRuntimeMigrationOptions { ApplyMigrations = true });
        using var provider = BuildProvider(lease.Options);
        var tx = provider.GetRequiredService<IRuntimeTransactionCoordinator>();
        var receipts = provider.GetRequiredService<IWorkflowSuspensionReceiptStore>();
        var workflow = NewWorkflow("tenant-a", "wf-1");
        var task = NewTask("tenant-a", "task-1", workflow.Key);

        var receipt = NewReceipt(workflow, workflow, task, "op-1");

        var act = async () => await tx.ExecuteAsync(async ct => await receipts.AddAsync(receipt, ct));
        var failure = await act.Should().ThrowAsync<RuntimePersistenceContractException>();
        failure.Which.Code.Should().Be(RuntimePersistenceContractErrorCode.PersistedInvariantViolation);
        failure.Which.Message.Should().Be("The persisted Runtime correlation violates a required invariant.");
        failure.Which.InnerException.Should().BeNull();
    }

    private static ServiceProvider BuildProvider(PostgreSqlRuntimePersistenceOptions options)
        => new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(options)
            .AddAccountability()
            .AddRuntimeDelivery()
            .BuildServiceProvider();

    private static ServiceProvider BuildComposedProvider(PostgreSqlRuntimePersistenceOptions options)
        => new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(options)
            .AddRuntimePersistence()
            .AddAccountability()
            .AddRuntimeDelivery()
            .AddSingleton<IDescriptorStableHashBuilder, TestHashBuilder>()
            .AddHumanTaskRuntime()
            .AddWorkflowEngine()
            .AddScoped<ILocalEventBus, NoopLocalEventBus>()
            .AddLogging()
            .AddSingleton<IRuntimeDescriptorPinResolver<WorkflowDescriptor>>(_ =>
                new FixedWorkflowPinResolver(new WorkflowDescriptor
                {
                    Id = "approval",
                    Name = "Approval",
                    Version = 1,
                    State = DescriptorState.Active
                }))
            .BuildServiceProvider();

    private static async Task ExecuteSchemaDdlAsync(PostgreSqlRuntimePersistenceOptions options, string sql)
    {
        await using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"set search_path to \"{options.Schema}\"; {sql}", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static WorkflowInstance NewWorkflow(string? tenantId, string id, bool includeAuditOrigin = false) => new()
    {
        Key = new RuntimeInstanceKey(tenantId, id),
        AuditOrigin = includeAuditOrigin
            ? new AuditOrigin
            {
                CorrelationId = $"correlation-{id}",
                InitiatingActor = new AuditActor { Kind = "test", Id = "test" },
                InvocationSource = "test"
            }
            : null,
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

    private static HumanTaskInstance NewTask(string? tenantId, string id, RuntimeInstanceKey workflowKey, string? stepId = "review") => new()
    {
        Key = new RuntimeInstanceKey(tenantId, id),
        HumanTaskPin = Pin("humantask", "review", "HumanTask"),
        Status = HumanTaskInstanceStatus.Assigned,
        WorkflowKey = workflowKey,
        WorkflowStepId = stepId,
        RequiredCompletionConsumerIds = ["crest.workflow.humantask-continuation/v1"],
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

    private sealed class RecordingWorkflowLifecyclePublisher : IWorkflowLifecycleEventPublisher
    {
        public List<WorkflowLifecycleEvent> Events { get; } = [];

        public Task PublishAsync(WorkflowLifecycleEvent lifecycleEvent, CancellationToken ct)
        {
            Events.Add(lifecycleEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class NoopLocalEventBus : ILocalEventBus
    {
        public Task PublishAsync(ILocalEvent @event, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : ILocalEvent
            => Task.CompletedTask;
    }

    private sealed class FixedWorkflowPinResolver(WorkflowDescriptor descriptor) : IRuntimeDescriptorPinResolver<WorkflowDescriptor>
    {
        public ResolvedRuntimeDescriptor<WorkflowDescriptor> Capture(WorkflowDescriptor value)
            => new() { Descriptor = value, Pin = descriptorPin };

        public ResolvedRuntimeDescriptor<WorkflowDescriptor> Resolve(RuntimeDescriptorPin pin)
            => new() { Descriptor = descriptor, Pin = pin };

        private static RuntimeDescriptorPin descriptorPin => new()
        {
            Ref = new DescriptorRef("workflow", "approval", 1),
            ContractHash = Hash("approval-contract", "Contract", "Workflow"),
            DefinitionHash = Hash("approval-definition", "Definition", "Workflow")
        };
    }

    private sealed class FailingWorkflowUpdateStore(IWorkflowInstanceStore inner) : IWorkflowInstanceStore
    {
        public Task AddAsync(WorkflowInstance instance, CancellationToken cancellationToken = default)
            => inner.AddAsync(instance, cancellationToken);

        public Task UpdateAsync(WorkflowInstance instance, long expectedRevision, CancellationToken cancellationToken = default)
            => Task.FromException(new InvalidOperationException("forced workflow write failure"));

        public Task<WorkflowInstance?> GetAsync(RuntimeInstanceKey key, CancellationToken cancellationToken = default)
            => inner.GetAsync(key, cancellationToken);

        public Task<WorkflowInstance?> GetByWaitingHumanTaskAsync(RuntimeInstanceKey humanTaskKey, CancellationToken cancellationToken = default)
            => inner.GetByWaitingHumanTaskAsync(humanTaskKey, cancellationToken);
    }

    private sealed class StoreBackedHumanTaskRuntime(IHumanTaskInstanceStore store) : IHumanTaskRuntime
    {
        public Task<HumanTaskInstance> PrepareAsync(HumanTaskCreationRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<HumanTaskInstance> CreateAsync(HumanTaskCreationRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<HumanTaskInstance> CompleteAsync(HumanTaskCompletionRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public async Task<HumanTaskInstance> CancelAsync(RuntimeInstanceKey humanTaskKey, string reason, CancellationToken ct = default)
        {
            var loaded = await store.GetAsync(humanTaskKey, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("HumanTask not found.");
            var candidate = loaded.Snapshot();
            candidate.Status = HumanTaskInstanceStatus.Cancelled;
            candidate.CancellationReason = reason;
            candidate.CancelledAt = DateTimeOffset.UtcNow;
            await store.UpdateAsync(candidate, loaded.Revision, ct).ConfigureAwait(false);
            candidate.Revision = loaded.Revision + 1;
            return candidate;
        }
    }

    private sealed class TestIdentity : IAuditIdentityGenerator
    {
        private int _counter;
        public string CreateOperationId() => $"abort-operation-{Interlocked.Increment(ref _counter)}";
        public string CreateAuditId() => $"abort-audit-{Interlocked.Increment(ref _counter)}";
    }

    private sealed class TestHashBuilder : IDescriptorStableHashBuilder
    {
        public DescriptorStableHashes Build(IDescriptor descriptor)
            => new() { ContractHash = Hash("contract", "Contract", "Workflow"), DefinitionHash = Hash("definition", "Definition", "Workflow") };
    }
}
