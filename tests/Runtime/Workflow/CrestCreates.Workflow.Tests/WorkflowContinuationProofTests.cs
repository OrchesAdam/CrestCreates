using System.Security.Cryptography;
using System.Text.Json;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Workflow.Abstractions;
using CrestCreates.Workflow.Abstractions.Delivery;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;
using CrestCreates.Runtime.Delivery.Abstractions.Stores;
using CrestCreates.Runtime.Delivery.Message;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using CrestCreates.Runtime.Persistence.InMemory;
using Microsoft.Extensions.DependencyInjection;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public sealed class WorkflowContinuationProofTests
{
    [Fact]
    public async Task WorkflowContinuationConsumer_WithoutWorkflowKey_ShouldNotProveDuplicate()
    {
        var continuation = new Mock<IWorkflowContinuationService>();
        var consumer = new WorkflowContinuationOutboxConsumer(continuation.Object);
        var result = await consumer.ConsumeAsync(new HumanTaskCompletedEvent
        {
            EventId = "completion-without-workflow",
            HumanTaskKey = new RuntimeInstanceKey("tenant-1", "task-1"),
            HumanTaskPin = new RuntimeDescriptorPin
            {
                Ref = new CrestCreates.Metadata.Abstractions.DescriptorRef("humantask", "review", 1),
                ContractHash = new CanonicalHash { Value = "c", Algorithm = "a", AlgorithmVersion = "1", ArtifactKind = "r", Scope = "s", Purpose = "p", ContractVersion = "v", CanonicalShapeVersion = "v" },
                DefinitionHash = new CanonicalHash { Value = "d", Algorithm = "a", AlgorithmVersion = "1", ArtifactKind = "r", Scope = "s", Purpose = "p", ContractVersion = "v", CanonicalShapeVersion = "v" }
            },
            Outcome = "Approve"
        }, null!, CancellationToken.None);

        result.Outcome.Should().Be(OutboxDeliveryOutcome.Conflict);
        result.FailureCode.Should().Be("WORKFLOW_CONTINUATION_CORRELATION_MISSING");
        continuation.Verify(value => value.ContinueAsync(It.IsAny<WorkflowContinuationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(WorkflowInstanceStatus.Running, null, 1)]
    [InlineData(WorkflowInstanceStatus.Completed, null, 0)]
    [InlineData(WorkflowInstanceStatus.Suspended, "task-2", 0)]
    public async Task AcceptedCompletionReplay_OnlyResumesUnfinishedRunnerWork(
        WorkflowInstanceStatus status,
        string? waitingTaskId,
        int expectedRuns)
    {
        var workflowKey = new RuntimeInstanceKey("tenant-1", "workflow-1");
        var humanTaskKey = new RuntimeInstanceKey("tenant-1", "task-1");
        var request = new WorkflowContinuationRequest
        {
            WorkflowKey = workflowKey,
            HumanTaskKey = humanTaskKey,
            CompletionEventId = "completion-1",
            Outcome = "approved"
        };
        var instance = new WorkflowInstance
        {
            Key = workflowKey,
            Status = status,
            Revision = 2,
            WaitingHumanTaskKey = waitingTaskId is null
                ? null
                : new RuntimeInstanceKey("tenant-1", waitingTaskId)
        };
        var acceptance = CreateAcceptance(request, 1, 2);
        var store = new Mock<IWorkflowInstanceStore>();
        store.Setup(value => value.GetAsync(workflowKey, It.IsAny<CancellationToken>())).ReturnsAsync(instance);
        var acceptances = new Mock<IWorkflowContinuationAcceptanceStore>();
        acceptances.Setup(value => value.GetAsync(
                new RuntimeTenantScope("tenant-1"), "completion-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(acceptance);
        acceptances.Setup(value => value.TryClaimResumeAsync(
                acceptance, It.IsAny<WorkflowContinuationResumeLease>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowContinuationResumeClaimResult.Acquired,
                new WorkflowContinuationResumeClaim(1)));
        acceptances.Setup(value => value.CompleteResumeAsync(
                acceptance, It.IsAny<WorkflowContinuationResumeClaim>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        acceptances.Setup(value => value.ReleaseResumeAsync(
                acceptance, It.IsAny<WorkflowContinuationResumeClaim>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var runner = new CountingWorkflowExecutionRunner();
        var service = new WorkflowContinuationService(
            store.Object,
            null!,
            runner,
            null!,
            null!,
            new WorkflowLifecycleEventFactory(null!, null!),
            new Mock<CrestCreates.Runtime.Persistence.Abstractions.State.IRuntimeStateContractRegistry>().Object,
            new Mock<CrestCreates.Metadata.Abstractions.Runtime.IRuntimeDescriptorPinResolver<WorkflowDescriptor>>().Object,
            null,
            acceptances.Object);

        await service.ContinueAsync(request);

        runner.Count.Should().Be(expectedRuns);
        if (expectedRuns > 0)
        {
            runner.Instance.Should().BeSameAs(instance);
            runner.OperationId.Should().Be("completion-1");
        }
        store.Verify(value => value.GetByWaitingHumanTaskAsync(It.IsAny<RuntimeInstanceKey>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AcceptedCompletionReplay_FailurePropagatesAndReleasesClaim()
    {
        var request = CreateRequest();
        var acceptance = CreateAcceptance(request, 1, 2);
        var instance = new WorkflowInstance { Key = request.WorkflowKey, Status = WorkflowInstanceStatus.Running, Revision = 2 };
        var store = new Mock<IWorkflowInstanceStore>();
        store.Setup(value => value.GetAsync(request.WorkflowKey, It.IsAny<CancellationToken>())).ReturnsAsync(instance);
        var acceptances = new Mock<IWorkflowContinuationAcceptanceStore>();
        acceptances.Setup(value => value.GetAsync(acceptance.TenantScope, acceptance.CompletionEventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(acceptance);
        var claim = new WorkflowContinuationResumeClaim(1);
        acceptances.Setup(value => value.TryClaimResumeAsync(acceptance, It.IsAny<WorkflowContinuationResumeLease>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowContinuationResumeClaimResult.Acquired, claim));
        acceptances.Setup(value => value.ReleaseResumeAsync(acceptance, claim, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var runner = new CountingWorkflowExecutionRunner { Failure = new InvalidOperationException("resume failed") };
        var service = CreateService(store.Object, acceptances.Object, runner);

        var action = () => service.ContinueAsync(request);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("resume failed");
        acceptances.Verify(value => value.ReleaseResumeAsync(acceptance, claim, CancellationToken.None), Times.Once);
        acceptances.Verify(value => value.CompleteResumeAsync(It.IsAny<WorkflowContinuationAcceptance>(), It.IsAny<WorkflowContinuationResumeClaim>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WorkflowContinuationConsumer_CancelledReplayRequestsRetry()
    {
        var continuation = new Mock<IWorkflowContinuationService>();
        continuation.Setup(value => value.ContinueAsync(It.IsAny<WorkflowContinuationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var consumer = new WorkflowContinuationOutboxConsumer(continuation.Object);
        var message = new DefaultOutboxMessageFactory().Create("completion-cancelled", "tenant-1", "contract/v1", "payload/v1", [1]);
        var context = new OutboxDeliveryContext
        {
            Message = message,
            Lease = new OutboxDeliveryLease { OwnerId = "worker", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1), Attempt = 1, Fence = 1 },
            AttemptDeadline = DateTimeOffset.UtcNow.AddSeconds(30),
            Services = null!
        };

        var result = await consumer.ConsumeAsync(new HumanTaskCompletedEvent
        {
            EventId = "completion-cancelled",
            HumanTaskKey = new RuntimeInstanceKey("tenant-1", "task-1"),
            Outcome = "Approve",
            WorkflowKey = new RuntimeInstanceKey("tenant-1", "workflow-1")
        }, context, CancellationToken.None);

        result.Outcome.Should().Be(OutboxDeliveryOutcome.Retry);
        result.FailureCode.Should().Be("WORKFLOW_CONTINUATION_CANCELLED");
    }

    [Fact]
    public async Task DurableResumeClaim_AllowsOneActiveOwnerAndRetainsCompletion()
    {
        using var provider = new ServiceCollection().AddCrestCreatesInMemoryRuntimePersistence().BuildServiceProvider();
        var coordinator = provider.GetRequiredService<IRuntimeTransactionCoordinator>();
        var workflows = provider.GetRequiredService<IWorkflowInstanceStore>();
        var acceptances = provider.GetRequiredService<IWorkflowContinuationAcceptanceStore>();
        var request = CreateRequest();
        var acceptance = CreateAcceptance(request, 0, 1);
        var workflow = new WorkflowInstance { Key = request.WorkflowKey, Status = WorkflowInstanceStatus.Running, WorkflowPin = ValidPin() };
        await coordinator.ExecuteAsync(async ct =>
        {
            await workflows.AddAsync(workflow, ct);
            (await acceptances.AddAsync(acceptance, ct)).Should().Be(WorkflowContinuationAcceptanceWriteResult.Accepted);
        });
        var firstLease = new WorkflowContinuationResumeLease("worker-1", 1, DateTimeOffset.UtcNow.AddMinutes(1));
        var secondLease = new WorkflowContinuationResumeLease("worker-2", 1, DateTimeOffset.UtcNow.AddMinutes(1));

        var first = await acceptances.TryClaimResumeAsync(acceptance, firstLease);
        var second = await acceptances.TryClaimResumeAsync(acceptance, secondLease);

        first.Result.Should().Be(WorkflowContinuationResumeClaimResult.Acquired);
        second.Result.Should().Be(WorkflowContinuationResumeClaimResult.AlreadyInProgress);
        (await acceptances.CompleteResumeAsync(acceptance, first.Claim!)).Should().BeTrue();
        (await acceptances.TryClaimResumeAsync(acceptance, secondLease)).Result.Should().Be(WorkflowContinuationResumeClaimResult.NoResumeRequired);
    }

    [Fact]
    public async Task DurableResumeClaim_RejectsOldAcceptanceAfterLaterRevision()
    {
        using var provider = new ServiceCollection().AddCrestCreatesInMemoryRuntimePersistence().BuildServiceProvider();
        var coordinator = provider.GetRequiredService<IRuntimeTransactionCoordinator>();
        var workflows = provider.GetRequiredService<IWorkflowInstanceStore>();
        var acceptances = provider.GetRequiredService<IWorkflowContinuationAcceptanceStore>();
        var request = CreateRequest();
        var acceptance = CreateAcceptance(request, 0, 1);
        var workflow = new WorkflowInstance { Key = request.WorkflowKey, Status = WorkflowInstanceStatus.Running, WorkflowPin = ValidPin() };
        await coordinator.ExecuteAsync(async ct =>
        {
            await workflows.AddAsync(workflow, ct);
            await acceptances.AddAsync(acceptance, ct);
            workflow.Revision = 1;
            await workflows.UpdateAsync(workflow, 1, ct);
            workflow.Revision = 2;
        });

        var result = await acceptances.TryClaimResumeAsync(acceptance,
            new WorkflowContinuationResumeLease("worker", 1, DateTimeOffset.UtcNow.AddMinutes(1)));

        result.Result.Should().Be(WorkflowContinuationResumeClaimResult.NoResumeRequired);
        result.Claim.Should().BeNull();
    }

    [Fact]
    public async Task DurableResumeClaim_AmbientRollbackDoesNotReleaseOrCompleteCommittedClaim()
    {
        using var provider = new ServiceCollection().AddCrestCreatesInMemoryRuntimePersistence().BuildServiceProvider();
        var coordinator = provider.GetRequiredService<IRuntimeTransactionCoordinator>();
        var workflows = provider.GetRequiredService<IWorkflowInstanceStore>();
        var acceptances = provider.GetRequiredService<IWorkflowContinuationAcceptanceStore>();
        var request = CreateRequest();
        var acceptance = CreateAcceptance(request, 0, 1);
        await coordinator.ExecuteAsync(async ct =>
        {
            await workflows.AddAsync(new WorkflowInstance
            {
                Key = request.WorkflowKey,
                Status = WorkflowInstanceStatus.Running,
                WorkflowPin = ValidPin()
            }, ct);
            await acceptances.AddAsync(acceptance, ct);
        });
        var lease = new WorkflowContinuationResumeLease("worker", 1, DateTimeOffset.UtcNow.AddMinutes(1));
        var claim = await acceptances.TryClaimResumeAsync(acceptance, lease);
        claim.Result.Should().Be(WorkflowContinuationResumeClaimResult.Acquired);

        var release = () => coordinator.ExecuteAsync(async ct =>
        {
            await acceptances.ReleaseResumeAsync(acceptance, claim.Claim!, ct);
            throw new InvalidOperationException("rollback release");
        }).AsTask();
        await release.Should().ThrowAsync<InvalidOperationException>().WithMessage("rollback release");
        (await acceptances.TryClaimResumeAsync(acceptance, lease)).Result.Should().Be(WorkflowContinuationResumeClaimResult.AlreadyInProgress);

        var complete = () => coordinator.ExecuteAsync(async ct =>
        {
            await acceptances.CompleteResumeAsync(acceptance, claim.Claim!, ct);
            throw new InvalidOperationException("rollback completion");
        }).AsTask();
        await complete.Should().ThrowAsync<InvalidOperationException>().WithMessage("rollback completion");
        (await acceptances.TryClaimResumeAsync(acceptance, lease)).Result.Should().Be(WorkflowContinuationResumeClaimResult.AlreadyInProgress);
    }

    private static RuntimeDescriptorPin ValidPin() => new()
    {
        Ref = new CrestCreates.Metadata.Abstractions.DescriptorRef("workflow", "workflow-1", 1),
        ContractHash = new CanonicalHash { Value = "c", Algorithm = "SHA-256", AlgorithmVersion = "1", ArtifactKind = "Descriptor", Scope = "InternalFull", Purpose = "Contract", ContractVersion = "v1", CanonicalShapeVersion = "v1" },
        DefinitionHash = new CanonicalHash { Value = "d", Algorithm = "SHA-256", AlgorithmVersion = "1", ArtifactKind = "Descriptor", Scope = "InternalFull", Purpose = "Definition", ContractVersion = "v1", CanonicalShapeVersion = "v1" }
    };

    private static WorkflowContinuationRequest CreateRequest() => new()
    {
        WorkflowKey = new RuntimeInstanceKey("tenant-1", "workflow-1"),
        HumanTaskKey = new RuntimeInstanceKey("tenant-1", "task-1"),
        CompletionEventId = "completion-1",
        Outcome = "approved"
    };

    private static WorkflowContinuationService CreateService(
        IWorkflowInstanceStore store,
        IWorkflowContinuationAcceptanceStore acceptances,
        IWorkflowExecutionRunner runner)
        => new(store, null!, runner, null!, null!, new WorkflowLifecycleEventFactory(null!, null!),
            new Mock<CrestCreates.Runtime.Persistence.Abstractions.State.IRuntimeStateContractRegistry>().Object,
            new Mock<CrestCreates.Metadata.Abstractions.Runtime.IRuntimeDescriptorPinResolver<WorkflowDescriptor>>().Object,
            null, acceptances);

    private sealed class CountingWorkflowExecutionRunner : IWorkflowExecutionRunner
    {
        public int Count { get; private set; }
        public WorkflowInstance? Instance { get; private set; }
        public string? OperationId { get; private set; }
        public Exception? Failure { get; init; }

        public Task<WorkflowInstance> RunAsync(WorkflowInstance instance, string workflowRunOperationId,
            string? enclosingAuditId, CancellationToken ct)
        {
            Count++;
            Instance = instance;
            OperationId = workflowRunOperationId;
            if (Failure is not null) throw Failure;
            return Task.FromResult(instance);
        }
    }

    private static WorkflowContinuationAcceptance CreateAcceptance(
        WorkflowContinuationRequest request, long fromRevision, long toRevision)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            writer.WriteStringValue(request.WorkflowKey.TenantId);
            writer.WriteStringValue(request.CompletionEventId);
            writer.WriteStringValue(request.HumanTaskKey.TenantId);
            writer.WriteStringValue(request.HumanTaskKey.InstanceId);
            writer.WriteStringValue(request.WorkflowKey.TenantId);
            writer.WriteStringValue(request.WorkflowKey.InstanceId);
            writer.WriteStringValue(request.Outcome);
            writer.WriteNullValue();
            writer.WriteNumberValue(fromRevision);
            writer.WriteNumberValue(toRevision);
            writer.WriteEndArray();
        }

        return new WorkflowContinuationAcceptance
        {
            TenantScope = new RuntimeTenantScope(request.WorkflowKey.TenantId ?? throw new InvalidOperationException("A tenant is required.")),
            CompletionEventId = request.CompletionEventId ?? throw new InvalidOperationException("A completion event id is required."),
            HumanTaskKey = request.HumanTaskKey,
            WorkflowKey = request.WorkflowKey,
            Outcome = request.Outcome,
            WorkflowFromRevision = fromRevision,
            WorkflowToRevision = toRevision,
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

    [Fact]
    public async Task MissingWaitingCorrelationAndAcceptanceFailsClosed()
    {
        var workflowKey = new RuntimeInstanceKey("tenant-1", "workflow-1");
        var humanTaskKey = new RuntimeInstanceKey("tenant-1", "task-1");
        var request = new WorkflowContinuationRequest
        {
            WorkflowKey = workflowKey,
            HumanTaskKey = humanTaskKey,
            CompletionEventId = "completion-1",
            Outcome = "approved"
        };
        var store = new Mock<IWorkflowInstanceStore>();
        store.Setup(value => value.GetByWaitingHumanTaskAsync(humanTaskKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowInstance?)null);
        var acceptances = new Mock<IWorkflowContinuationAcceptanceStore>();
        acceptances.Setup(value => value.GetAsync(
                new RuntimeTenantScope("tenant-1"), "completion-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowContinuationAcceptance?)null);
        var stateRegistry = new Mock<CrestCreates.Runtime.Persistence.Abstractions.State.IRuntimeStateContractRegistry>();
        var pinResolver = new Mock<CrestCreates.Metadata.Abstractions.Runtime.IRuntimeDescriptorPinResolver<WorkflowDescriptor>>();
        var service = new WorkflowContinuationService(
            store.Object,
            null!,
            null!,
            null!,
            null!,
            new WorkflowLifecycleEventFactory(null!, null!),
            stateRegistry.Object,
            pinResolver.Object,
            null,
            acceptances.Object);

        var action = () => service.ContinueAsync(request);

        var exception = await action.Should().ThrowAsync<RuntimePersistenceContractException>();
        exception.Which.Code.Should().Be(RuntimePersistenceContractErrorCode.WaitingTaskCorrelationConflict);
        exception.Which.Message.Should().Contain("neither a waiting correlation nor an exact durable acceptance");
        acceptances.Verify(value => value.GetAsync(
            new RuntimeTenantScope("tenant-1"), "completion-1", It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(value => value.GetByWaitingHumanTaskAsync(humanTaskKey, It.IsAny<CancellationToken>()), Times.Once);
    }
}
