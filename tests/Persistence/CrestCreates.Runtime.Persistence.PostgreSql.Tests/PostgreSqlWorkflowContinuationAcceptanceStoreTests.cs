using System.Security.Cryptography;
using System.Text.Json;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

[Collection(PostgreSqlRuntimeCollection.Name)]
public sealed class PostgreSqlWorkflowContinuationAcceptanceStoreTests(PostgreSqlRuntimeCollectionFixture fixture)
{
    [Fact]
    public async Task ResumeClaim_IsAtomicAcrossProvidersAndBoundToExactRevision()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await using var first = BuildProvider(lease.Options);
        await using var second = BuildProvider(lease.Options);
        var workflowKey = new RuntimeInstanceKey("tenant-claim", $"workflow-{Guid.NewGuid():N}");
        var taskKey = new RuntimeInstanceKey("tenant-claim", $"task-{Guid.NewGuid():N}");
        var acceptance = CreateAcceptance(workflowKey, taskKey);
        var coordinator = first.GetRequiredService<IRuntimeTransactionCoordinator>();
        await coordinator.ExecuteAsync(async ct =>
        {
            await first.GetRequiredService<IWorkflowInstanceStore>().AddAsync(
                new WorkflowInstance { Key = workflowKey, WorkflowPin = Pin("workflow") }, ct);
            await first.GetRequiredService<IHumanTaskInstanceStore>().AddAsync(new HumanTaskInstance
            {
                Key = taskKey,
                WorkflowKey = workflowKey,
                WorkflowStepId = "review",
                HumanTaskPin = Pin("humantask"),
                RequiredCompletionConsumerIds = [HumanTaskDeliveryConstants.WorkflowContinuationConsumerId],
                Status = HumanTaskInstanceStatus.Assigned
            }, ct);
            (await first.GetRequiredService<IWorkflowContinuationAcceptanceStore>().AddAsync(acceptance, ct))
                .Should().Be(WorkflowContinuationAcceptanceWriteResult.Accepted);
        });
        var firstStore = first.GetRequiredService<IWorkflowContinuationAcceptanceStore>();
        var secondStore = second.GetRequiredService<IWorkflowContinuationAcceptanceStore>();
        var firstLease = new WorkflowContinuationResumeLease("worker-1", 1, DateTimeOffset.UtcNow.AddMinutes(1));
        var secondLease = new WorkflowContinuationResumeLease("worker-2", 1, DateTimeOffset.UtcNow.AddMinutes(1));

        var claims = await Task.WhenAll(
            firstStore.TryClaimResumeAsync(acceptance, firstLease),
            secondStore.TryClaimResumeAsync(acceptance, secondLease));

        claims.Count(result => result.Result == WorkflowContinuationResumeClaimResult.Acquired).Should().Be(1);
        claims.Should().ContainSingle(result => result.Result == WorkflowContinuationResumeClaimResult.AlreadyInProgress);
        var winner = claims.Single(result => result.Result == WorkflowContinuationResumeClaimResult.Acquired);
        (await firstStore.CompleteResumeAsync(acceptance, winner.Claim!)).Should().BeTrue();
        (await firstStore.TryClaimResumeAsync(acceptance, firstLease)).Result.Should().Be(WorkflowContinuationResumeClaimResult.NoResumeRequired);
    }

    [Fact]
    public async Task ResumeClaim_FirstInsertRaceElectsExactlyOneOwner()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await using var first = BuildProvider(lease.Options);
        await using var second = BuildProvider(lease.Options);
        var workflowKey = new RuntimeInstanceKey("tenant-race", $"workflow-{Guid.NewGuid():N}");
        var taskKey = new RuntimeInstanceKey("tenant-race", $"task-{Guid.NewGuid():N}");
        var acceptance = CreateAcceptance(workflowKey, taskKey);
        var coordinator = first.GetRequiredService<IRuntimeTransactionCoordinator>();
        await coordinator.ExecuteAsync(async ct =>
        {
            await first.GetRequiredService<IWorkflowInstanceStore>().AddAsync(
                new WorkflowInstance { Key = workflowKey, WorkflowPin = Pin("workflow") }, ct);
            await first.GetRequiredService<IHumanTaskInstanceStore>().AddAsync(new HumanTaskInstance
            {
                Key = taskKey,
                WorkflowKey = workflowKey,
                WorkflowStepId = "review",
                HumanTaskPin = Pin("humantask"),
                RequiredCompletionConsumerIds = [HumanTaskDeliveryConstants.WorkflowContinuationConsumerId],
                Status = HumanTaskInstanceStatus.Assigned
            }, ct);
            await first.GetRequiredService<IWorkflowContinuationAcceptanceStore>().AddAsync(acceptance, ct);
        });

        await using var blocker = new NpgsqlConnection(lease.Options.ConnectionString);
        await blocker.OpenAsync();
        await using var blockerTransaction = await blocker.BeginTransactionAsync();
        await using (var lockCommand = blocker.CreateCommand())
        {
            lockCommand.Transaction = blockerTransaction;
            lockCommand.CommandText = $"select revision from \"{lease.Options.Schema}\".runtime_workflow_instances where tenant_scope_kind='tenant' and tenant_id=@tenant and instance_id=@id for update;";
            lockCommand.Parameters.AddWithValue("tenant", workflowKey.TenantId!);
            lockCommand.Parameters.AddWithValue("id", workflowKey.InstanceId);
            await lockCommand.ExecuteNonQueryAsync();
        }

        var firstClaim = first.GetRequiredService<IWorkflowContinuationAcceptanceStore>().TryClaimResumeAsync(
            acceptance, new WorkflowContinuationResumeLease("worker-1", 1, DateTimeOffset.UtcNow.AddMinutes(1)));
        var secondClaim = second.GetRequiredService<IWorkflowContinuationAcceptanceStore>().TryClaimResumeAsync(
            acceptance, new WorkflowContinuationResumeLease("worker-2", 1, DateTimeOffset.UtcNow.AddMinutes(1)));
        await WaitForBlockedClaimAttemptsAsync(lease.Options.ConnectionString);
        await blockerTransaction.CommitAsync();
        var claims = await Task.WhenAll(firstClaim, secondClaim);

        claims.Count(result => result.Result == WorkflowContinuationResumeClaimResult.Acquired).Should().Be(1);
        claims.Should().ContainSingle(result => result.Result == WorkflowContinuationResumeClaimResult.AlreadyInProgress);
    }

    private static async Task WaitForBlockedClaimAttemptsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                select count(*)
                from pg_stat_activity
                where datname = current_database()
                  and wait_event_type = 'Lock'
                  and query like '%waiting_instance_id from%runtime_workflow_instances%for update%';
                """;
            if ((long)(await command.ExecuteScalarAsync(deadline.Token))! >= 2)
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(20), deadline.Token);
        }
    }

    [Fact]
    public async Task ResumeClaim_ReleaseAllowsRetryButLaterRevisionRejectsOldAcceptance()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await using var provider = BuildProvider(lease.Options);
        var workflowKey = new RuntimeInstanceKey("tenant-retry", $"workflow-{Guid.NewGuid():N}");
        var taskKey = new RuntimeInstanceKey("tenant-retry", $"task-{Guid.NewGuid():N}");
        var acceptance = CreateAcceptance(workflowKey, taskKey);
        var coordinator = provider.GetRequiredService<IRuntimeTransactionCoordinator>();
        await coordinator.ExecuteAsync(async ct =>
        {
            await provider.GetRequiredService<IWorkflowInstanceStore>().AddAsync(
                new WorkflowInstance { Key = workflowKey, WorkflowPin = Pin("workflow") }, ct);
            await provider.GetRequiredService<IHumanTaskInstanceStore>().AddAsync(new HumanTaskInstance
            {
                Key = taskKey,
                WorkflowKey = workflowKey,
                WorkflowStepId = "review",
                HumanTaskPin = Pin("humantask"),
                RequiredCompletionConsumerIds = [HumanTaskDeliveryConstants.WorkflowContinuationConsumerId],
                Status = HumanTaskInstanceStatus.Assigned
            }, ct);
            await provider.GetRequiredService<IWorkflowContinuationAcceptanceStore>().AddAsync(acceptance, ct);
        });
        var store = provider.GetRequiredService<IWorkflowContinuationAcceptanceStore>();
        var firstLease = new WorkflowContinuationResumeLease("worker-1", 1, DateTimeOffset.UtcNow.AddMinutes(1));
        var retryLease = new WorkflowContinuationResumeLease("worker-2", 2, DateTimeOffset.UtcNow.AddMinutes(1));
        var first = await store.TryClaimResumeAsync(acceptance, firstLease);
        first.Result.Should().Be(WorkflowContinuationResumeClaimResult.Acquired);
        (await store.ReleaseResumeAsync(acceptance, first.Claim!)).Should().BeTrue();
        var retry = await store.TryClaimResumeAsync(acceptance, retryLease);
        retry.Result.Should().Be(WorkflowContinuationResumeClaimResult.Acquired);
        retry.Claim!.Version.Should().Be(first.Claim!.Version + 1);
        (await store.ReleaseResumeAsync(acceptance, retry.Claim!)).Should().BeTrue();

        await coordinator.ExecuteAsync(async ct =>
        {
            var workflow = (await provider.GetRequiredService<IWorkflowInstanceStore>().GetAsync(workflowKey, ct))!;
            workflow.Status = WorkflowInstanceStatus.Suspended;
            workflow.WaitingHumanTaskKey = taskKey;
            await provider.GetRequiredService<IWorkflowInstanceStore>().UpdateAsync(workflow, workflow.Revision, ct);
        });

        (await store.TryClaimResumeAsync(acceptance, retryLease)).Result.Should().Be(WorkflowContinuationResumeClaimResult.NoResumeRequired);
    }

    private static ServiceProvider BuildProvider(PostgreSqlRuntimePersistenceOptions options) => new ServiceCollection()
        .AddCrestCreatesPostgreSqlRuntimePersistence(options)
        .BuildServiceProvider();

    private static RuntimeDescriptorPin Pin(string kind) => new()
    {
        Ref = new DescriptorRef(kind, $"{kind}-1", 1),
        ContractHash = Hash("contract"),
        DefinitionHash = Hash("definition")
    };

    private static CanonicalHash Hash(string purpose) => new()
    {
        Value = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(purpose))).ToLowerInvariant(),
        Algorithm = "SHA-256",
        AlgorithmVersion = "1",
        ArtifactKind = "Descriptor",
        Scope = "InternalFull",
        Purpose = purpose == "contract" ? "Contract" : "Definition",
        ContractVersion = "v1",
        CanonicalShapeVersion = "v1"
    };

    private static WorkflowContinuationAcceptance CreateAcceptance(RuntimeInstanceKey workflowKey, RuntimeInstanceKey taskKey)
    {
        var completionEventId = $"completion-{Guid.NewGuid():N}";
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            writer.WriteStringValue(workflowKey.TenantId);
            writer.WriteStringValue(completionEventId);
            writer.WriteStringValue(taskKey.TenantId);
            writer.WriteStringValue(taskKey.InstanceId);
            writer.WriteStringValue(workflowKey.TenantId);
            writer.WriteStringValue(workflowKey.InstanceId);
            writer.WriteStringValue("Approve");
            writer.WriteNullValue();
            writer.WriteNumberValue(0);
            writer.WriteNumberValue(1);
            writer.WriteEndArray();
        }
        return new WorkflowContinuationAcceptance
        {
            TenantScope = new RuntimeTenantScope(workflowKey.TenantId),
            CompletionEventId = completionEventId,
            HumanTaskKey = taskKey,
            WorkflowKey = workflowKey,
            Outcome = "Approve",
            WorkflowFromRevision = 0,
            WorkflowToRevision = 1,
            Integrity = new CanonicalHash
            {
                Value = Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant(),
                Algorithm = "SHA-256",
                AlgorithmVersion = "1",
                ArtifactKind = "RuntimeWorkflowContinuationAcceptance",
                DescriptorKind = "Runtime",
                Scope = "InternalFull",
                Purpose = "Integrity",
                ContractVersion = "canonical-hash-v1",
                CanonicalShapeVersion = "runtime-workflow-continuation-acceptance-v1"
            }
        };
    }
}
