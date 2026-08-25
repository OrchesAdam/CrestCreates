using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Workflow.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;
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
