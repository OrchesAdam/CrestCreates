using Xunit;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.DescriptorCompatibility;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;

namespace CrestCreates.Metadata.Tests.DescriptorCompatibility;

public class CapabilityCompatibilityRuleTests
{
    private static readonly IDescriptorCompatibilityAnalyzer Analyzer = new DescriptorCompatibilityAnalyzer();

    private static CapabilityDescriptor MakeCapability(
        string id = "C1", int version = 1, IReadOnlyList<string>? permissions = null,
        CapabilityRiskLevel riskLevel = CapabilityRiskLevel.Medium,
        string? inputSchemaId = null, string? outputSchemaId = null)
    {
        return new CapabilityDescriptor
        {
            Id = id, Name = "TestCapability", Version = version,
            State = DescriptorState.Active, ContractHash = "hash", DefinitionHash = "defhash",
            InputSchema = inputSchemaId != null ? new VersionedDescriptorRef<SchemaDescriptor> { Id = inputSchemaId, Version = 1 } : null,
            OutputSchema = outputSchemaId != null ? new VersionedDescriptorRef<SchemaDescriptor> { Id = outputSchemaId, Version = 1 } : null,
            Permissions = permissions ?? Array.Empty<string>(), RiskLevel = riskLevel,
            CapabilityKind = CapabilityKind.Query, SemanticTags = Array.Empty<string>(),
            Categories = Array.Empty<string>(), Produces = Array.Empty<EventRef>(), Consumes = Array.Empty<EventRef>()
        };
    }

    private static DescriptorImpactAnalysisReport MakeImpactReport(DescriptorChangeSet changeSet, DescriptorRef changedRef, params DescriptorRef[] affectedRefs)
    {
        var paths = affectedRefs.Select(r => new DescriptorImpactPath { SourceChange = changedRef, Affected = r, Segments = Array.Empty<DescriptorImpactPathSegment>() }).ToArray();
        return new DescriptorImpactAnalysisReport { ChangeSet = changeSet, AffectedDescriptors = affectedRefs.Select(r => new AffectedDescriptor { Ref = r, Kind = DescriptorKind.Capability, Name = r.FullId, Severity = DescriptorImpactSeverity.Low, RuntimeAreas = new[] { DescriptorImpactRuntimeArea.Capability }, Paths = paths.Where(p => p.Affected == r).ToArray() }).ToArray(), Paths = paths, MaxSeverity = DescriptorImpactSeverity.Low, Diagnostics = Array.Empty<DescriptorImpactDiagnostic>() };
    }

    private static DescriptorChange MakeChange(string id, int version) => new()
    {
        Ref = new DescriptorRef("capability", id, version), Kind = DescriptorChangeKind.ContractHashChanged, BeforeContractHash = "h1", AfterContractHash = "h2"
    };

    [Fact] public void Capability_InputSchemaChanged_Breaking()
    {
        var before = MakeCapability();
        var after = MakeCapability(inputSchemaId: "S1");
        var change = MakeChange("C1", 1);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, MakeImpactReport(cs, change.Ref));
        result.Findings.Should().Contain(f => f.RuleId == "COMPAT_CAPABILITY_INPUT_SCHEMA_CHANGED" && f.Level == DescriptorCompatibilityLevel.Breaking);
    }

    [Fact] public void Capability_OutputSchemaChanged_Breaking()
    {
        var before = MakeCapability();
        var after = MakeCapability(outputSchemaId: "S1");
        var change = MakeChange("C1", 1);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, MakeImpactReport(cs, change.Ref));
        result.Findings.Should().Contain(f => f.RuleId == "COMPAT_CAPABILITY_OUTPUT_SCHEMA_ADDED" && f.Level == DescriptorCompatibilityLevel.Risky);
    }

    [Fact] public void Capability_PermissionAdded_SecuritySensitive()
    {
        var before = MakeCapability(permissions: new[] { "perm1" });
        var after = MakeCapability(permissions: new[] { "perm1", "perm2" });
        var change = MakeChange("C1", 1);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, MakeImpactReport(cs, change.Ref));
        result.Findings.Should().Contain(f => f.RuleId == "COMPAT_CAPABILITY_PERMISSION_ADDED" && f.Level == DescriptorCompatibilityLevel.SecuritySensitive);
    }

    [Fact] public void Capability_PermissionRemoved_SecuritySensitive()
    {
        var before = MakeCapability(permissions: new[] { "perm1", "perm2" });
        var after = MakeCapability(permissions: new[] { "perm1" });
        var change = MakeChange("C1", 1);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, MakeImpactReport(cs, change.Ref));
        result.Findings.Should().Contain(f => f.RuleId == "COMPAT_CAPABILITY_PERMISSION_REMOVED" && f.Level == DescriptorCompatibilityLevel.SecuritySensitive);
    }

    [Fact] public void Capability_RiskLevelChanged_SecuritySensitive()
    {
        var before = MakeCapability(riskLevel: CapabilityRiskLevel.Low);
        var after = MakeCapability(riskLevel: CapabilityRiskLevel.High);
        var change = MakeChange("C1", 1);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, MakeImpactReport(cs, change.Ref));
        result.Findings.Should().Contain(f => f.RuleId == "COMPAT_CAPABILITY_RISK_INCREASED" && f.Level == DescriptorCompatibilityLevel.SecuritySensitive);
    }
}
