using System.Reflection;
using CrestCreates.Event.Abstractions;
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

/// <summary>
/// Guards against descriptor field additions that are not reflected in
/// <see cref="DescriptorStableHashBuilder"/>. Every public instance property
/// on each descriptor type must be explicitly classified in the coverage
/// policy. Adding a new property without updating the policy causes a
/// test failure — preventing silent hash gaps.
///
/// Classification principles (Phase 6g):
/// - <see cref="HashFieldCoverage.Contract"/>: affects invocation, execution,
///   binding, I/O structure, event payload, or workflow runtime behavior.
/// - <see cref="HashFieldCoverage.DefinitionOnly"/>: display metadata,
///   labels, validation rules, layout — changes definition without
///   changing externally observable contract.
/// - <see cref="HashFieldCoverage.ExcludedWithReason"/>: computed
///   constants, hash outputs, properties intentionally excluded
///   from hashing with a documented reason.
/// </summary>
public sealed class DescriptorStableHashCoverageTests
{
    private enum HashFieldCoverage
    {
        Contract,
        DefinitionOnly,
        RuntimeReserved,
        BindingReserved,
        ExcludedWithReason
    }

    private sealed record FieldCoverage(
        string PropertyName,
        HashFieldCoverage Coverage,
        string Reason);

    // ═══════════════════════════════════════════════════════════
    // Coverage Policy
    // ═══════════════════════════════════════════════════════════

    private static readonly Dictionary<Type, FieldCoverage[]> Coverage = new()
    {
        // ── SchemaDescriptor ──────────────────────────────────
        [typeof(SchemaDescriptor)] = new FieldCoverage[]
        {
            new(nameof(SchemaDescriptor.Namespace), HashFieldCoverage.ExcludedWithReason, "Computed per-kind constant; not independently hashed"),
            new(nameof(SchemaDescriptor.Kind), HashFieldCoverage.ExcludedWithReason, "Computed per-kind constant; not independently hashed"),
            new(nameof(SchemaDescriptor.Id), HashFieldCoverage.Contract, "Stable identity — part of descriptor ref"),
            new(nameof(SchemaDescriptor.Name), HashFieldCoverage.Contract, "Human-readable identity; included in both hashes"),
            new(nameof(SchemaDescriptor.State), HashFieldCoverage.Contract, "Lifecycle state affects consumption eligibility"),
            new(nameof(SchemaDescriptor.ContractHash), HashFieldCoverage.ExcludedWithReason, "Output of hash computation, not input"),
            new(nameof(SchemaDescriptor.DefinitionHash), HashFieldCoverage.ExcludedWithReason, "Output of hash computation, not input"),
            new(nameof(SchemaDescriptor.SupersededById), HashFieldCoverage.Contract, "Affects version resolution and deprecation chain"),
            new(nameof(SchemaDescriptor.Version), HashFieldCoverage.Contract, "Versioned identity — part of descriptor ref"),
            new(nameof(SchemaDescriptor.ChangeKind), HashFieldCoverage.Contract, "Schema evolution kind affects compatibility"),
            new(nameof(SchemaDescriptor.Fields), HashFieldCoverage.Contract, "Structural schema fields — define data shape"),
            new(nameof(SchemaDescriptor.ValidationRules), HashFieldCoverage.DefinitionOnly, "Validation expressions — change validation without changing structure"),
            new(nameof(SchemaDescriptor.References), HashFieldCoverage.Contract, "Schema references affect dependency graph and compatibility"),
        },

        // ── SchemaFieldDescriptor ─────────────────────────────
        [typeof(SchemaFieldDescriptor)] = new FieldCoverage[]
        {
            new(nameof(SchemaFieldDescriptor.Name), HashFieldCoverage.Contract, "Field identity — referenced by form, validation, mapping"),
            new(nameof(SchemaFieldDescriptor.FieldType), HashFieldCoverage.Contract, "Data type — changes contract"),
            new(nameof(SchemaFieldDescriptor.IsRequired), HashFieldCoverage.Contract, "Requiredness — changes contract"),
            new(nameof(SchemaFieldDescriptor.IsNullable), HashFieldCoverage.Contract, "Nullability — changes contract"),
            new(nameof(SchemaFieldDescriptor.MaxLength), HashFieldCoverage.Contract, "Length constraint — changes contract"),
            new(nameof(SchemaFieldDescriptor.MinLength), HashFieldCoverage.Contract, "Length constraint — changes contract"),
            new(nameof(SchemaFieldDescriptor.MaxValue), HashFieldCoverage.Contract, "Value constraint — changes contract"),
            new(nameof(SchemaFieldDescriptor.MinValue), HashFieldCoverage.Contract, "Value constraint — changes contract"),
            new(nameof(SchemaFieldDescriptor.Pattern), HashFieldCoverage.Contract, "Regex pattern — changes validation contract"),
            new(nameof(SchemaFieldDescriptor.IsCollection), HashFieldCoverage.Contract, "Cardinality — changes data shape"),
            new(nameof(SchemaFieldDescriptor.CollectionElementType), HashFieldCoverage.Contract, "Collection element type — changes data shape"),
        },

        // ── CapabilityDescriptor ──────────────────────────────
        [typeof(CapabilityDescriptor)] = new FieldCoverage[]
        {
            new(nameof(CapabilityDescriptor.Namespace), HashFieldCoverage.ExcludedWithReason, "Computed per-kind constant; not independently hashed"),
            new(nameof(CapabilityDescriptor.Id), HashFieldCoverage.Contract, "Stable identity — part of descriptor ref"),
            new(nameof(CapabilityDescriptor.Name), HashFieldCoverage.Contract, "Human-readable identity; included in both hashes"),
            new(nameof(CapabilityDescriptor.Kind), HashFieldCoverage.ExcludedWithReason, "Computed per-kind constant; not independently hashed"),
            new(nameof(CapabilityDescriptor.State), HashFieldCoverage.Contract, "Lifecycle state affects consumption eligibility"),
            new(nameof(CapabilityDescriptor.SupersededById), HashFieldCoverage.Contract, "Affects version resolution"),
            new(nameof(CapabilityDescriptor.Version), HashFieldCoverage.Contract, "Versioned identity"),
            new(nameof(CapabilityDescriptor.ContractHash), HashFieldCoverage.ExcludedWithReason, "Output of hash computation, not input"),
            new(nameof(CapabilityDescriptor.DefinitionHash), HashFieldCoverage.ExcludedWithReason, "Output of hash computation, not input"),
            new(nameof(CapabilityDescriptor.Categories), HashFieldCoverage.DefinitionOnly, "Taxonomy/categorization — does not change execution contract"),
            new(nameof(CapabilityDescriptor.Produces), HashFieldCoverage.DefinitionOnly, "Declared event production — metadata, not execution contract"),
            new(nameof(CapabilityDescriptor.Consumes), HashFieldCoverage.DefinitionOnly, "Declared event consumption — metadata, not execution contract"),
            new(nameof(CapabilityDescriptor.SemanticTags), HashFieldCoverage.Contract, "Semantic tags affect discovery and capability matching"),
            new(nameof(CapabilityDescriptor.CapabilityKind), HashFieldCoverage.Contract, "Query/Command kind determines execution semantics"),
            new(nameof(CapabilityDescriptor.InputSchema), HashFieldCoverage.Contract, "Input schema defines capability contract"),
            new(nameof(CapabilityDescriptor.OutputSchema), HashFieldCoverage.Contract, "Output schema defines capability contract"),
            new(nameof(CapabilityDescriptor.Permissions), HashFieldCoverage.Contract, "Permissions gate execution — changes security contract"),
            new(nameof(CapabilityDescriptor.RiskLevel), HashFieldCoverage.Contract, "Risk level affects execution policy and audit"),
        },

        // ── EventDescriptor ───────────────────────────────────
        [typeof(EventDescriptor)] = new FieldCoverage[]
        {
            new(nameof(EventDescriptor.Namespace), HashFieldCoverage.ExcludedWithReason, "Computed per-kind constant; not independently hashed"),
            new(nameof(EventDescriptor.Kind), HashFieldCoverage.ExcludedWithReason, "Computed per-kind constant; not independently hashed"),
            new(nameof(EventDescriptor.Id), HashFieldCoverage.Contract, "Stable identity"),
            new(nameof(EventDescriptor.Name), HashFieldCoverage.Contract, "Human-readable identity"),
            new(nameof(EventDescriptor.State), HashFieldCoverage.Contract, "Lifecycle state"),
            new(nameof(EventDescriptor.SupersededById), HashFieldCoverage.Contract, "Affects version resolution"),
            new(nameof(EventDescriptor.ContractHash), HashFieldCoverage.ExcludedWithReason, "Output of hash, not input"),
            new(nameof(EventDescriptor.DefinitionHash), HashFieldCoverage.ExcludedWithReason, "Output of hash, not input"),
            new(nameof(EventDescriptor.Version), HashFieldCoverage.Contract, "Versioned identity"),
            new(nameof(EventDescriptor.PayloadSchema), HashFieldCoverage.Contract, "Payload schema defines event contract"),
            new(nameof(EventDescriptor.Category), HashFieldCoverage.Contract, "Event category affects routing and handling"),
            new(nameof(EventDescriptor.Semantic), HashFieldCoverage.Contract, "Event semantic affects processing"),
            new(nameof(EventDescriptor.Importance), HashFieldCoverage.Contract, "Importance drives infrastructure policy"),
            new(nameof(EventDescriptor.ChangeKind), HashFieldCoverage.Contract, "Schema evolution kind for payload"),
        },

        // ── FormDescriptor ────────────────────────────────────
        [typeof(FormDescriptor)] = new FieldCoverage[]
        {
            new(nameof(FormDescriptor.Namespace), HashFieldCoverage.ExcludedWithReason, "Computed per-kind constant"),
            new(nameof(FormDescriptor.Kind), HashFieldCoverage.ExcludedWithReason, "Computed per-kind constant"),
            new(nameof(FormDescriptor.Id), HashFieldCoverage.Contract, "Stable identity"),
            new(nameof(FormDescriptor.Name), HashFieldCoverage.Contract, "Human-readable identity"),
            new(nameof(FormDescriptor.State), HashFieldCoverage.Contract, "Lifecycle state"),
            new(nameof(FormDescriptor.SupersededById), HashFieldCoverage.Contract, "Affects version resolution"),
            new(nameof(FormDescriptor.ContractHash), HashFieldCoverage.ExcludedWithReason, "Output of hash, not input"),
            new(nameof(FormDescriptor.DefinitionHash), HashFieldCoverage.ExcludedWithReason, "Output of hash, not input"),
            new(nameof(FormDescriptor.Version), HashFieldCoverage.Contract, "Versioned identity"),
            new(nameof(FormDescriptor.Schema), HashFieldCoverage.Contract, "Schema reference defines form data contract"),
            new(nameof(FormDescriptor.Fields), HashFieldCoverage.Contract, "Form fields — ControlType/IsRequiredOverride are contract-level"),
            new(nameof(FormDescriptor.LayoutColumns), HashFieldCoverage.DefinitionOnly, "Layout metadata — does not change form contract"),
        },

        // ── FormFieldDescriptor ───────────────────────────────
        [typeof(FormFieldDescriptor)] = new FieldCoverage[]
        {
            new(nameof(FormFieldDescriptor.SchemaFieldName), HashFieldCoverage.Contract, "Links form field to schema — structural"),
            new(nameof(FormFieldDescriptor.Label), HashFieldCoverage.DefinitionOnly, "Display label — cosmetic"),
            new(nameof(FormFieldDescriptor.Placeholder), HashFieldCoverage.DefinitionOnly, "Placeholder text — cosmetic"),
            new(nameof(FormFieldDescriptor.HelpText), HashFieldCoverage.DefinitionOnly, "Help text — cosmetic"),
            new(nameof(FormFieldDescriptor.FormatHint), HashFieldCoverage.DefinitionOnly, "Display format hint — cosmetic"),
            new(nameof(FormFieldDescriptor.Order), HashFieldCoverage.Contract, "Field ordering affects form interaction contract"),
            new(nameof(FormFieldDescriptor.Group), HashFieldCoverage.Contract, "Field grouping affects form structure"),
            new(nameof(FormFieldDescriptor.IsReadOnly), HashFieldCoverage.Contract, "Readonly status affects interaction contract"),
            new(nameof(FormFieldDescriptor.VisibilityCondition), HashFieldCoverage.DefinitionOnly, "Conditional visibility — display logic, not structural"),
            new(nameof(FormFieldDescriptor.ControlType), HashFieldCoverage.Contract, "Control type affects interaction contract"),
            new(nameof(FormFieldDescriptor.IsRequiredOverride), HashFieldCoverage.Contract, "Required override affects interaction contract"),
            new(nameof(FormFieldDescriptor.ValidationMessage), HashFieldCoverage.DefinitionOnly, "Validation message text — cosmetic"),
            new(nameof(FormFieldDescriptor.DefaultValueExpression), HashFieldCoverage.DefinitionOnly, "Default value — convenience, not contract"),
            new(nameof(FormFieldDescriptor.OptionsSource), HashFieldCoverage.Contract, "Options source affects interaction contract"),
            new(nameof(FormFieldDescriptor.Metadata), HashFieldCoverage.DefinitionOnly, "Extension metadata dictionary — not structural contract"),
        },

        // ── HumanTaskDescriptor ───────────────────────────────
        [typeof(HumanTaskDescriptor)] = new FieldCoverage[]
        {
            new(nameof(HumanTaskDescriptor.Namespace), HashFieldCoverage.ExcludedWithReason, "Computed per-kind constant"),
            new(nameof(HumanTaskDescriptor.Kind), HashFieldCoverage.ExcludedWithReason, "Computed per-kind constant"),
            new(nameof(HumanTaskDescriptor.Id), HashFieldCoverage.Contract, "Stable identity"),
            new(nameof(HumanTaskDescriptor.Name), HashFieldCoverage.Contract, "Human-readable identity"),
            new(nameof(HumanTaskDescriptor.State), HashFieldCoverage.Contract, "Lifecycle state"),
            new(nameof(HumanTaskDescriptor.SupersededById), HashFieldCoverage.Contract, "Affects version resolution"),
            new(nameof(HumanTaskDescriptor.ContractHash), HashFieldCoverage.ExcludedWithReason, "Output of hash, not input"),
            new(nameof(HumanTaskDescriptor.DefinitionHash), HashFieldCoverage.ExcludedWithReason, "Output of hash, not input"),
            new(nameof(HumanTaskDescriptor.Version), HashFieldCoverage.Contract, "Versioned identity"),
            new(nameof(HumanTaskDescriptor.Interaction), HashFieldCoverage.Contract, "Interaction target defines task contract"),
            new(nameof(HumanTaskDescriptor.InputSchema), HashFieldCoverage.Contract, "Input schema defines task contract"),
            new(nameof(HumanTaskDescriptor.OutputSchema), HashFieldCoverage.Contract, "Output schema defines task contract"),
            new(nameof(HumanTaskDescriptor.AssigneeStrategy), HashFieldCoverage.Contract, "Assignee strategy affects task distribution"),
            new(nameof(HumanTaskDescriptor.Timeout), HashFieldCoverage.DefinitionOnly, "Timeout duration — operational metadata, not contract identity"),
            new(nameof(HumanTaskDescriptor.Permissions), HashFieldCoverage.Contract, "Permissions gate task access"),
            new(nameof(HumanTaskDescriptor.Outcomes), HashFieldCoverage.Contract, "Completion outcomes define task contract"),
        },

        // ── WorkflowDescriptor ────────────────────────────────
        [typeof(WorkflowDescriptor)] = new FieldCoverage[]
        {
            new(nameof(WorkflowDescriptor.Namespace), HashFieldCoverage.ExcludedWithReason, "Computed per-kind constant"),
            new(nameof(WorkflowDescriptor.Kind), HashFieldCoverage.ExcludedWithReason, "Computed per-kind constant"),
            new(nameof(WorkflowDescriptor.Id), HashFieldCoverage.Contract, "Stable identity"),
            new(nameof(WorkflowDescriptor.Name), HashFieldCoverage.Contract, "Human-readable identity"),
            new(nameof(WorkflowDescriptor.State), HashFieldCoverage.Contract, "Lifecycle state"),
            new(nameof(WorkflowDescriptor.SupersededById), HashFieldCoverage.Contract, "Affects version resolution"),
            new(nameof(WorkflowDescriptor.ContractHash), HashFieldCoverage.ExcludedWithReason, "Output of hash, not input"),
            new(nameof(WorkflowDescriptor.DefinitionHash), HashFieldCoverage.ExcludedWithReason, "Output of hash, not input"),
            new(nameof(WorkflowDescriptor.Version), HashFieldCoverage.Contract, "Versioned identity"),
            new(nameof(WorkflowDescriptor.VariableSchema), HashFieldCoverage.Contract, "Variable schema defines workflow data contract"),
            new(nameof(WorkflowDescriptor.Steps), HashFieldCoverage.Contract, "Steps define workflow execution contract"),
            new(nameof(WorkflowDescriptor.DefaultVariableScope), HashFieldCoverage.Contract, "Variable scope affects execution semantics"),
        },

        // ── WorkflowStep ──────────────────────────────────────
        [typeof(WorkflowStep)] = new FieldCoverage[]
        {
            new(nameof(WorkflowStep.Id), HashFieldCoverage.Contract, "Step identity — referenced by transitions"),
            new(nameof(WorkflowStep.Name), HashFieldCoverage.DefinitionOnly, "Step display name — human-readable, not execution-critical"),
            new(nameof(WorkflowStep.Target), HashFieldCoverage.Contract, "Step target defines what is invoked at runtime"),
            new(nameof(WorkflowStep.Condition), HashFieldCoverage.Contract, "Conditional execution — affects runtime flow"),
            new(nameof(WorkflowStep.Transitions), HashFieldCoverage.Contract, "Step transitions define workflow graph"),
            new(nameof(WorkflowStep.InputMapping), HashFieldCoverage.DefinitionOnly, "Input mapping expression — wiring detail, not contract structure"),
            new(nameof(WorkflowStep.OutputMapping), HashFieldCoverage.DefinitionOnly, "Output mapping expression — wiring detail, not contract structure"),
            new(nameof(WorkflowStep.OnError), HashFieldCoverage.Contract, "Error behavior affects execution semantics"),
        },
    };

    // ═══════════════════════════════════════════════════════════
    // Guard Test: every public instance property must be listed
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Coverage_Should_List_All_Public_Instance_Properties()
    {
        foreach (var (type, coverage) in Coverage)
        {
            var actual = type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetMethod is not null)
                .Select(p => p.Name)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();

            var expected = coverage
                .Select(x => x.PropertyName)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();

            actual.Should().BeEquivalentTo(expected, opts => opts.WithStrictOrdering(),
                $"every public instance property on {type.Name} must be explicitly classified in the coverage policy. " +
                "Did you add a new property without updating the coverage dictionary?");
        }
    }

    [Fact]
    public void Coverage_ExcludedWithReason_Must_Have_NonEmpty_Reason()
    {
        foreach (var (type, coverage) in Coverage)
        {
            foreach (var field in coverage.Where(f => f.Coverage == HashFieldCoverage.ExcludedWithReason))
            {
                field.Reason.Should().NotBeNullOrWhiteSpace(
                    $"ExcludedWithReason on {type.Name}.{field.PropertyName} must have a non-empty reason");
            }
        }
    }

    [Fact]
    public void Coverage_RuntimeReserved_And_BindingReserved_Must_Have_NonEmpty_Reason()
    {
        foreach (var (type, coverage) in Coverage)
        {
            foreach (var field in coverage.Where(f =>
                         f.Coverage is HashFieldCoverage.RuntimeReserved or HashFieldCoverage.BindingReserved))
            {
                field.Reason.Should().NotBeNullOrWhiteSpace(
                    $"RuntimeReserved/BindingReserved on {type.Name}.{field.PropertyName} must have a non-empty reason");
            }
        }
    }

    [Fact]
    public void Coverage_No_Duplicate_Properties()
    {
        foreach (var (type, coverage) in Coverage)
        {
            var duplicates = coverage
                .GroupBy(f => f.PropertyName)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToArray();

            duplicates.Should().BeEmpty(
                $"duplicate property entries found for {type.Name}: {string.Join(", ", duplicates)}");
        }
    }
}
