using CrestCreates.Event.Abstractions;
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Samples.DescriptorControlPlane;

/// <summary>
/// Deterministic before/after descriptor change scenarios for the Company Certification
/// control plane. Each scenario produces independent deep copies so that downstream
/// impact traversal can discover consumers of changed descriptors without mutating
/// the static baseline or the <c>before</c> inventory.
///
/// All copying is explicit (property-by-property), avoiding reflection for AOT
/// compatibility. Every array/list property is independently allocated so later
/// mutations cannot affect the static descriptors or the counterpart inventory.
/// </summary>
public record CompanyCertificationChangeScenario(
    string Name,
    IReadOnlyList<IDescriptor> Before,
    IReadOnlyList<IDescriptor> After)
{
    private static readonly DefaultCanonicalHashComputer HashComputer = new();
    private static readonly DescriptorStableHashBuilder HashBuilder = new(HashComputer);

    // ── Baseline ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="CompanyCertificationChangeScenario"/> from a single
    /// inventory where Before and After are independent deep copies.
    /// Useful for runtime proof scenarios where the inventory is the active descriptor set.
    /// </summary>
    public static CompanyCertificationChangeScenario FromInventory(
        string name,
        IReadOnlyList<IDescriptor> inventory)
    {
        var before = inventory.Select(CompanyCertificationDescriptorCloner.CopyDescriptor).ToList().AsReadOnly();
        var after = inventory.Select(CompanyCertificationDescriptorCloner.CopyDescriptor).ToList().AsReadOnly();
        return new CompanyCertificationChangeScenario(name, before, after);
    }

    /// <summary>
    /// Before and after are independent deep copies of the full control plane inventory.
    /// </summary>
    public static CompanyCertificationChangeScenario Baseline()
    {
        var before = CompanyCertificationDescriptorCloner.CopyAllDescriptors();
        var after = CompanyCertificationDescriptorCloner.CopyAllDescriptors();
        return new("Baseline: full control plane inventory", before, after);
    }

    // ── OptionalFieldAddition ───────────────────────────────────────────

    /// <summary>
    /// Adds an optional <c>ContactEmail</c> string field to
    /// <c>CompanyCertificationSubmitInput</c> in the After inventory only.
    /// Form and consumers are preserved so impact traversal can discover them.
    /// </summary>
    public static CompanyCertificationChangeScenario OptionalFieldAddition()
    {
        var before = CompanyCertificationDescriptorCloner.CopyAllDescriptors();

        var original = CompanyCertificationDescriptors.CompanyCertificationSubmitInput;
        var modified = new SchemaDescriptor
        {
            Id = original.Id,
            Name = original.Name,
            Version = original.Version,
            State = original.State,
            ChangeKind = SchemaChangeKind.Additive,
            SupersededById = original.SupersededById,
            Fields = original.Fields
                .Select(CompanyCertificationDescriptorCloner.CopySchemaField)
                .Append(new SchemaFieldDescriptor
                {
                    Name = "ContactEmail",
                    FieldType = "string",
                    IsRequired = false,
                    IsNullable = true,
                })
                .ToArray(),
            ValidationRules = original.ValidationRules
                .Select(CompanyCertificationDescriptorCloner.CopyValidationRule)
                .ToArray(),
            References = original.References.ToArray(),
        };

        var after = BuildAfter(
            CompanyCertificationDescriptorCloner.CopyAllDescriptors(),
            original.Id, modified);
        return new(
            "OptionalFieldAddition: adds optional ContactEmail to SubmitInput",
            before,
            after);
    }

    // ── RequiredFieldRemoval ────────────────────────────────────────────

    /// <summary>
    /// Removes the required <c>UnifiedSocialCreditCode</c> field from
    /// <c>CompanyCertificationSubmitInput</c> in the After inventory only.
    /// The corresponding form field is deliberately preserved so impact
    /// traversal can discover the dangling consumer reference.
    /// </summary>
    public static CompanyCertificationChangeScenario RequiredFieldRemoval()
    {
        var before = CompanyCertificationDescriptorCloner.CopyAllDescriptors();

        var original = CompanyCertificationDescriptors.CompanyCertificationSubmitInput;
        var modified = new SchemaDescriptor
        {
            Id = original.Id,
            Name = original.Name,
            Version = original.Version,
            State = original.State,
            ChangeKind = SchemaChangeKind.Breaking,
            SupersededById = original.SupersededById,
            Fields = original.Fields
                .Where(f => f.Name != "UnifiedSocialCreditCode")
                .Select(CompanyCertificationDescriptorCloner.CopySchemaField)
                .ToArray(),
            ValidationRules = original.ValidationRules
                .Select(CompanyCertificationDescriptorCloner.CopyValidationRule)
                .ToArray(),
            References = original.References.ToArray(),
        };

        var after = BuildAfter(
            CompanyCertificationDescriptorCloner.CopyAllDescriptors(),
            original.Id, modified);
        return new(
            "RequiredFieldRemoval: removes required UnifiedSocialCreditCode from SubmitInput",
            before,
            after);
    }

    // ── PermissionChange ────────────────────────────────────────────────

    /// <summary>
    /// Changes <c>ApproveCompanyCertification.Permissions</c> from
    /// <c>["CompanyCertification.Approve"]</c> to
    /// <c>["CompanyCertification.SeniorApprove"]</c> in the After inventory only.
    /// No other descriptors are modified.
    /// </summary>
    public static CompanyCertificationChangeScenario PermissionChange()
    {
        var before = CompanyCertificationDescriptorCloner.CopyAllDescriptors();

        var original = CompanyCertificationDescriptors.ApproveCompanyCertification;
        var modified = new CapabilityDescriptor
        {
            Id = original.Id,
            Name = original.Name,
            Version = original.Version,
            State = original.State,
            SupersededById = original.SupersededById,
            CapabilityKind = original.CapabilityKind,
            InputSchema = original.InputSchema,
            OutputSchema = original.OutputSchema,
            Permissions = new[] { "CompanyCertification.SeniorApprove" },
            RiskLevel = original.RiskLevel,
            Produces = original.Produces.ToArray(),
            Consumes = original.Consumes.ToArray(),
            Categories = original.Categories.ToArray(),
            SemanticTags = original.SemanticTags.ToArray(),
        };

        var after = BuildAfter(
            CompanyCertificationDescriptorCloner.CopyAllDescriptors(),
            original.Id, modified);
        return new(
            "PermissionChange: changes ApproveCompanyCertification.Permissions to SeniorApprove",
            before,
            after);
    }

    // ── MissingWorkflowTarget ──────────────────────────────────────────

    /// <summary>
    /// Changes <c>step_submit</c> in <c>CompanyCertificationWorkflow</c> to reference
    /// a capability (<c>cap_missing_handler</c>) that does not exist in the inventory.
    /// Only the workflow descriptor is mutated; all other descriptors are preserved.
    /// </summary>
    public static CompanyCertificationChangeScenario MissingWorkflowTarget()
    {
        var before = CompanyCertificationDescriptorCloner.CopyAllDescriptors();

        var original = CompanyCertificationDescriptors.CompanyCertificationWorkflow;
        var modified = new WorkflowDescriptor
        {
            Id = original.Id,
            Name = original.Name,
            Version = original.Version,
            State = original.State,
            SupersededById = original.SupersededById,
            VariableSchema = original.VariableSchema,
            DefaultVariableScope = original.DefaultVariableScope,
            Steps = new WorkflowStep[]
            {
                new()
                {
                    Id = "step_submit",
                    Name = "Submit Certification",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<IVersionedDescriptor>(
                            "cap_missing_handler", 1),
                    },
                    Transitions = new[] { "step_review" },
                },
                CompanyCertificationDescriptorCloner.CopyWorkflowStep(original.Steps[1]),
                CompanyCertificationDescriptorCloner.CopyWorkflowStep(original.Steps[2]),
            },
        };

        var after = BuildAfter(
            CompanyCertificationDescriptorCloner.CopyAllDescriptors(),
            original.Id, modified);
        return new(
            "MissingWorkflowTarget: step_submit references missing capability cap_missing_handler",
            before,
            after);
    }

    // ── UnsupportedSubWorkflow ─────────────────────────────────────────

    /// <summary>
    /// Inserts a new <c>step_sub_review</c> into <c>CompanyCertificationWorkflow</c>
    /// whose target is a <see cref="SubWorkflowTarget"/> - an existing shape in the
    /// Workflow abstractions that this sample control plane does not claim to support.
    /// The sub-workflow references <c>wf_company_certification_review_sub</c>, which
    /// does not exist in the inventory.
    /// </summary>
    public static CompanyCertificationChangeScenario UnsupportedSubWorkflow()
    {
        var before = CompanyCertificationDescriptorCloner.CopyAllDescriptors();

        var original = CompanyCertificationDescriptors.CompanyCertificationWorkflow;
        var modified = new WorkflowDescriptor
        {
            Id = original.Id,
            Name = original.Name,
            Version = original.Version,
            State = original.State,
            SupersededById = original.SupersededById,
            VariableSchema = original.VariableSchema,
            DefaultVariableScope = original.DefaultVariableScope,
            Steps = new WorkflowStep[]
            {
                CompanyCertificationDescriptorCloner.CopyWorkflowStep(original.Steps[0]),
                new()
                {
                    Id = "step_sub_review",
                    Name = "Sub Certification Review",
                    Target = new SubWorkflowTarget
                    {
                        SubWorkflow = new VersionedDescriptorRef<WorkflowDescriptor>(
                            "wf_company_certification_review_sub", 1),
                    },
                    Transitions = Array.Empty<string>(),
                },
                CompanyCertificationDescriptorCloner.CopyWorkflowStep(original.Steps[1]),
                CompanyCertificationDescriptorCloner.CopyWorkflowStep(original.Steps[2]),
            },
        };

        var after = BuildAfter(
            CompanyCertificationDescriptorCloner.CopyAllDescriptors(),
            original.Id, modified);
        return new(
            "UnsupportedSubWorkflow: new step with SubWorkflowTarget (unsupported shape)",
            before,
            after);
    }

    // ── BuildAfter – replace single descriptor ─────────────────────────

    /// <summary>
    /// Returns a new list where the descriptor whose <see cref="IDescriptor.Id"/> equals
    /// <paramref name="id"/> is replaced by <paramref name="replacement"/>. All other
    /// descriptors are preserved by reference from the input, which is already expected
    /// to be a deep copy. The original list is never mutated.
    /// </summary>
    private static IReadOnlyList<IDescriptor> BuildAfter(
        IReadOnlyList<IDescriptor> descriptors,
        string id,
        IDescriptor replacement)
    {
        var list = new List<IDescriptor>(descriptors.Count);
        foreach (var d in descriptors)
        {
            list.Add(d.Id == id ? replacement : d);
        }
        return list.AsReadOnly();
    }
}

public static class CompanyCertificationChangeScenarios
{
    public static CompanyCertificationChangeScenario Baseline() =>
        CompanyCertificationChangeScenario.Baseline();

    public static CompanyCertificationChangeScenario OptionalFieldAddition() =>
        CompanyCertificationChangeScenario.OptionalFieldAddition();

    public static CompanyCertificationChangeScenario RequiredFieldRemoval() =>
        CompanyCertificationChangeScenario.RequiredFieldRemoval();

    public static CompanyCertificationChangeScenario PermissionChange() =>
        CompanyCertificationChangeScenario.PermissionChange();

    public static CompanyCertificationChangeScenario MissingWorkflowTarget() =>
        CompanyCertificationChangeScenario.MissingWorkflowTarget();

    public static CompanyCertificationChangeScenario UnsupportedSubWorkflow() =>
        CompanyCertificationChangeScenario.UnsupportedSubWorkflow();
}
