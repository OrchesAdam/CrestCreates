using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Event;

public sealed class EventBindingStatusContributor : IDescriptorBindingStatusContributor
{
    private readonly IEventRegistry _eventRegistry;
    private readonly ISchemaRegistry _schemaRegistry;

    public EventBindingStatusContributor(IEventRegistry eventRegistry, ISchemaRegistry schemaRegistry)
    {
        _eventRegistry = eventRegistry;
        _schemaRegistry = schemaRegistry;
    }

    public DescriptorKind SupportedKind => DescriptorKind.Event;
    public int Order => 50;

    public IReadOnlyList<IDescriptor> GetDescriptors()
    {
        return Array.Empty<IDescriptor>();
    }

    public DescriptorBindingReport Evaluate(IDescriptor descriptor)
    {
        var evt = (GeneratedEventDescriptor)descriptor;
        var issues = new List<DescriptorBindingIssue>();
        var fullId = $"{evt.Namespace}.{evt.Id}";

        if (evt.State == DescriptorState.Deprecated)
        {
            issues.Add(new DescriptorBindingIssue(ValidationSeverity.Warning, "WARN_DEPRECATED",
                $"Event '{evt.Name}' is deprecated.", fullId, DescriptorKind.Event));
        }
        else if (evt.State == DescriptorState.Removed)
        {
            issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "UNSUPPORTED_REMOVED",
                $"Event '{evt.Name}' has been removed.", fullId, DescriptorKind.Event));
        }

        if (evt.State != DescriptorState.Removed && evt.PayloadSchemaRef.Id != null)
        {
            var schema = _schemaRegistry.GetByVersion(evt.PayloadSchemaRef.Id, evt.PayloadSchemaRef.Version);
            if (schema == null)
            {
                issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "REF_MISSING_SCHEMA",
                    $"Payload schema '{evt.PayloadSchemaRef.Id}' v{evt.PayloadSchemaRef.Version} not found.",
                    fullId, DescriptorKind.Event, "PayloadSchemaRef"));
            }
        }

        var status = BindingStatusSynthesizer.SynthesizeStatus(issues);
        return new DescriptorBindingReport
        {
            DescriptorId = fullId,
            DescriptorKind = DescriptorKind.Event,
            Status = status,
            Issues = issues
        };
    }
}
