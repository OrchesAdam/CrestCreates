using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public class WorkflowStateMachineTests
{
    private readonly IWorkflowStateMachine _machine = new DefaultWorkflowStateMachine();

    [Fact]
    public void ValidateTransition_RunningToSuspended_DoesNotThrow()
        => new Action(() => _machine.ValidateTransition(
            WorkflowInstanceStatus.Running, WorkflowInstanceStatus.Suspended))
            .Should().NotThrow();

    [Fact]
    public void ValidateTransition_RunningToCompleted_DoesNotThrow()
        => new Action(() => _machine.ValidateTransition(
            WorkflowInstanceStatus.Running, WorkflowInstanceStatus.Completed))
            .Should().NotThrow();

    [Fact]
    public void ValidateTransition_RunningToFailed_DoesNotThrow()
        => new Action(() => _machine.ValidateTransition(
            WorkflowInstanceStatus.Running, WorkflowInstanceStatus.Failed))
            .Should().NotThrow();

    [Fact]
    public void ValidateTransition_SuspendedToRunning_DoesNotThrow()
        => new Action(() => _machine.ValidateTransition(
            WorkflowInstanceStatus.Suspended, WorkflowInstanceStatus.Running))
            .Should().NotThrow();

    [Fact]
    public void ValidateTransition_CompletedToRunning_Throws()
        => new Action(() => _machine.ValidateTransition(
            WorkflowInstanceStatus.Completed, WorkflowInstanceStatus.Running))
            .Should().Throw<InvalidWorkflowTransitionException>();

    [Fact]
    public void ValidateTransition_SuspendedToSuspended_Throws()
        => new Action(() => _machine.ValidateTransition(
            WorkflowInstanceStatus.Suspended, WorkflowInstanceStatus.Suspended))
            .Should().Throw<InvalidWorkflowTransitionException>();

    [Fact]
    public void ValidateTransition_FailedToRunning_Throws()
        => new Action(() => _machine.ValidateTransition(
            WorkflowInstanceStatus.Failed, WorkflowInstanceStatus.Running))
            .Should().Throw<InvalidWorkflowTransitionException>();
}
