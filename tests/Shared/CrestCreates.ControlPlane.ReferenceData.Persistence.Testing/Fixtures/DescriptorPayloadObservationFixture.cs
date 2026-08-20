using System.Collections.Immutable;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.ControlPlane.ReferenceData.Persistence.Testing;

public static class DescriptorPayloadObservationFixture
{
    public static DescriptorPayloadObservation Expected(DescriptorPayloadVariant variant)
    {
        var leaves = new List<DescriptorPayloadObservationLeaf>
        {
            Text("TenantId", "tenant-1"),
            Text("DraftId", DraftIdFor(variant)),
            Enum("DescriptorKind", (int)KindFor(variant)),
            Ticks("CreatedAt.UtcTicks", new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.FromHours(5)).UtcTicks)
        };

        switch (variant)
        {
            case DescriptorPayloadVariant.Schema:
                AddSchema(leaves);
                break;
            case DescriptorPayloadVariant.Form:
                AddForm(leaves);
                break;
            case DescriptorPayloadVariant.Capability:
                AddCapability(leaves);
                break;
            case DescriptorPayloadVariant.HumanTask:
                AddHumanTask(leaves);
                break;
            case DescriptorPayloadVariant.Event:
                AddEvent(leaves);
                break;
            case DescriptorPayloadVariant.WorkflowCapabilityTarget:
                AddWorkflow(leaves, "Capability", "capability-1");
                break;
            case DescriptorPayloadVariant.WorkflowHumanTaskTarget:
                AddWorkflow(leaves, "HumanTask", "task-1");
                break;
            case DescriptorPayloadVariant.WorkflowSubWorkflowTarget:
                AddWorkflow(leaves, "SubWorkflow", "workflow-child");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(variant), variant, null);
        }

        return new DescriptorPayloadObservation(variant, leaves.ToImmutableArray());
    }

    public static void AssertEqual(
        DescriptorPayloadObservation expected,
        DescriptorPayloadObservation actual)
    {
        if (expected.Variant != actual.Variant || expected.Leaves.Length != actual.Leaves.Length)
            throw new InvalidOperationException(
                $"Payload observation shape mismatch for {expected.Variant}: expected {expected.Leaves.Length} leaves, got {actual.Leaves.Length}.");

        for (var index = 0; index < expected.Leaves.Length; index++)
        {
            if (expected.Leaves[index] != actual.Leaves[index])
                throw new InvalidOperationException(
                    $"Payload observation mismatch at leaf {index} for {expected.Variant}: " +
                    $"expected '{expected.Leaves[index]}', got '{actual.Leaves[index]}'.");
        }
    }

    private static string DraftIdFor(DescriptorPayloadVariant variant) => variant switch
    {
        DescriptorPayloadVariant.Schema => "draft-Schema",
        DescriptorPayloadVariant.Form => "draft-Form",
        DescriptorPayloadVariant.Capability => "draft-Capability",
        DescriptorPayloadVariant.HumanTask => "draft-HumanTask",
        DescriptorPayloadVariant.Event => "draft-Event",
        DescriptorPayloadVariant.WorkflowCapabilityTarget => "draft-workflow-Capability",
        DescriptorPayloadVariant.WorkflowHumanTaskTarget => "draft-workflow-HumanTask",
        DescriptorPayloadVariant.WorkflowSubWorkflowTarget => "draft-workflow-SubWorkflow",
        _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null)
    };

    private static DescriptorKind KindFor(DescriptorPayloadVariant variant) => variant switch
    {
        DescriptorPayloadVariant.Schema => DescriptorKind.Schema,
        DescriptorPayloadVariant.Form => DescriptorKind.Form,
        DescriptorPayloadVariant.Capability => DescriptorKind.Capability,
        DescriptorPayloadVariant.HumanTask => DescriptorKind.HumanTask,
        DescriptorPayloadVariant.Event => DescriptorKind.Event,
        DescriptorPayloadVariant.WorkflowCapabilityTarget or
        DescriptorPayloadVariant.WorkflowHumanTaskTarget or
        DescriptorPayloadVariant.WorkflowSubWorkflowTarget => DescriptorKind.Workflow,
        _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null)
    };

    private static void AddSchema(List<DescriptorPayloadObservationLeaf> leaves)
    {
        Text(leaves, "Payload.Id", "schema-1");
        Text(leaves, "Payload.Name", "Schema");
        Enum(leaves, "Payload.ChangeKind", 0);
        Integer(leaves, "Payload.Fields.Count", 1);
        Text(leaves, "Payload.Fields[0].Name", "Address");
        Text(leaves, "Payload.Fields[0].FieldType", "object");
        Text(leaves, "Payload.Fields[0].ObjectSchema.Id", "address");
        Integer(leaves, "Payload.Fields[0].ObjectSchema.Version", 2);
        Enum(leaves, "Payload.Fields[0].ObjectSchema.SelectionMode", 1);
        Text(leaves, "Payload.Fields[0].ObjectSchema.ExpectedContractHash", "hash");
        Integer(leaves, "Payload.References.Count", 1);
        Text(leaves, "Payload.References[0].Id", "address");
        Integer(leaves, "Payload.References[0].Version", 2);
    }

    private static void AddForm(List<DescriptorPayloadObservationLeaf> leaves)
    {
        Text(leaves, "Payload.Id", "form-1");
        Text(leaves, "Payload.Name", "Form");
        Text(leaves, "Payload.Schema.Id", "schema-1");
        Integer(leaves, "Payload.Schema.Version", 1);
        Integer(leaves, "Payload.Fields.Count", 1);
        Text(leaves, "Payload.Fields[0].SchemaFieldName", "Address");
        Text(leaves, "Payload.Fields[0].Label", "Address");
        Text(leaves, "Payload.Fields[0].Metadata[display]", "compact");
    }

    private static void AddCapability(List<DescriptorPayloadObservationLeaf> leaves)
    {
        Text(leaves, "Payload.Id", "capability-1");
        Text(leaves, "Payload.Name", "Capability");
        Text(leaves, "Payload.Namespace", "capability");
        Text(leaves, "Payload.Categories[0]", "read");
        Text(leaves, "Payload.Produces[0].Namespace", "event");
        Text(leaves, "Payload.Produces[0].Id", "changed");
        Integer(leaves, "Payload.Produces[0].Version", 3);
        Text(leaves, "Payload.Consumes[0].Namespace", "event");
        Text(leaves, "Payload.Consumes[0].Id", "created");
        Null(leaves, "Payload.Consumes[0].Version");
        Text(leaves, "Payload.SemanticTags[0]", "safe");
        Enum(leaves, "Payload.CapabilityKind", 0);
        Text(leaves, "Payload.InputSchema.Id", "input");
        Integer(leaves, "Payload.InputSchema.Version", 1);
        Text(leaves, "Payload.OutputSchema.Id", "output");
        Integer(leaves, "Payload.OutputSchema.Version", 1);
        Text(leaves, "Payload.Permissions[0]", "read:capability");
        Enum(leaves, "Payload.RiskLevel", 2);
        Enum(leaves, "Payload.ProjectionKind", 1);
    }

    private static void AddHumanTask(List<DescriptorPayloadObservationLeaf> leaves)
    {
        Text(leaves, "Payload.Id", "task-1");
        Text(leaves, "Payload.Name", "Task");
        Text(leaves, "Payload.Interaction.Id", "form-1");
        Integer(leaves, "Payload.Interaction.Version", 1);
        Text(leaves, "Payload.InputSchema.Id", "input");
        Integer(leaves, "Payload.InputSchema.Version", 1);
        Ticks(leaves, "Payload.Timeout.Ticks", TimeSpan.FromMinutes(5).Ticks);
        Text(leaves, "Payload.Permissions", "approve");
        Integer(leaves, "Payload.Outcomes.Count", 1);
        Enum(leaves, "Payload.Outcomes[0].Condition", 0);
        Text(leaves, "Payload.Outcomes[0].Capability.Id", "capability-1");
        Integer(leaves, "Payload.Outcomes[0].Capability.Version", 1);
    }

    private static void AddEvent(List<DescriptorPayloadObservationLeaf> leaves)
    {
        Text(leaves, "Payload.Id", "event-1");
        Text(leaves, "Payload.Name", "Event");
        Text(leaves, "Payload.PayloadSchema.Id", "payload");
        Integer(leaves, "Payload.PayloadSchema.Version", 1);
        Enum(leaves, "Payload.Category", 1);
        Enum(leaves, "Payload.Semantic", 0);
        Enum(leaves, "Payload.Importance", 1);
        Enum(leaves, "Payload.ChangeKind", 1);
    }

    private static void AddWorkflow(List<DescriptorPayloadObservationLeaf> leaves, string targetKind, string targetId)
    {
        Text(leaves, "Payload.Id", "workflow-1");
        Text(leaves, "Payload.Name", "Workflow");
        Text(leaves, "Payload.VariableSchema.Id", "variables");
        Integer(leaves, "Payload.VariableSchema.Version", 1);
        Integer(leaves, "Payload.Steps.Count", 1);
        Text(leaves, "Payload.Steps[0].Id", "step-1");
        Text(leaves, "Payload.Steps[0].Name", "Step");
        Text(leaves, "Payload.Steps[0].Target.Kind", targetKind);
        Text(leaves, "Payload.Steps[0].Target.Reference.Id", targetId);
        Integer(leaves, "Payload.Steps[0].Target.Reference.Version", 1);
        Text(leaves, "Payload.Steps[0].Condition", "ready");
        Text(leaves, "Payload.Steps[0].Transitions[0]", "step-2");
        Text(leaves, "Payload.Steps[0].InputMapping", "input");
        Text(leaves, "Payload.Steps[0].OutputMapping", "output");
        Enum(leaves, "Payload.Steps[0].OnError", 3);
        Enum(leaves, "Payload.DefaultVariableScope", 2);
    }

    private static DescriptorPayloadObservationLeaf Text(string path, string value)
        => new(path, ObservationValueKind.Text, value, null, null, null);

    private static void Text(List<DescriptorPayloadObservationLeaf> leaves, string path, string value)
        => leaves.Add(Text(path, value));

    private static DescriptorPayloadObservationLeaf Integer(string path, long value)
        => new(path, ObservationValueKind.Integer, null, value, null, null);

    private static void Integer(List<DescriptorPayloadObservationLeaf> leaves, string path, long value)
        => leaves.Add(Integer(path, value));

    private static DescriptorPayloadObservationLeaf Enum(string path, long value)
        => new(path, ObservationValueKind.EnumUnderlyingValue, null, value, null, null);

    private static void Enum(List<DescriptorPayloadObservationLeaf> leaves, string path, long value)
        => leaves.Add(Enum(path, value));

    private static DescriptorPayloadObservationLeaf Ticks(string path, long value)
        => new(path, ObservationValueKind.Ticks, null, value, null, null);

    private static void Ticks(List<DescriptorPayloadObservationLeaf> leaves, string path, long value)
        => leaves.Add(Ticks(path, value));

    private static DescriptorPayloadObservationLeaf Null(string path)
        => new(path, ObservationValueKind.Null, null, null, null, null);

    private static void Null(List<DescriptorPayloadObservationLeaf> leaves, string path)
        => leaves.Add(Null(path));
}
