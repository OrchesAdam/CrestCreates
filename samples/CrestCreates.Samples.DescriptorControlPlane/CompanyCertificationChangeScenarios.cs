using CrestCreates.Event.Abstractions;
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
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
    private static readonly DescriptorStableHashBuilder HashBuilder = new();

    //  Baseline

    /// <summary>
    /// Before and after are independent deep copies of the full control plane inventory.
    /// </summary>
    public static CompanyCertificationChangeScenario Baseline()
    {
        var before = CopyAllDescriptors();
        var after = CopyAllDescriptors();
        return new("Baseline: full control plane inventory", before, after);
    }

    //  OptionalFieldAddition

    /// <summary>
    /// Adds an optional <c>ContactEmail</c> string field to
    /// <c>CompanyCertificationSubmitInput</c> in the After inventory only.
    /// Form and consumers are preserved so impact traversal can discover them.
    /// </summary>
    public static CompanyCertificationChangeScenario OptionalFieldAddition()
    {
        var before = CopyAllDescriptors();

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
                .Select(CopySchemaField)
                .Append(new SchemaFieldDescriptor
                {
                    Name = "ContactEmail",
                    FieldType = "string",
                    IsRequired = false,
                    IsNullable = true,
                })
                .ToArray(),
            ValidationRules = original.ValidationRules.Select(CopyValidationRule).ToArray(),
            References = original.References.ToArray(),
        };

        var hashes = HashBuilder.Build(modified);
        modified = new SchemaDescriptor
        {
            Id = modified.Id,
            Name = modified.Name,
            Version = modified.Version,
            State = modified.State,
            ChangeKind = modified.ChangeKind,
            ContractHash = hashes.ContractHash,
            DefinitionHash = hashes.DefinitionHash,
            SupersededById = modified.SupersededById,
            Fields = modified.Fields,
            ValidationRules = modified.ValidationRules,
            References = modified.References,
        };

        var after = BuildAfter(CopyAllDescriptors(), original.Id, modified);
        return new(
            "OptionalFieldAddition: adds optional ContactEmail to SubmitInput",
            before,
            after);
    }

    //  RequiredFieldRemoval

    /// <summary>
    /// Removes the required <c>UnifiedSocialCreditCode</c> field from
    /// <c>CompanyCertificationSubmitInput</c> in the After inventory only.
    /// The corresponding form field is deliberately preserved so impact
    /// traversal can discover the dangling consumer reference.
    /// </summary>
    public static CompanyCertificationChangeScenario RequiredFieldRemoval()
    {
        var before = CopyAllDescriptors();

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
                .Select(CopySchemaField)
                .ToArray(),
            ValidationRules = original.ValidationRules.Select(CopyValidationRule).ToArray(),
            References = original.References.ToArray(),
        };

        var hashes = HashBuilder.Build(modified);
        modified = new SchemaDescriptor
        {
            Id = modified.Id,
            Name = modified.Name,
            Version = modified.Version,
            State = modified.State,
            ChangeKind = modified.ChangeKind,
            ContractHash = hashes.ContractHash,
            DefinitionHash = hashes.DefinitionHash,
            SupersededById = modified.SupersededById,
            Fields = modified.Fields,
            ValidationRules = modified.ValidationRules,
            References = modified.References,
        };

        var after = BuildAfter(CopyAllDescriptors(), original.Id, modified);
        return new(
            "RequiredFieldRemoval: removes required UnifiedSocialCreditCode from SubmitInput",
            before,
            after);
    }

    //  PermissionChange

    /// <summary>
    /// Changes <c>ApproveCompanyCertification.Permissions</c> from
    /// <c>["CompanyCertification.Approve"]</c> to
    /// <c>["CompanyCertification.SeniorApprove"]</c> in the After inventory only.
    /// No other descriptors are modified.
    /// </summary>
    public static CompanyCertificationChangeScenario PermissionChange()
    {
        var before = CopyAllDescriptors();

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

        var hashes = HashBuilder.Build(modified);
        modified = new CapabilityDescriptor
        {
            Id = modified.Id,
            Name = modified.Name,
            Version = modified.Version,
            State = modified.State,
            ContractHash = hashes.ContractHash,
            DefinitionHash = hashes.DefinitionHash,
            SupersededById = modified.SupersededById,
            CapabilityKind = modified.CapabilityKind,
            InputSchema = modified.InputSchema,
            OutputSchema = modified.OutputSchema,
            Permissions = modified.Permissions,
            RiskLevel = modified.RiskLevel,
            Produces = modified.Produces,
            Consumes = modified.Consumes,
            Categories = modified.Categories,
            SemanticTags = modified.SemanticTags,
        };

        var after = BuildAfter(CopyAllDescriptors(), original.Id, modified);
        return new(
            "PermissionChange: changes ApproveCompanyCertification.Permissions to SeniorApprove",
            before,
            after);
    }

    //  MissingWorkflowTarget

    /// <summary>
    /// Changes <c>step_submit</c> in <c>CompanyCertificationWorkflow</c> to reference
    /// a capability (<c>cap_missing_handler</c>) that does not exist in the inventory.
    /// Only the workflow descriptor is mutated; all other descriptors are preserved.
    /// </summary>
    public static CompanyCertificationChangeScenario MissingWorkflowTarget()
    {
        var before = CopyAllDescriptors();

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
                CopyWorkflowStep(original.Steps[1]),
                CopyWorkflowStep(original.Steps[2]),
            },
        };

        var hashes = HashBuilder.Build(modified);
        modified = new WorkflowDescriptor
        {
            Id = modified.Id,
            Name = modified.Name,
            Version = modified.Version,
            State = modified.State,
            ContractHash = hashes.ContractHash,
            DefinitionHash = hashes.DefinitionHash,
            SupersededById = modified.SupersededById,
            VariableSchema = modified.VariableSchema,
            DefaultVariableScope = modified.DefaultVariableScope,
            Steps = modified.Steps,
        };

        var after = BuildAfter(CopyAllDescriptors(), original.Id, modified);
        return new(
            "MissingWorkflowTarget: step_submit references missing capability cap_missing_handler",
            before,
            after);
    }

    //  UnsupportedSubWorkflow

    /// <summary>
    /// Inserts a new <c>step_sub_review</c> into <c>CompanyCertificationWorkflow</c>
    /// whose target is a <see cref="SubWorkflowTarget"/> - an existing shape in the
    /// Workflow abstractions that this sample control plane does not claim to support.
    /// The sub-workflow references <c>wf_company_certification_review_sub</c>, which
    /// does not exist in the inventory.
    /// </summary>
    public static CompanyCertificationChangeScenario UnsupportedSubWorkflow()
    {
        var before = CopyAllDescriptors();

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
                CopyWorkflowStep(original.Steps[0]),
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
                CopyWorkflowStep(original.Steps[1]),
                CopyWorkflowStep(original.Steps[2]),
            },
        };

        var hashes = HashBuilder.Build(modified);
        modified = new WorkflowDescriptor
        {
            Id = modified.Id,
            Name = modified.Name,
            Version = modified.Version,
            State = modified.State,
            ContractHash = hashes.ContractHash,
            DefinitionHash = hashes.DefinitionHash,
            SupersededById = modified.SupersededById,
            VariableSchema = modified.VariableSchema,
            DefaultVariableScope = modified.DefaultVariableScope,
            Steps = modified.Steps,
        };

        var after = BuildAfter(CopyAllDescriptors(), original.Id, modified);
        return new(
            "UnsupportedSubWorkflow: new step with SubWorkflowTarget (unsupported shape)",
            before,
            after);
    }

    //  Full inventory deep copy

    /// <summary>
    /// Returns a deep copy of every descriptor in the static Company Certification
    /// catalog. No returned object shares a reference with the static descriptors
    /// or with any other return value of this method.
    /// </summary>
    private static IReadOnlyList<IDescriptor> CopyAllDescriptors()
    {
        var list = new List<IDescriptor>(14)
        {
            // Schemas (5)
            CopySchema(CompanyCertificationDescriptors.CompanyCertificationSubmitInput),
            CopySchema(CompanyCertificationDescriptors.CompanyCertificationReviewInput),
            CopySchema(CompanyCertificationDescriptors.CompanyCertificationResult),
            CopySchema(CompanyCertificationDescriptors.CompanyCertificationApprovedPayload),
            CopySchema(CompanyCertificationDescriptors.CompanyCertificationRejectedPayload),
            // Forms (2)
            CopyForm(CompanyCertificationDescriptors.CompanyCertificationSubmitForm),
            CopyForm(CompanyCertificationDescriptors.CompanyCertificationReviewForm),
            // Capabilities (3)
            CopyCapability(CompanyCertificationDescriptors.SubmitCompanyCertification),
            CopyCapability(CompanyCertificationDescriptors.ApproveCompanyCertification),
            CopyCapability(CompanyCertificationDescriptors.RejectCompanyCertification),
            // HumanTask (1)
            CopyHumanTask(CompanyCertificationDescriptors.ReviewCompanyCertification),
            // Workflow (1)
            CopyWorkflow(CompanyCertificationDescriptors.CompanyCertificationWorkflow),
            // Events (3)
            CopyEvent(CompanyCertificationDescriptors.CompanyCertificationSubmitted),
            CopyEvent(CompanyCertificationDescriptors.CompanyCertificationApproved),
            CopyEvent(CompanyCertificationDescriptors.CompanyCertificationRejected),
        };
        return list.AsReadOnly();
    }

    //  BuildAfter - replace single descriptor

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

    //  Per-descriptor-kind deep copy helpers

    private static SchemaDescriptor CopySchema(SchemaDescriptor d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        State = d.State,
        ContractHash = d.ContractHash,
        DefinitionHash = d.DefinitionHash,
        SupersededById = d.SupersededById,
        Version = d.Version,
        ChangeKind = d.ChangeKind,
        Fields = d.Fields.Select(CopySchemaField).ToArray(),
        ValidationRules = d.ValidationRules.Select(CopyValidationRule).ToArray(),
        References = d.References.ToArray(),
    };

    private static SchemaFieldDescriptor CopySchemaField(SchemaFieldDescriptor f) => new()
    {
        Name = f.Name,
        FieldType = f.FieldType,
        IsRequired = f.IsRequired,
        IsNullable = f.IsNullable,
        MaxLength = f.MaxLength,
        MinLength = f.MinLength,
        MaxValue = f.MaxValue,
        MinValue = f.MinValue,
        Pattern = f.Pattern,
        IsCollection = f.IsCollection,
        CollectionElementType = f.CollectionElementType,
    };

    private static SchemaValidationRule CopyValidationRule(SchemaValidationRule r) => new()
    {
        Name = r.Name,
        Expression = r.Expression,
        ErrorMessage = r.ErrorMessage,
    };

    private static FormDescriptor CopyForm(FormDescriptor d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        State = d.State,
        SupersededById = d.SupersededById,
        ContractHash = d.ContractHash,
        DefinitionHash = d.DefinitionHash,
        Version = d.Version,
        Schema = d.Schema,
        Fields = d.Fields.Select(CopyFormField).ToArray(),
        LayoutColumns = d.LayoutColumns,
    };

    private static FormFieldDescriptor CopyFormField(FormFieldDescriptor f) => new()
    {
        SchemaFieldName = f.SchemaFieldName,
        Label = f.Label,
        Placeholder = f.Placeholder,
        HelpText = f.HelpText,
        FormatHint = f.FormatHint,
        Order = f.Order,
        Group = f.Group,
        IsReadOnly = f.IsReadOnly,
        VisibilityCondition = f.VisibilityCondition,
        ControlType = f.ControlType,
        IsRequiredOverride = f.IsRequiredOverride,
        ValidationMessage = f.ValidationMessage,
        DefaultValueExpression = f.DefaultValueExpression,
        OptionsSource = f.OptionsSource,
        Metadata = new Dictionary<string, string>(f.Metadata),
    };

    private static CapabilityDescriptor CopyCapability(CapabilityDescriptor d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        State = d.State,
        SupersededById = d.SupersededById,
        Version = d.Version,
        ContractHash = d.ContractHash,
        DefinitionHash = d.DefinitionHash,
        CapabilityKind = d.CapabilityKind,
        InputSchema = d.InputSchema,
        OutputSchema = d.OutputSchema,
        RiskLevel = d.RiskLevel,
        Categories = d.Categories.ToArray(),
        Produces = d.Produces.ToArray(),
        Consumes = d.Consumes.ToArray(),
        SemanticTags = d.SemanticTags.ToArray(),
        Permissions = d.Permissions.ToArray(),
    };

    private static EventDescriptor CopyEvent(EventDescriptor d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        State = d.State,
        SupersededById = d.SupersededById,
        ContractHash = d.ContractHash,
        DefinitionHash = d.DefinitionHash,
        Version = d.Version,
        PayloadSchema = d.PayloadSchema,
        Category = d.Category,
        Semantic = d.Semantic,
        Importance = d.Importance,
        ChangeKind = d.ChangeKind,
    };

    private static HumanTaskDescriptor CopyHumanTask(HumanTaskDescriptor d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        State = d.State,
        SupersededById = d.SupersededById,
        ContractHash = d.ContractHash,
        DefinitionHash = d.DefinitionHash,
        Version = d.Version,
        Interaction = d.Interaction,
        InputSchema = d.InputSchema,
        OutputSchema = d.OutputSchema,
        AssigneeStrategy = d.AssigneeStrategy,
        Timeout = d.Timeout,
        Permissions = d.Permissions,
        Outcomes = d.Outcomes.Select(CopyCompletionOutcome).ToArray(),
    };

    private static CompletionOutcome CopyCompletionOutcome(CompletionOutcome o) => new()
    {
        Condition = o.Condition,
        Capability = o.Capability,
    };

    private static WorkflowDescriptor CopyWorkflow(WorkflowDescriptor d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        State = d.State,
        SupersededById = d.SupersededById,
        ContractHash = d.ContractHash,
        DefinitionHash = d.DefinitionHash,
        Version = d.Version,
        VariableSchema = d.VariableSchema,
        DefaultVariableScope = d.DefaultVariableScope,
        Steps = d.Steps.Select(CopyWorkflowStep).ToArray(),
    };

    private static WorkflowStep CopyWorkflowStep(WorkflowStep s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Condition = s.Condition,
        InputMapping = s.InputMapping,
        OutputMapping = s.OutputMapping,
        OnError = s.OnError,
        Target = s.Target,
        Transitions = s.Transitions.ToArray(),
    };
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
