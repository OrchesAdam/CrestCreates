using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class WorkflowEngine : IWorkflowEngine
{
    private readonly IWorkflowRegistry _registry;
    private readonly IWorkflowStepExecutorRegistry _executorRegistry;
    private readonly IWorkflowInstanceStore _store;

    public WorkflowEngine(
        IWorkflowRegistry registry,
        IWorkflowStepExecutorRegistry executorRegistry,
        IWorkflowInstanceStore store)
    {
        _registry = registry;
        _executorRegistry = executorRegistry;
        _store = store;
    }

    public async Task<WorkflowInstance> ExecuteAsync(
        string workflowId,
        Dictionary<string, object?>? inputVariables = null,
        CancellationToken ct = default)
    {
        var descriptor = _registry.GetById(workflowId);
        if (descriptor == null)
            throw new InvalidOperationException($"Workflow '{workflowId}' not found.");

        var instance = new WorkflowInstance
        {
            Workflow = new VersionedDescriptorRef<WorkflowDescriptor>(descriptor.Id, descriptor.Version)
        };

        if (inputVariables != null)
        {
            foreach (var kv in inputVariables)
                instance.Variables[kv.Key] = kv.Value;
        }

        return await ExecuteStepsAsync(instance, descriptor, ct).ConfigureAwait(false);
    }

    private async Task<WorkflowInstance> ExecuteStepsAsync(
        WorkflowInstance instance,
        WorkflowDescriptor descriptor,
        CancellationToken ct)
    {
        var steps = descriptor.Steps;
        instance.Status = WorkflowInstanceStatus.Running;

        while (instance.StepIndex < steps.Count)
        {
            ct.ThrowIfCancellationRequested();

            var step = steps[instance.StepIndex];
            instance.CurrentStepId = step.Id;

            var startedAt = DateTimeOffset.UtcNow;

            // Resolve executor via registry — no target-type branching in engine
            var executor = _executorRegistry.Resolve(step.Target);
            var context = new WorkflowExecutionContext(descriptor, instance, step);

            StepExecutionResult stepResult;
            try
            {
                stepResult = await executor.ExecuteAsync(context, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Infrastructure/programming error — record as Failed
                var history = new WorkflowStepResult
                {
                    StepId = step.Id,
                    StepName = step.Name,
                    Status = StepExecutionStatus.Failed,
                    ErrorMessage = ex.Message,
                    ExecutedAt = DateTimeOffset.UtcNow,
                    Duration = DateTimeOffset.UtcNow - startedAt
                };
                instance.StepResults.Add(history);
                instance.Status = WorkflowInstanceStatus.Failed;
                instance.ErrorMessage = ex.Message;
                instance.CompletedAt = DateTimeOffset.UtcNow;
                await _store.SaveAsync(instance, ct).ConfigureAwait(false);
                return instance;
            }

            // Engine applies variable changes — executor is pure
            if (stepResult.Variables != null)
            {
                foreach (var kv in stepResult.Variables)
                    instance.Variables[kv.Key] = kv.Value;
            }

            // Record history
            var duration = DateTimeOffset.UtcNow - startedAt;
            var stepRecord = new WorkflowStepResult
            {
                StepId = step.Id,
                StepName = step.Name,
                Status = stepResult.Status,
                Output = stepResult.Output,
                ExecutedAt = DateTimeOffset.UtcNow,
                Duration = duration
            };
            instance.StepResults.Add(stepRecord);

            // State transitions based on executor result
            switch (stepResult.Status)
            {
                case StepExecutionStatus.Completed:
                    instance.StepIndex++;
                    continue;

                case StepExecutionStatus.Suspended:
                    instance.Status = WorkflowInstanceStatus.Suspended;
                    instance.CurrentStepId = null;
                    instance.CompletedAt = DateTimeOffset.UtcNow;
                    await _store.SaveAsync(instance, ct).ConfigureAwait(false);
                    return instance;

                case StepExecutionStatus.Failed:
                    // StepErrorBehavior.Skip: record failure, continue
                    if (step.OnError == StepErrorBehavior.Skip)
                    {
                        instance.StepIndex++;
                        continue;
                    }
                    // StepErrorBehavior.Fail (default): stop execution
                    instance.Status = WorkflowInstanceStatus.Failed;
                    instance.ErrorMessage = $"Step '{step.Id}' failed.";
                    instance.CompletedAt = DateTimeOffset.UtcNow;
                    await _store.SaveAsync(instance, ct).ConfigureAwait(false);
                    return instance;

                default:
                    throw new InvalidOperationException(
                        $"Unknown StepExecutionStatus: {stepResult.Status}");
            }
        }

        instance.Status = WorkflowInstanceStatus.Completed;
        instance.CompletedAt = DateTimeOffset.UtcNow;
        instance.CurrentStepId = null;
        await _store.SaveAsync(instance, ct).ConfigureAwait(false);
        return instance;
    }
}
