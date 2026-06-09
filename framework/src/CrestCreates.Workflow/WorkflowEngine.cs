using System.Text.Json;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Draft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class WorkflowEngine : IWorkflowEngine
{
    private const int MaxRetries = 3;

    private readonly IWorkflowRegistry _registry;
    private readonly ICapabilityPipeline? _pipeline;
    private readonly IDraftStore? _draftStore;

    public WorkflowEngine(
        IWorkflowRegistry registry,
        ICapabilityPipeline? pipeline = null,
        IDraftStore? draftStore = null)
    {
        _registry = registry;
        _pipeline = pipeline;
        _draftStore = draftStore;
    }

    public async Task<WorkflowInstance> ExecuteAsync(
        string workflowName,
        Dictionary<string, object?>? inputVariables = null,
        CancellationToken ct = default)
    {
        var descriptor = _registry.GetActiveVersion(workflowName)
            ?? _registry.GetByName(workflowName);

        if (descriptor == null)
            throw new InvalidOperationException($"Workflow '{workflowName}' not found.");

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

    public async Task<WorkflowInstance> ResumeAsync(string instanceId, CancellationToken ct = default)
    {
        if (_draftStore == null)
            throw new InvalidOperationException("No IDraftStore registered — cannot resume workflows.");

        var checkpointId = $"wf_ckpt_{instanceId}";
        var checkpoint = await _draftStore.GetAsync(checkpointId, ct).ConfigureAwait(false);

        if (checkpoint == null)
            throw new InvalidOperationException($"No checkpoint found for instance '{instanceId}'.");

        var state = JsonSerializer.Deserialize<CheckpointState>(checkpoint.PayloadJson)
            ?? throw new InvalidOperationException("Corrupted checkpoint payload.");

        var descriptor = _registry.GetById(state.WorkflowId)
            ?? throw new InvalidOperationException($"Workflow '{state.WorkflowId}' not found.");

        var instance = new WorkflowInstance
        {
            InstanceId = state.InstanceId,
            Workflow = new VersionedDescriptorRef<WorkflowDescriptor>(state.WorkflowId, state.WorkflowVersion),
            StepIndex = state.StepIndex,
            CurrentStepId = state.CurrentStepId,
            Variables = state.Variables ?? new Dictionary<string, object?>()
        };

        return await ExecuteStepsAsync(instance, descriptor, ct).ConfigureAwait(false);
    }

    public sealed class CheckpointState
    {
        public string InstanceId { get; set; } = string.Empty;
        public string WorkflowId { get; set; } = string.Empty;
        public int WorkflowVersion { get; set; }
        public int StepIndex { get; set; }
        public string? CurrentStepId { get; set; }
        public Dictionary<string, object?>? Variables { get; set; }
    }

    private async Task<WorkflowInstance> ExecuteStepsAsync(
        WorkflowInstance instance,
        WorkflowDescriptor descriptor,
        CancellationToken ct)
    {
        var steps = descriptor.Steps;
        instance.Status = WorkflowInstanceStatus.Running;
        var retryCount = 0;

        while (instance.StepIndex < steps.Count)
        {
            ct.ThrowIfCancellationRequested();

            var step = steps[instance.StepIndex];
            instance.CurrentStepId = step.Id;

            var startedAt = DateTimeOffset.UtcNow;
            WorkflowStepResult result;

            try
            {
                result = await ExecuteStepAsync(instance, step, descriptor, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result = new WorkflowStepResult
                {
                    StepId = step.Id,
                    StepName = step.Name,
                    IsSuccess = false,
                    ErrorMessage = ex.Message,
                    Duration = DateTimeOffset.UtcNow - startedAt
                };
            }

            result = new WorkflowStepResult
            {
                StepId = result.StepId,
                StepName = step.Name,
                IsSuccess = result.IsSuccess,
                Output = result.Output,
                ErrorMessage = result.ErrorMessage,
                Duration = result.Duration
            };

            instance.StepResults.Add(result);

            if (!result.IsSuccess)
            {
                var (handled, shouldRetry) = HandleStepError(step, ref retryCount);
                if (shouldRetry)
                {
                    continue;
                }
                if (!handled)
                {
                    instance.Status = WorkflowInstanceStatus.Failed;
                    instance.ErrorMessage = result.ErrorMessage;
                    instance.CompletedAt = DateTimeOffset.UtcNow;
                    return instance;
                }
                retryCount = 0;
            }
            else
            {
                retryCount = 0;
            }

            await CheckpointAsync(instance, descriptor, ct).ConfigureAwait(false);

            // Follow transitions or sequential next
            if (step.Transitions.Count > 0)
            {
                var nextStepId = step.Transitions[0];
                var stepsList = steps.ToList();
                var nextIndex = stepsList.FindIndex(s => s.Id == nextStepId);
                instance.StepIndex = nextIndex >= 0 ? nextIndex : instance.StepIndex + 1;
            }
            else
            {
                instance.StepIndex++;
            }
        }

        instance.Status = WorkflowInstanceStatus.Completed;
        instance.CompletedAt = DateTimeOffset.UtcNow;
        instance.CurrentStepId = null;
        return instance;
    }

    private async Task<WorkflowStepResult> ExecuteStepAsync(
        WorkflowInstance instance,
        WorkflowStep step,
        WorkflowDescriptor descriptor,
        CancellationToken ct)
    {
        var startedAt = DateTimeOffset.UtcNow;

        return step.Target switch
        {
            CapabilityTarget capTarget => await ExecuteCapabilityTarget(
                instance, capTarget, ct).ConfigureAwait(false),

            HumanTaskTarget => new WorkflowStepResult
            {
                StepId = step.Id,
                IsSuccess = true,
                Duration = DateTimeOffset.UtcNow - startedAt
            },

            SubWorkflowTarget subTarget => await ExecuteSubWorkflowTarget(
                instance, subTarget, ct).ConfigureAwait(false),

            _ => new WorkflowStepResult
            {
                StepId = step.Id,
                IsSuccess = false,
                ErrorMessage = $"Unknown target type: {step.Target.GetType().Name}",
                Duration = DateTimeOffset.UtcNow - startedAt
            }
        };
    }

    private async Task<WorkflowStepResult> ExecuteCapabilityTarget(
        WorkflowInstance instance,
        CapabilityTarget target,
        CancellationToken ct)
    {
        var startedAt = DateTimeOffset.UtcNow;

        if (_pipeline == null)
        {
            return new WorkflowStepResult
            {
                StepId = instance.CurrentStepId ?? "",
                IsSuccess = false,
                ErrorMessage = "No ICapabilityPipeline registered — cannot execute CapabilityTarget.",
                Duration = DateTimeOffset.UtcNow - startedAt
            };
        }

        var capRef = target.Capability;
        var result = await _pipeline.ExecuteAsync(
            $"capability:{capRef.Id}",
            input: instance.Variables,
            ct: ct).ConfigureAwait(false);

        if (result.IsSuccess && result.Output is Dictionary<string, object?> outputVars)
        {
            foreach (var kv in outputVars)
                instance.Variables[kv.Key] = kv.Value;
        }

        return new WorkflowStepResult
        {
            StepId = instance.CurrentStepId ?? "",
            IsSuccess = result.IsSuccess,
            Output = result.Output,
            ErrorMessage = result.ErrorMessage,
            Duration = result.Duration
        };
    }

    private async Task<WorkflowStepResult> ExecuteSubWorkflowTarget(
        WorkflowInstance instance,
        SubWorkflowTarget target,
        CancellationToken ct)
    {
        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            var subRef = target.SubWorkflow;
            var subDescriptor = _registry.GetById(subRef.Id);
            if (subDescriptor == null)
            {
                return new WorkflowStepResult
                {
                    StepId = instance.CurrentStepId ?? "",
                    IsSuccess = false,
                    ErrorMessage = $"Sub-workflow '{subRef.Id}' not found.",
                    Duration = DateTimeOffset.UtcNow - startedAt
                };
            }

            var subInstance = await ExecuteAsync(
                subDescriptor.Name,
                new Dictionary<string, object?>(instance.Variables),
                ct).ConfigureAwait(false);

            if (subInstance.Status == WorkflowInstanceStatus.Completed)
            {
                foreach (var kv in subInstance.Variables)
                    instance.Variables[$"sub_{kv.Key}"] = kv.Value;
            }

            return new WorkflowStepResult
            {
                StepId = instance.CurrentStepId ?? "",
                IsSuccess = subInstance.Status == WorkflowInstanceStatus.Completed,
                Duration = DateTimeOffset.UtcNow - startedAt
            };
        }
        catch (Exception ex)
        {
            return new WorkflowStepResult
            {
                StepId = instance.CurrentStepId ?? "",
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Duration = DateTimeOffset.UtcNow - startedAt
            };
        }
    }

    private static (bool handled, bool shouldRetry) HandleStepError(
        WorkflowStep step, ref int retryCount)
    {
        return step.OnError switch
        {
            StepErrorBehavior.Retry when retryCount < MaxRetries =>
                (handled: true, shouldRetry: true),

            StepErrorBehavior.Retry =>
                (handled: false, shouldRetry: false),

            StepErrorBehavior.Skip =>
                (handled: true, shouldRetry: false),

            StepErrorBehavior.Compensate =>
                (handled: false, shouldRetry: false),

            StepErrorBehavior.Fail =>
                (handled: false, shouldRetry: false),

            _ => (handled: false, shouldRetry: false)
        };
    }

    private async Task CheckpointAsync(
        WorkflowInstance instance,
        WorkflowDescriptor descriptor,
        CancellationToken ct)
    {
        if (_draftStore == null) return;

        var checkpoint = new DraftRecord
        {
            DraftId = $"wf_ckpt_{instance.InstanceId}",
            DraftType = "workflow.checkpoint",
            Schema = new VersionedDescriptorRef<SchemaDescriptor>(
                descriptor.VariableSchema?.Id ?? "schema_workflow_vars",
                descriptor.VariableSchema?.Version ?? 1),
            TenantId = instance.Variables.TryGetValue("TenantId", out var tid)
                ? tid?.ToString() : null,
            OwnerId = instance.InstanceId,
            PayloadJson = JsonSerializer.Serialize(new
            {
                instance.InstanceId,
                instance.StepIndex,
                instance.CurrentStepId,
                instance.Variables
            }),
            Status = DraftStatus.Active
        };

        await _draftStore.SaveAsync(checkpoint, ct).ConfigureAwait(false);
    }
}
