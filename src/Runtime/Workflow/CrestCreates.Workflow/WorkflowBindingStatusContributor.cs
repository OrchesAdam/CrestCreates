using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorBinding;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.DescriptorBinding;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class WorkflowBindingStatusContributor : IDescriptorBindingStatusContributor
{
    private readonly IWorkflowRegistry _workflowRegistry;
    private readonly ISchemaRegistry _schemaRegistry;
    private readonly ICapabilityRegistry _capabilityRegistry;
    private readonly IHumanTaskRegistry _humanTaskRegistry;

    public WorkflowBindingStatusContributor(
        IWorkflowRegistry workflowRegistry, ISchemaRegistry schemaRegistry,
        ICapabilityRegistry capabilityRegistry, IHumanTaskRegistry humanTaskRegistry)
    {
        _workflowRegistry = workflowRegistry;
        _schemaRegistry = schemaRegistry;
        _capabilityRegistry = capabilityRegistry;
        _humanTaskRegistry = humanTaskRegistry;
    }

    public DescriptorKind SupportedKind => DescriptorKind.Workflow;
    public int Order => 40;

    public IReadOnlyList<IDescriptor> GetDescriptors()
    {
        return _workflowRegistry.GetAll().Cast<IDescriptor>().ToList();
    }

    public DescriptorBindingReport Evaluate(IDescriptor descriptor)
    {
        var wf = (WorkflowDescriptor)descriptor;
        var issues = new List<DescriptorBindingIssue>();
        var fullId = $"{wf.Namespace}.{wf.Id}";

        if (wf.VariableSchema.HasValue)
        {
            var refVal = wf.VariableSchema.Value;
            var schema = _schemaRegistry.GetByVersion(refVal.Id, refVal.Version);
            if (schema == null)
            {
                issues.Add(new DescriptorBindingIssue(SeverityLevel.Error, new DiagnosticCode("REF_MISSING_SCHEMA"),
                    $"Variable schema '{refVal.Id}' v{refVal.Version} not found.",
                    fullId, DescriptorKind.Workflow, "VariableSchema"));
            }
        }

        foreach (var step in wf.Steps)
        {
            switch (step.Target)
            {
                case CapabilityTarget capTarget:
                    var cap = _capabilityRegistry.GetByVersion(capTarget.Capability.Id, capTarget.Capability.Version);
                    if (cap == null)
                    {
                        issues.Add(new DescriptorBindingIssue(SeverityLevel.Error, new DiagnosticCode("REF_MISSING_TARGET"),
                            $"Capability target '{capTarget.Capability.Id}' v{capTarget.Capability.Version} not found.",
                            fullId, DescriptorKind.Workflow, $"Steps[{step.Id}].Target"));
                    }
                    break;
                case HumanTaskTarget taskTarget:
                    var task = _humanTaskRegistry.GetByVersion(taskTarget.HumanTask.Id, taskTarget.HumanTask.Version);
                    if (task == null)
                    {
                        issues.Add(new DescriptorBindingIssue(SeverityLevel.Error, new DiagnosticCode("REF_MISSING_TARGET"),
                            $"HumanTask target '{taskTarget.HumanTask.Id}' v{taskTarget.HumanTask.Version} not found.",
                            fullId, DescriptorKind.Workflow, $"Steps[{step.Id}].Target"));
                    }
                    break;
                case SubWorkflowTarget:
                    issues.Add(new DescriptorBindingIssue(SeverityLevel.Error, new DiagnosticCode("UNSUPPORTED_SUBWORKFLOW"),
                        $"Step '{step.Id}' uses SubWorkflowTarget which is not supported by the current runtime.",
                        fullId, DescriptorKind.Workflow, $"Steps[{step.Id}].Target"));
                    break;
            }

            if (step.OnError == StepErrorBehavior.Retry)
            {
                issues.Add(new DescriptorBindingIssue(SeverityLevel.Error, new DiagnosticCode("UNSUPPORTED_RETRY"),
                    $"Step '{step.Id}' uses Retry which is not supported by the current runtime.",
                    fullId, DescriptorKind.Workflow, $"Steps[{step.Id}].OnError"));
            }

            if (step.OnError == StepErrorBehavior.Compensate)
            {
                issues.Add(new DescriptorBindingIssue(SeverityLevel.Error, new DiagnosticCode("UNSUPPORTED_COMPENSATE"),
                    $"Step '{step.Id}' uses Compensate which is not supported by the current runtime.",
                    fullId, DescriptorKind.Workflow, $"Steps[{step.Id}].OnError"));
            }

            if (step.Transitions?.Count > 0)
            {
                issues.Add(new DescriptorBindingIssue(SeverityLevel.Error, new DiagnosticCode("UNSUPPORTED_TRANSITIONS"),
                    $"Step '{step.Id}' has transitions which are not supported by the current runtime.",
                    fullId, DescriptorKind.Workflow, $"Steps[{step.Id}].Transitions"));
            }
        }

        var status = BindingStatusSynthesizer.SynthesizeStatus(issues);
        return new DescriptorBindingReport
        {
            DescriptorId = fullId,
            DescriptorKind = DescriptorKind.Workflow,
            Status = status,
            Issues = issues
        };
    }
}
