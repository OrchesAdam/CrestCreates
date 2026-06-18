using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class WorkflowEventConsumer : IWorkflowEventConsumer
{
    private readonly IWorkflowEngine _engine;
    private readonly IWorkflowRegistry _registry;

    public WorkflowEventConsumer(IWorkflowEngine engine, IWorkflowRegistry registry)
    {
        _engine = engine;
        _registry = registry;
    }

    public async Task OnCapabilityEventAsync(string eventName, object? payload, CancellationToken ct)
    {
        if (eventName != "capability.succeeded" && eventName != "capability.failed")
            return;

        // Future: match suspended workflow instances by correlationId in the event payload
        // and call _engine.ResumeAsync(instanceId)
        // Current phase: infrastructure is ready, matching logic is deferred
        // until HumanTask completion outcome integration is implemented
    }
}