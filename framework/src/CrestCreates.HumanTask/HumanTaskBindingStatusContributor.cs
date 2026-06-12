using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.HumanTask;

public sealed class HumanTaskBindingStatusContributor : IDescriptorBindingStatusContributor
{
    private readonly IHumanTaskRegistry _taskRegistry;
    private readonly IFormRegistry _formRegistry;
    private readonly ISchemaRegistry _schemaRegistry;
    private readonly ICapabilityRegistry _capabilityRegistry;

    private static readonly HashSet<AssigneeStrategy> UnsupportedStrategies = new()
    {
        AssigneeStrategy.RoundRobin,
        AssigneeStrategy.LeastLoaded
    };

    public HumanTaskBindingStatusContributor(
        IHumanTaskRegistry taskRegistry, IFormRegistry formRegistry,
        ISchemaRegistry schemaRegistry, ICapabilityRegistry capabilityRegistry)
    {
        _taskRegistry = taskRegistry;
        _formRegistry = formRegistry;
        _schemaRegistry = schemaRegistry;
        _capabilityRegistry = capabilityRegistry;
    }

    public DescriptorKind SupportedKind => DescriptorKind.HumanTask;
    public int Order => 30;

    public IReadOnlyList<IDescriptor> GetDescriptors()
    {
        return _taskRegistry.GetAll().Cast<IDescriptor>().ToList();
    }

    public DescriptorBindingReport Evaluate(IDescriptor descriptor)
    {
        var task = (HumanTaskDescriptor)descriptor;
        var issues = new List<DescriptorBindingIssue>();
        var fullId = $"{task.Namespace}.{task.Id}";

        // Interaction is non-nullable
        var form = _formRegistry.GetByVersion(task.Interaction.Id, task.Interaction.Version);
        if (form == null)
        {
            issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "REF_MISSING_INTERACTION",
                $"Interaction form '{task.Interaction.Id}' v{task.Interaction.Version} not found.",
                fullId, DescriptorKind.HumanTask, "Interaction"));
        }

        if (task.InputSchema.HasValue)
        {
            var refVal = task.InputSchema.Value;
            var schema = _schemaRegistry.GetByVersion(refVal.Id, refVal.Version);
            if (schema == null)
            {
                issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "REF_MISSING_SCHEMA",
                    $"Input schema '{refVal.Id}' v{refVal.Version} not found.",
                    fullId, DescriptorKind.HumanTask, "InputSchema"));
            }
        }

        if (task.OutputSchema.HasValue)
        {
            var refVal = task.OutputSchema.Value;
            var schema = _schemaRegistry.GetByVersion(refVal.Id, refVal.Version);
            if (schema == null)
            {
                issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "REF_MISSING_SCHEMA",
                    $"Output schema '{refVal.Id}' v{refVal.Version} not found.",
                    fullId, DescriptorKind.HumanTask, "OutputSchema"));
            }
        }

        foreach (var outcome in task.Outcomes)
        {
            if (outcome.Capability.HasValue)
            {
                var refVal = outcome.Capability.Value;
                var cap = _capabilityRegistry.GetByVersion(refVal.Id, refVal.Version);
                if (cap == null)
                {
                    issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "REF_MISSING_CAPABILITY",
                        $"Outcome capability '{refVal.Id}' v{refVal.Version} not found.",
                        fullId, DescriptorKind.HumanTask, "Outcomes"));
                }
            }
        }

        if (UnsupportedStrategies.Contains(task.AssigneeStrategy))
        {
            issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "UNSUPPORTED_ASSIGNEE_STRATEGY",
                $"Assignee strategy '{task.AssigneeStrategy}' is not supported by the current runtime.",
                fullId, DescriptorKind.HumanTask, "AssigneeStrategy"));
        }

        var status = BindingStatusSynthesizer.SynthesizeStatus(issues);
        return new DescriptorBindingReport
        {
            DescriptorId = fullId,
            DescriptorKind = DescriptorKind.HumanTask,
            Status = status,
            Issues = issues
        };
    }
}
