using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public sealed class WorkflowContinuationProofTests
{
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
