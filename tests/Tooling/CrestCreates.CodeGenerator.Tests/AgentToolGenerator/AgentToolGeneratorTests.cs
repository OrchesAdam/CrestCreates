using System.Linq;
using CrestCreates.CodeGenerator.AgentToolGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using FluentAssertions;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.AgentToolGenerator;

public sealed class AgentToolGeneratorTests
{
    [Fact]
    public void Typed_tool_emits_independent_provider_exact_bindings_and_json_registrations()
    {
        var result = Run(@"
namespace Demo;
public sealed class InputDto { public string Name { get; set; } }
public sealed class OutputDto { public string Id { get; set; } }

[CrestCreates.Agent.Tools.AgentToolSpecs]
public static partial class OrderTools
{
    [CrestCreates.Agent.Tools.AgentToolSpec(
        ""orders.create"",
        DescriptorId = ""agent-tool:orders.create.v2"",
        DescriptorVersion = 2,
        CapabilityVersion = 3,
        ExpectedCapabilityContractHash = ""sha256:capability"",
        InputType = typeof(InputDto),
        OutputType = typeof(OutputDto),
        ToolName = ""orders.create"",
        Title = ""Create order"",
        Description = ""Creates one validated order."",
        SelectionPolicy = CrestCreates.Metadata.AgentTool.AgentToolSelectionPolicy.AutomaticAllowed,
        SideEffectKind = CrestCreates.Metadata.AgentTool.AgentToolSideEffectKind.ExternalWrite,
        RiskFloor = CrestCreates.Agent.Tools.AgentToolRiskFloor.High,
        ApprovalMode = CrestCreates.Metadata.AgentTool.AgentToolApprovalMode.Required,
        BudgetCategory = ""order-write"",
        CostUnits = 5,
        MaxCallsPerExecution = 1,
        AuditMode = CrestCreates.Metadata.AgentTool.AgentToolAuditMode.Required,
        AllowedAgentRoles = new[] { ""sales-agent"", ""admin-agent"" })]
    public sealed class Create { }
}");

        result.Diagnostics.Should().NotContain(diagnostic => diagnostic.Id.StartsWith("ATP"));
        result.CompilationSuccess.Should().BeTrue(
            string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.ToString())));
        result.GeneratedSources.Should().HaveCount(2);

        var generated = Generated(result);
        generated.Should().Contain("IDescriptorProvider<AgentCapabilityToolDescriptor>");
        generated.Should().Contain("new CapabilityProjectionReference(");
        generated.Should().Contain("VersionSelectionMode.Exact");
        generated.Should().Contain("\"sha256:capability\"");
        generated.Should().Contain("SelectionPolicy = AgentToolSelectionPolicy.AutomaticAllowed");
        generated.Should().Contain("SideEffectKind = AgentToolSideEffectKind.ExternalWrite");
        generated.Should().Contain("RiskFloor = CapabilityRiskLevel.High");
        generated.Should().Contain("ApprovalMode = AgentToolApprovalMode.Required");
        generated.Should().Contain("Category = \"order-write\"");
        generated.Should().Contain("CostUnits = 5L");
        generated.Should().Contain("MaxCallsPerExecution = 1");
        generated.Should().Contain("AllowedAgentRoles = new string[] { \"admin-agent\", \"sales-agent\" }");
        generated.Should().Contain("AgentToolJsonContractRegistry.RegisterInputType(typeof(global::Demo.InputDto))");
        generated.Should().Contain("AgentToolJsonContractRegistry.RegisterOutputType(typeof(global::Demo.OutputDto))");
        generated.Should().Contain("JsonTypeInfo<global::Demo.InputDto>");
        generated.Should().Contain("output.GetType() != typeof(global::Demo.OutputDto)");
        generated.Should().Contain("AgentToolBindingRegistry.Register");
    }

    [Fact]
    public void Safe_defaults_emit_explicit_only_latest_inherited_risk_and_null_max_calls()
    {
        var result = Run(@"
[CrestCreates.Agent.Tools.AgentToolSpecs]
public static partial class LookupTools
{
    [CrestCreates.Agent.Tools.AgentToolSpec(
        ""orders.lookup"",
        Description = ""Looks up an order."",
        BudgetCategory = ""read"",
        AllowedAgentRoles = new[] { ""reader"" })]
    public sealed class Lookup { }
}");

        result.Diagnostics.Should().NotContain(diagnostic => diagnostic.Id.StartsWith("ATP"));
        var generated = Generated(result);
        generated.Should().Contain("Id = \"agent-tool:orders.lookup\"");
        generated.Should().Contain("ToolName = \"orders.lookup\"");
        generated.Should().Contain("VersionSelectionMode.Latest");
        generated.Should().Contain("SelectionPolicy = AgentToolSelectionPolicy.ExplicitOnly");
        generated.Should().Contain("SideEffectKind = AgentToolSideEffectKind.Unknown");
        generated.Should().Contain("RiskFloor = null");
        generated.Should().Contain("ApprovalMode = AgentToolApprovalMode.PolicyDriven");
        generated.Should().Contain("AuditMode = AgentToolAuditMode.Required");
        generated.Should().Contain("MaxCallsPerExecution = null");
    }

    [Fact]
    public void No_input_and_void_output_emit_null_contract_without_json_type_registration()
    {
        var result = Run(@"
[CrestCreates.Agent.Tools.AgentToolSpecs]
public static partial class MaintenanceTools
{
    [CrestCreates.Agent.Tools.AgentToolSpec(
        ""cache.refresh"",
        Description = ""Refreshes cache."",
        BudgetCategory = ""maintenance"",
        AllowedAgentRoles = new[] { ""operator"" })]
    public sealed class Refresh { }
}");

        var generated = Generated(result);
        generated.Should().Contain("InputType = null");
        generated.Should().Contain("OutputType = null");
        generated.Should().Contain("new ValueTask<object?>((object?)null)");
        generated.Should().Contain("new ValueTask<JsonElement?>((JsonElement?)null)");
        generated.Should().NotContain("RegisterInputType");
        generated.Should().NotContain("RegisterOutputType");
    }

    [Fact]
    public void Risk_floor_mapping_is_explicit_and_does_not_cast_between_enum_layouts()
    {
        var result = Run(@"
[CrestCreates.Agent.Tools.AgentToolSpecs]
public static partial class RiskTools
{
    [CrestCreates.Agent.Tools.AgentToolSpec(""one"", Description = ""One."", BudgetCategory = ""risk"", AllowedAgentRoles = new[] { ""r"" }, RiskFloor = CrestCreates.Agent.Tools.AgentToolRiskFloor.Low)] public sealed class Low { }
    [CrestCreates.Agent.Tools.AgentToolSpec(""two"", Description = ""Two."", BudgetCategory = ""risk"", AllowedAgentRoles = new[] { ""r"" }, RiskFloor = CrestCreates.Agent.Tools.AgentToolRiskFloor.Medium)] public sealed class Medium { }
    [CrestCreates.Agent.Tools.AgentToolSpec(""three"", Description = ""Three."", BudgetCategory = ""risk"", AllowedAgentRoles = new[] { ""r"" }, RiskFloor = CrestCreates.Agent.Tools.AgentToolRiskFloor.High, ApprovalMode = CrestCreates.Metadata.AgentTool.AgentToolApprovalMode.Required)] public sealed class High { }
    [CrestCreates.Agent.Tools.AgentToolSpec(""four"", Description = ""Four."", BudgetCategory = ""risk"", AllowedAgentRoles = new[] { ""r"" }, RiskFloor = CrestCreates.Agent.Tools.AgentToolRiskFloor.Critical, ApprovalMode = CrestCreates.Metadata.AgentTool.AgentToolApprovalMode.Required)] public sealed class Critical { }
}");

        var generated = Generated(result);
        generated.Should().Contain("RiskFloor = CapabilityRiskLevel.Low");
        generated.Should().Contain("RiskFloor = CapabilityRiskLevel.Medium");
        generated.Should().Contain("RiskFloor = CapabilityRiskLevel.High");
        generated.Should().Contain("RiskFloor = CapabilityRiskLevel.Critical");
        generated.Should().NotContain("(CapabilityRiskLevel)");
    }

    [Fact]
    public void Duplicate_tool_name_and_descriptor_identity_have_distinct_diagnostics_and_suppress_container()
    {
        var result = Run(@"
[CrestCreates.Agent.Tools.AgentToolSpecs]
public static partial class DuplicateTools
{
    [CrestCreates.Agent.Tools.AgentToolSpec(""one"", DescriptorId = ""same"", ToolName = ""same"", Description = ""One."", BudgetCategory = ""b"", AllowedAgentRoles = new[] { ""r"" })] public sealed class One { }
    [CrestCreates.Agent.Tools.AgentToolSpec(""two"", DescriptorId = ""same"", ToolName = ""same"", Description = ""Two."", BudgetCategory = ""b"", AllowedAgentRoles = new[] { ""r"" })] public sealed class Two { }
}");

        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Id == "ATP003");
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Id == "ATP008");
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void Invalid_identity_text_versions_and_budget_report_precise_diagnostics()
    {
        var result = Run(@"
[CrestCreates.Agent.Tools.AgentToolSpecs]
public static partial class BadTools
{
    [CrestCreates.Agent.Tools.AgentToolSpec(
        """",
        DescriptorId = ""bad id"",
        DescriptorVersion = 0,
        CapabilityVersion = -1,
        ToolName = ""bad tool"",
        Description = """",
        BudgetCategory = """",
        CostUnits = 0,
        MaxCallsPerExecution = -1,
        AllowedAgentRoles = new[] { ""reader"" })]
    public sealed class Bad { }
}");

        result.Diagnostics.Select(diagnostic => diagnostic.Id).Should().Contain([
            "ATP001", "ATP002", "ATP004", "ATP005", "ATP008", "ATP012", "ATP014"]);
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void Input_and_output_root_failures_are_reported_separately()
    {
        var result = Run(@"
using System.Collections.Generic;
public interface IInput { }
public abstract class AbstractOutput { }
[CrestCreates.Agent.Tools.AgentToolSpecs]
public static partial class BadRootTools
{
    [CrestCreates.Agent.Tools.AgentToolSpec(""one"", InputType = typeof(IInput), OutputType = typeof(AbstractOutput), Description = ""One."", BudgetCategory = ""b"", AllowedAgentRoles = new[] { ""r"" })] public sealed class One { }
    [CrestCreates.Agent.Tools.AgentToolSpec(""two"", InputType = typeof(Dictionary<string, object>), OutputType = typeof(List<string>), Description = ""Two."", BudgetCategory = ""b"", AllowedAgentRoles = new[] { ""r"" })] public sealed class Two { }
    [CrestCreates.Agent.Tools.AgentToolSpec(""three"", InputType = typeof(int), OutputType = typeof(string), Description = ""Three."", BudgetCategory = ""b"", AllowedAgentRoles = new[] { ""r"" })] public sealed class Three { }
}");

        result.Diagnostics.Count(diagnostic => diagnostic.Id == "ATP006").Should().Be(3);
        result.Diagnostics.Count(diagnostic => diagnostic.Id == "ATP007").Should().Be(3);
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void Framework_converter_types_are_not_treated_as_object_dto_roots()
    {
        var result = Run(@"
using System;
[CrestCreates.Agent.Tools.AgentToolSpecs]
public static partial class FrameworkRootTools
{
    [CrestCreates.Agent.Tools.AgentToolSpec(""framework"", InputType = typeof(Version), OutputType = typeof(Version), Description = ""Framework roots."", BudgetCategory = ""b"", AllowedAgentRoles = new[] { ""r"" })]
    public sealed class FrameworkRoots { }
}");

        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Id == "ATP006");
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Id == "ATP007");
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void Unknown_governance_enum_values_fail_closed()
    {
        var result = Run(@"
[CrestCreates.Agent.Tools.AgentToolSpecs]
public static partial class EnumTools
{
    [CrestCreates.Agent.Tools.AgentToolSpec(""selection"", Description = ""Selection."", BudgetCategory = ""b"", AllowedAgentRoles = new[] { ""r"" }, SelectionPolicy = (CrestCreates.Metadata.AgentTool.AgentToolSelectionPolicy)99)] public sealed class Selection { }
    [CrestCreates.Agent.Tools.AgentToolSpec(""side-effect"", Description = ""Side effect."", BudgetCategory = ""b"", AllowedAgentRoles = new[] { ""r"" }, SideEffectKind = (CrestCreates.Metadata.AgentTool.AgentToolSideEffectKind)99)] public sealed class SideEffect { }
    [CrestCreates.Agent.Tools.AgentToolSpec(""risk"", Description = ""Risk."", BudgetCategory = ""b"", AllowedAgentRoles = new[] { ""r"" }, RiskFloor = (CrestCreates.Agent.Tools.AgentToolRiskFloor)99)] public sealed class Risk { }
    [CrestCreates.Agent.Tools.AgentToolSpec(""approval"", Description = ""Approval."", BudgetCategory = ""b"", AllowedAgentRoles = new[] { ""r"" }, ApprovalMode = (CrestCreates.Metadata.AgentTool.AgentToolApprovalMode)0)] public sealed class Approval { }
    [CrestCreates.Agent.Tools.AgentToolSpec(""audit"", Description = ""Audit."", BudgetCategory = ""b"", AllowedAgentRoles = new[] { ""r"" }, AuditMode = (CrestCreates.Metadata.AgentTool.AgentToolAuditMode)0)] public sealed class Audit { }
}");

        result.Diagnostics.Count(diagnostic => diagnostic.Id == "ATP009").Should().Be(5);
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void Invalid_container_and_non_direct_spec_fail_closed_for_the_whole_container()
    {
        var invalidContainer = Run(@"
[CrestCreates.Agent.Tools.AgentToolSpecs]
public class BadContainer
{
    [CrestCreates.Agent.Tools.AgentToolSpec(""one"", Description = ""One."", BudgetCategory = ""b"", AllowedAgentRoles = new[] { ""r"" })]
    public sealed class One { }
}");
        invalidContainer.Diagnostics.Should().Contain(diagnostic => diagnostic.Id == "ATP010");
        invalidContainer.GeneratedSources.Should().BeEmpty();

        var indirectSpec = Run(@"
[CrestCreates.Agent.Tools.AgentToolSpecs]
public static partial class IndirectTools
{
    public sealed class Group
    {
        [CrestCreates.Agent.Tools.AgentToolSpec(""one"", Description = ""One."", BudgetCategory = ""b"", AllowedAgentRoles = new[] { ""r"" })]
        public sealed class One { }
    }
}");
        indirectSpec.Diagnostics.Should().Contain(diagnostic => diagnostic.Id == "ATP011");
        indirectSpec.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void Empty_duplicate_and_wildcard_roles_are_rejected()
    {
        var result = Run(@"
[CrestCreates.Agent.Tools.AgentToolSpecs]
public static partial class RoleTools
{
    [CrestCreates.Agent.Tools.AgentToolSpec(""none"", Description = ""None."", BudgetCategory = ""b"")] public sealed class None { }
    [CrestCreates.Agent.Tools.AgentToolSpec(""duplicate"", Description = ""Duplicate."", BudgetCategory = ""b"", AllowedAgentRoles = new[] { ""r"", ""r"" })] public sealed class Duplicate { }
    [CrestCreates.Agent.Tools.AgentToolSpec(""wildcard"", Description = ""Wildcard."", BudgetCategory = ""b"", AllowedAgentRoles = new[] { ""*"" })] public sealed class Wildcard { }
}");

        result.Diagnostics.Count(diagnostic => diagnostic.Id == "ATP013").Should().Be(3);
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void Unsafe_strong_governance_and_external_best_effort_audit_are_rejected()
    {
        var result = Run(@"
[CrestCreates.Agent.Tools.AgentToolSpecs]
public static partial class GovernanceTools
{
    [CrestCreates.Agent.Tools.AgentToolSpec(""high"", Description = ""High."", BudgetCategory = ""b"", AllowedAgentRoles = new[] { ""r"" }, RiskFloor = CrestCreates.Agent.Tools.AgentToolRiskFloor.High, ApprovalMode = CrestCreates.Metadata.AgentTool.AgentToolApprovalMode.None)] public sealed class High { }
    [CrestCreates.Agent.Tools.AgentToolSpec(""write"", Description = ""Write."", BudgetCategory = ""b"", AllowedAgentRoles = new[] { ""r"" }, SideEffectKind = CrestCreates.Metadata.AgentTool.AgentToolSideEffectKind.ExternalWrite, AuditMode = CrestCreates.Metadata.AgentTool.AgentToolAuditMode.BestEffort)] public sealed class Write { }
}");

        result.Diagnostics.Count(diagnostic => diagnostic.Id == "ATP016").Should().Be(2);
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void Diagnostic_catalog_contains_every_approved_generator_code()
    {
        new[]
        {
            AgentToolDiagnostics.InvalidCapabilityId.Id,
            AgentToolDiagnostics.InvalidToolName.Id,
            AgentToolDiagnostics.DuplicateToolName.Id,
            AgentToolDiagnostics.MissingRequiredText.Id,
            AgentToolDiagnostics.InvalidDescriptorVersion.Id,
            AgentToolDiagnostics.UnsupportedInputType.Id,
            AgentToolDiagnostics.UnsupportedOutputType.Id,
            AgentToolDiagnostics.InvalidDescriptorId.Id,
            AgentToolDiagnostics.InvalidEnum.Id,
            AgentToolDiagnostics.InvalidContainer.Id,
            AgentToolDiagnostics.InvalidSpec.Id,
            AgentToolDiagnostics.NegativeCapabilityVersion.Id,
            AgentToolDiagnostics.InvalidAllowedRole.Id,
            AgentToolDiagnostics.InvalidBudget.Id,
            AgentToolDiagnostics.ContradictorySideEffect.Id,
            AgentToolDiagnostics.UnsafeGovernance.Id
        }.Should().Equal(Enumerable.Range(1, 16).Select(value => $"ATP{value:000}"));
    }

    [Fact]
    public void Capability_kind_is_not_inferred_from_identity_or_tool_name()
    {
        var result = Run(@"
[CrestCreates.Agent.Tools.AgentToolSpecs]
public static partial class KindUnknownTools
{
    [CrestCreates.Agent.Tools.AgentToolSpec(
        ""orders.get"",
        Description = ""Gets an order."",
        BudgetCategory = ""read"",
        AllowedAgentRoles = new[] { ""reader"" },
        SideEffectKind = CrestCreates.Metadata.AgentTool.AgentToolSideEffectKind.ReadOnly)]
    public sealed class Get { }
}");

        result.Diagnostics.Should().NotContain(diagnostic => diagnostic.Id == "ATP015",
            "CapabilityKind has no stable same-compilation authoring source in Phase 8f and must be validated at startup");
        result.GeneratedSources.Should().HaveCount(2);
    }

    [Fact]
    public void Unknown_side_effect_with_best_effort_audit_is_deferred_to_startup()
    {
        var result = Run(@"
[CrestCreates.Agent.Tools.AgentToolSpecs]
public static partial class QueryTools
{
    [CrestCreates.Agent.Tools.AgentToolSpec(
        ""orders.query"",
        Description = ""Reads orders."",
        BudgetCategory = ""read"",
        AllowedAgentRoles = new[] { ""reader"" },
        AuditMode = CrestCreates.Metadata.AgentTool.AgentToolAuditMode.BestEffort)]
    public sealed class Query { }
}");

        result.Diagnostics.Should().NotContain(diagnostic => diagnostic.Id == "ATP016");
        result.GeneratedSources.Should().HaveCount(2);
    }

    [Fact]
    public void Generated_source_has_no_forbidden_execution_or_reflection_fallback_dependencies()
    {
        var result = Run(@"
public sealed class InputDto { public string Value { get; set; } }
public sealed class OutputDto { public string Value { get; set; } }
[CrestCreates.Agent.Tools.AgentToolSpecs]
public static partial class GuardedTools
{
    [CrestCreates.Agent.Tools.AgentToolSpec(""guarded"", InputType = typeof(InputDto), OutputType = typeof(OutputDto), Description = ""Guarded."", BudgetCategory = ""b"", AllowedAgentRoles = new[] { ""r"" })]
    public sealed class Guarded { }
}");

        var generated = Generated(result);
        generated.Should().NotContain("CrestCreates.Mcp");
        generated.Should().NotContain("OpenAI");
        generated.Should().NotContain("Microsoft.Agents");
        generated.Should().NotContain("DynamicApi");
        generated.Should().NotContain("AspNetCore");
        generated.Should().NotContain("ControlPlane");
        generated.Should().NotContain("Handler");
        generated.Should().NotContain("Dictionary<string, object?>");
        generated.Should().NotContain("DefaultJsonTypeInfoResolver");
        generated.Should().NotContain("GetProperties(");
        generated.Should().NotContain("ICapabilityDispatcher");
        generated.Should().NotContain("DispatchAsync(");
    }

    [Fact]
    public void Same_container_name_in_different_namespaces_has_unique_hint_names()
    {
        var result = Run(@"
namespace Sales
{
    [CrestCreates.Agent.Tools.AgentToolSpecs]
    public static partial class Tools
    {
        [CrestCreates.Agent.Tools.AgentToolSpec(""sales"", Description = ""Sales."", BudgetCategory = ""b"", AllowedAgentRoles = new[] { ""r"" })] public sealed class Sales { }
    }
}
namespace Support
{
    [CrestCreates.Agent.Tools.AgentToolSpecs]
    public static partial class Tools
    {
        [CrestCreates.Agent.Tools.AgentToolSpec(""support"", Description = ""Support."", BudgetCategory = ""b"", AllowedAgentRoles = new[] { ""r"" })] public sealed class Support { }
    }
}");

        result.GeneratedSources.Select(source => source.FileName).Should().OnlyHaveUniqueItems();
        result.GeneratedSources.Select(source => source.FileName)
            .Should().Contain(file => file.EndsWith("Sales.Tools_AgentToolProvider.g.cs"))
            .And.Contain(file => file.EndsWith("Support.Tools_AgentToolProvider.g.cs"));
    }

    private static SourceGeneratorResult Run(string source)
        => SourceGeneratorTestHelper.RunGenerator<CodeGenerator.AgentToolGenerator.AgentToolGenerator>(
            source,
            additionalSources: [Stubs],
            additionalReferences: ["System.Text.Json"]);

    private static string Generated(SourceGeneratorResult result)
        => string.Join("\n", result.GeneratedSources.Select(source => source.SourceText));

    private const string Stubs = @"
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.Agent.Tools
{
    public enum AgentToolRiskFloor { Inherit = 0, Low = 1, Medium = 2, High = 3, Critical = 4 }
    [AttributeUsage(AttributeTargets.Class)] public sealed class AgentToolSpecsAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class AgentToolSpecAttribute : Attribute
    {
        public AgentToolSpecAttribute(string capabilityId) { }
        public string DescriptorId { get; set; }
        public int DescriptorVersion { get; set; } = 1;
        public int CapabilityVersion { get; set; }
        public string ExpectedCapabilityContractHash { get; set; }
        public Type InputType { get; set; }
        public Type OutputType { get; set; }
        public string ToolName { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public CrestCreates.Metadata.AgentTool.AgentToolSelectionPolicy SelectionPolicy { get; set; } = CrestCreates.Metadata.AgentTool.AgentToolSelectionPolicy.ExplicitOnly;
        public CrestCreates.Metadata.AgentTool.AgentToolSideEffectKind SideEffectKind { get; set; }
        public AgentToolRiskFloor RiskFloor { get; set; }
        public CrestCreates.Metadata.AgentTool.AgentToolApprovalMode ApprovalMode { get; set; } = CrestCreates.Metadata.AgentTool.AgentToolApprovalMode.PolicyDriven;
        public string BudgetCategory { get; set; }
        public long CostUnits { get; set; } = 1;
        public int MaxCallsPerExecution { get; set; }
        public CrestCreates.Metadata.AgentTool.AgentToolAuditMode AuditMode { get; set; } = CrestCreates.Metadata.AgentTool.AgentToolAuditMode.Required;
        public string[] AllowedAgentRoles { get; set; } = Array.Empty<string>();
    }
    public sealed class AgentToolBindingContract
    {
        public string ToolDescriptorId { get; init; }
        public int ToolDescriptorVersion { get; init; }
        public Type InputType { get; init; }
        public Type OutputType { get; init; }
        public Func<JsonElement, JsonTypeInfo, CancellationToken, ValueTask<object>> BindInputAsync { get; init; }
        public Func<object, JsonTypeInfo, CancellationToken, ValueTask<JsonElement?>> SerializeOutputAsync { get; init; }
    }
    public static class AgentToolBindingRegistry { public static void Register(AgentToolBindingContract contract) { } }
    public static class AgentToolJsonContractRegistry { public static void RegisterInputType(Type type) { } public static void RegisterOutputType(Type type) { } }
}
namespace CrestCreates.Metadata.AgentTool
{
    public enum AgentToolSelectionPolicy { Unknown = 0, ExplicitOnly = 1, AutomaticAllowed = 2 }
    public enum AgentToolSideEffectKind { Unknown = 0, ReadOnly = 1, InternalWrite = 2, ExternalWrite = 3, Destructive = 4 }
    public enum AgentToolApprovalMode { Unknown = 0, PolicyDriven = 1, Required = 2, None = 3 }
    public enum AgentToolAuditMode { Unknown = 0, Required = 1, BestEffort = 2 }
    public sealed class AgentToolBudgetRequirement { public string Category { get; init; } public long CostUnits { get; init; } public int? MaxCallsPerExecution { get; init; } }
    public sealed class AgentCapabilityToolDescriptor
    {
        public string Id { get; init; } public string Name { get; init; } public int Version { get; init; }
        public CrestCreates.Metadata.Abstractions.DescriptorCapability.CapabilityProjectionReference Capability { get; init; }
        public string ToolName { get; init; } public string Title { get; init; } public string Description { get; init; }
        public AgentToolSelectionPolicy SelectionPolicy { get; init; } public AgentToolSideEffectKind SideEffectKind { get; init; }
        public CrestCreates.Metadata.Abstractions.DescriptorCapability.CapabilityRiskLevel? RiskFloor { get; init; }
        public AgentToolApprovalMode ApprovalMode { get; init; } public AgentToolBudgetRequirement Budget { get; init; }
        public AgentToolAuditMode AuditMode { get; init; } public IReadOnlyList<string> AllowedAgentRoles { get; init; }
    }
}
namespace CrestCreates.Metadata.Abstractions
{
    public enum VersionSelectionMode { Exact = 0, Latest = 1, Compatible = 2 }
}
namespace CrestCreates.Metadata.Abstractions.DescriptorCapability
{
    public enum CapabilityRiskLevel { Low = 0, Medium = 1, High = 2, Critical = 3 }
    public readonly struct CapabilityProjectionReference
    {
        public CapabilityProjectionReference(string id, int version, CrestCreates.Metadata.Abstractions.VersionSelectionMode mode, string expectedContractHash) { }
    }
}
namespace CrestCreates.Metadata
{
    public interface IDescriptorProvider<T> { IReadOnlyList<T> GetDescriptors(); }
    public static class DescriptorProviderRegistry { public static void Register<T>(IDescriptorProvider<T> provider) { } }
}
";
}
