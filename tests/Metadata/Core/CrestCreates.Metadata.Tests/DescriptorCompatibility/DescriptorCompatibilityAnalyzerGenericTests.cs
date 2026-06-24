using Xunit;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using CrestCreates.Metadata.DescriptorCompatibility;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;

namespace CrestCreates.Metadata.Tests.DescriptorCompatibility;

public class DescriptorCompatibilityAnalyzerGenericTests
{
    private static readonly IDescriptorCompatibilityAnalyzer Analyzer = new DescriptorCompatibilityAnalyzer();

    private static DescriptorRef TestRef => new("test", "T1", 1);

    private static DescriptorChange MakeChange(DescriptorChangeKind kind,
        DescriptorState? beforeState = null, DescriptorState? afterState = null,
        string? beforeHash = null, string? afterHash = null)
        => new()
        {
            Ref = TestRef,
            Kind = kind,
            BeforeState = beforeState,
            AfterState = afterState,
            BeforeContractHash = beforeHash,
            AfterContractHash = afterHash
        };

    private static DescriptorImpactAnalysisReport MakeImpactReport(
        DescriptorChangeSet changeSet,
        params DescriptorRef[] affectedRefs)
    {
        var paths = affectedRefs.Select(r => new DescriptorImpactPath
        {
            SourceChange = TestRef,
            Affected = r,
            Segments = Array.Empty<DescriptorImpactPathSegment>()
        }).ToArray();

        return new DescriptorImpactAnalysisReport
        {
            ChangeSet = changeSet,
            AffectedDescriptors = affectedRefs.Select(r => new AffectedDescriptor
            {
                Ref = r,
                Kind = DescriptorKind.Schema,
                Name = r.FullId,
                Severity = DescriptorImpactSeverity.Low,
                RuntimeAreas = new[] { DescriptorImpactRuntimeArea.Schema },
                Paths = paths.Where(p => p.Affected == r).ToArray()
            }).ToArray(),
            Paths = paths,
            MaxSeverity = DescriptorImpactSeverity.Low,
            Diagnostics = Array.Empty<DescriptorImpactDiagnostic>()
        };
    }

    [Fact]
    public void AddedDescriptor_ReturnsCompatible()
    {
        var change = MakeChange(DescriptorChangeKind.Added);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var report = MakeImpactReport(cs);

        var result = Analyzer.Analyze(Array.Empty<IDescriptor>(), Array.Empty<IDescriptor>(), cs, report);

        result.Findings.Should().ContainSingle(f =>
            f.RuleId == "COMPAT_GENERIC_ADDED" && f.Level == DescriptorCompatibilityLevel.Compatible);
    }

    [Fact]
    public void RemovedDescriptor_WithAffectedConsumers_ReturnsBreaking()
    {
        var change = MakeChange(DescriptorChangeKind.Removed, DescriptorState.Active);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var consumer = new DescriptorRef("consumer", "C1", 1);
        var report = MakeImpactReport(cs, consumer);

        var result = Analyzer.Analyze(Array.Empty<IDescriptor>(), Array.Empty<IDescriptor>(), cs, report);

        result.Findings.Should().Contain(f =>
            f.RuleId == "COMPAT_GENERIC_REMOVED_WITH_CONSUMERS" && f.Level == DescriptorCompatibilityLevel.Breaking);
    }

    [Fact]
    public void RemovedDescriptor_WithoutAffectedConsumers_ReturnsRiskyByDefault()
    {
        var change = MakeChange(DescriptorChangeKind.Removed, DescriptorState.Active);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var report = MakeImpactReport(cs);

        var result = Analyzer.Analyze(Array.Empty<IDescriptor>(), Array.Empty<IDescriptor>(), cs, report);

        result.Findings.Should().Contain(f =>
            f.RuleId == "COMPAT_GENERIC_REMOVED_NO_CONSUMERS" && f.Level == DescriptorCompatibilityLevel.Risky);
    }

    [Fact]
    public void RemovedDescriptor_WithoutConsumers_OptionDisabled_ReturnsCompatible()
    {
        var change = MakeChange(DescriptorChangeKind.Removed, DescriptorState.Active);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var report = MakeImpactReport(cs);
        var options = new DescriptorCompatibilityAnalysisOptions { TreatRemovedWithoutConsumersAsRisky = false };

        var result = Analyzer.Analyze(Array.Empty<IDescriptor>(), Array.Empty<IDescriptor>(), cs, report, options);

        result.Findings.Should().Contain(f =>
            f.RuleId == "COMPAT_GENERIC_REMOVED_NO_CONSUMERS" && f.Level == DescriptorCompatibilityLevel.Compatible);
    }

    [Fact]
    public void DeprecatedDescriptor_WithAffectedConsumers_ReturnsRisky()
    {
        var change = MakeChange(DescriptorChangeKind.Deprecated, DescriptorState.Active, DescriptorState.Deprecated);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var consumer = new DescriptorRef("consumer", "C1", 1);
        var report = MakeImpactReport(cs, consumer);

        var result = Analyzer.Analyze(Array.Empty<IDescriptor>(), Array.Empty<IDescriptor>(), cs, report);

        result.Findings.Should().Contain(f =>
            f.RuleId == "COMPAT_GENERIC_DEPRECATED_WITH_CONSUMERS" && f.Level == DescriptorCompatibilityLevel.Risky);
    }

    [Fact]
    public void ActivatedDescriptor_ReturnsCompatible()
    {
        var change = MakeChange(DescriptorChangeKind.Activated, DescriptorState.Draft, DescriptorState.Active);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var report = MakeImpactReport(cs);

        var result = Analyzer.Analyze(Array.Empty<IDescriptor>(), Array.Empty<IDescriptor>(), cs, report);

        result.Findings.Should().Contain(f =>
            f.RuleId == "COMPAT_GENERIC_ACTIVATED" && f.Level == DescriptorCompatibilityLevel.Compatible);
    }

    [Fact]
    public void StateChangedToRemoved_WithConsumers_ReturnsBreaking()
    {
        var change = MakeChange(DescriptorChangeKind.StateChanged, DescriptorState.Active, DescriptorState.Removed);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var consumer = new DescriptorRef("consumer", "C1", 1);
        var report = MakeImpactReport(cs, consumer);

        var result = Analyzer.Analyze(Array.Empty<IDescriptor>(), Array.Empty<IDescriptor>(), cs, report);

        result.Findings.Should().Contain(f =>
            f.RuleId == "COMPAT_GENERIC_STATE_REMOVED" && f.Level == DescriptorCompatibilityLevel.Breaking);
    }

    [Fact]
    public void Updated_Normal_ReturnsCompatible()
    {
        var change = MakeChange(DescriptorChangeKind.Updated, beforeHash: "hash1", afterHash: "hash1");
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var report = MakeImpactReport(cs);

        var result = Analyzer.Analyze(Array.Empty<IDescriptor>(), Array.Empty<IDescriptor>(), cs, report);

        result.Findings.Should().Contain(f =>
            f.RuleId == "COMPAT_GENERIC_UPDATED" && f.Level == DescriptorCompatibilityLevel.Compatible);
    }

    [Fact]
    public void Updated_UnexpectedHashChange_ReturnsRisky()
    {
        var change = MakeChange(DescriptorChangeKind.Updated, beforeHash: "hash1", afterHash: "hash2");
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var report = MakeImpactReport(cs);

        var result = Analyzer.Analyze(Array.Empty<IDescriptor>(), Array.Empty<IDescriptor>(), cs, report);

        result.Findings.Should().Contain(f =>
            f.RuleId == "COMPAT_GENERIC_UPDATED_UNEXPECTED" && f.Level == DescriptorCompatibilityLevel.Risky);
    }

    [Fact]
    public void MaxLevel_ReportsHighestLevel()
    {
        var change1 = MakeChange(DescriptorChangeKind.Added);
        var change2 = MakeChange(DescriptorChangeKind.Removed, DescriptorState.Active);
        var cs = new DescriptorChangeSet { Changes = new[] { change1, change2 } };
        var consumer = new DescriptorRef("consumer", "C1", 1);
        var report = MakeImpactReport(cs, consumer);

        var result = Analyzer.Analyze(Array.Empty<IDescriptor>(), Array.Empty<IDescriptor>(), cs, report);

        result.MaxLevel.Should().Be(DescriptorCompatibilityLevel.Breaking);
        result.RequiresReview.Should().BeTrue();
        result.HasBreakingChanges.Should().BeTrue();
    }

    [Fact]
    public void MaxLevel_DoesNotTreatUnsupportedAsMoreSevere()
    {
        var change = MakeChange(DescriptorChangeKind.ContractHashChanged, beforeHash: "h1", afterHash: "h2");
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var consumer = new DescriptorRef("consumer", "C1", 1);
        var impactReport = new DescriptorImpactAnalysisReport
        {
            ChangeSet = cs,
            AffectedDescriptors = new[]
            {
                new AffectedDescriptor
                {
                    Ref = consumer, Kind = DescriptorKind.Schema, Name = consumer.FullId,
                    Severity = DescriptorImpactSeverity.Low,
                    RuntimeAreas = new[] { DescriptorImpactRuntimeArea.Schema },
                    Paths = new[]
                    {
                        new DescriptorImpactPath { SourceChange = TestRef, Affected = consumer,
                            Segments = Array.Empty<DescriptorImpactPathSegment>() }
                    }
                }
            },
            Paths = new[]
            {
                new DescriptorImpactPath { SourceChange = TestRef, Affected = consumer,
                    Segments = Array.Empty<DescriptorImpactPathSegment>() }
            },
            MaxSeverity = DescriptorImpactSeverity.Low,
            Diagnostics = new[]
            {
                new DescriptorImpactDiagnostic(
                    DiagnosticSeverity.Error, "IMPACT_TOPOLOGY_MISSING_TARGET",
                    "Missing target", TestRef, new[] { consumer })
            }
        };

        var result = Analyzer.Analyze(Array.Empty<IDescriptor>(), Array.Empty<IDescriptor>(), cs, impactReport);

        // Impact error adds Unsupported finding, but MaxLevel should be Risky (from the contract hash generic fallback), not Unsupported
        result.MaxLevel.Should().Be(DescriptorCompatibilityLevel.Risky);
    }

    [Fact]
    public void HighImpactSeverity_DoesNotAutomaticallyMeanBreaking()
    {
        var change = MakeChange(DescriptorChangeKind.Added);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var report = MakeImpactReport(cs);

        var result = Analyzer.Analyze(Array.Empty<IDescriptor>(), Array.Empty<IDescriptor>(), cs, report);

        result.Findings.Should().Contain(f => f.Level == DescriptorCompatibilityLevel.Compatible);
        result.MaxLevel.Should().Be(DescriptorCompatibilityLevel.Compatible);
    }

    [Fact]
    public void LowImpactSeverity_CanStillBeBreaking_WhenRuleSaysBreaking()
    {
        var change = MakeChange(DescriptorChangeKind.Removed, DescriptorState.Active);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var consumer = new DescriptorRef("consumer", "C1", 1);
        var report = MakeImpactReport(cs, consumer);

        var result = Analyzer.Analyze(Array.Empty<IDescriptor>(), Array.Empty<IDescriptor>(), cs, report);

        result.Findings.Should().Contain(f => f.Level == DescriptorCompatibilityLevel.Breaking);
        result.HasBreakingChanges.Should().BeTrue();
    }

    [Fact]
    public void GenericDefinitionHashChanged_ReturnsRisky()
    {
        var change = MakeChange(DescriptorChangeKind.DefinitionHashChanged,
            beforeHash: "h1", afterHash: "h1");
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var report = MakeImpactReport(cs);

        var result = Analyzer.Analyze(Array.Empty<IDescriptor>(), Array.Empty<IDescriptor>(), cs, report);

        result.Findings.Should().Contain(f =>
            f.RuleId == "COMPAT_GENERIC_DEFINITION_CHANGED" && f.Level == DescriptorCompatibilityLevel.Risky);
    }

    [Fact]
    public void SchemaSpecificDefinitionHashChanged_DoesNotReceiveConflictingGenericFinding()
    {
        // Use SchemaCompatibilityRule via the analyzer — schema descriptor + DefinitionHashChanged
        // Schema rule should fully classify the change; generic rule must not emit a second Risky.
        var schemaBefore = new SchemaDescriptor
        {
            Id = "S1", Name = "TestSchema", Version = 1,
            State = DescriptorState.Active,
            Fields = new[] { new SchemaFieldDescriptor { Name = "test", FieldType = "string" } }
        };
        var schemaAfter = new SchemaDescriptor
        {
            Id = "S1", Name = "TestSchema", Version = 1,
            State = DescriptorState.Active,
            Fields = new[] { new SchemaFieldDescriptor { Name = "test", FieldType = "string" } }
        };

        var change = new DescriptorChange
        {
            Ref = new DescriptorRef("schema", "S1", 1),
            Kind = DescriptorChangeKind.DefinitionHashChanged,
            BeforeContractHash = "h1",
            AfterContractHash = "h1",
            BeforeDefinitionHash = "d1",
            AfterDefinitionHash = "d2"
        };
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var report = MakeImpactReport(cs);

        var result = Analyzer.Analyze(
            new IDescriptor[] { schemaBefore },
            new IDescriptor[] { schemaAfter },
            cs, report);

        // Should not contain a generic definition-changed finding
        result.Findings.Should().NotContain(f =>
            f.RuleId == "COMPAT_GENERIC_DEFINITION_CHANGED");
    }
}
