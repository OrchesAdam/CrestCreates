using Xunit;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using CrestCreates.Metadata.DescriptorCompatibility;
using FluentAssertions;
using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Metadata.Tests.DescriptorCompatibility;

public class DescriptorCompatibilityDiagnosticsTests
{
    private static readonly IDescriptorCompatibilityAnalyzer Analyzer = new DescriptorCompatibilityAnalyzer();

    private static DescriptorRef TestRef => new("test", "T1", 1);

    [Fact]
    public void ImpactTopologyError_AddsCompatibilityDiagnostic()
    {
        var change = new DescriptorChange { Ref = TestRef, Kind = DescriptorChangeKind.Added };
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var consumer = new DescriptorRef("consumer", "C1", 1);
        var impactReport = new DescriptorImpactAnalysisReport
        {
            ChangeSet = cs,
            AffectedDescriptors = Array.Empty<AffectedDescriptor>(),
            Paths = Array.Empty<DescriptorImpactPath>(),
            MaxSeverity = DescriptorImpactSeverity.Low,
            Diagnostics = new[]
            {
                new DescriptorImpactDiagnostic(
SeverityLevel.Error, new DiagnosticCode("IMPACT_TOPOLOGY_MISSING_TARGET"),
                    "Missing target", TestRef, new[] { consumer })
            }
        };

        var result = Analyzer.Analyze(Array.Empty<IDescriptor>(), Array.Empty<IDescriptor>(), cs, impactReport);

        result.Diagnostics.Should().Contain(d =>
            d.Code == "COMPAT_BLOCKED_BY_TOPOLOGY_ERROR" && d.Severity == SeverityLevel.Error);
        result.Findings.Should().Contain(f =>
            f.RuleId == "COMPAT_ANALYSIS_UNTRUSTED_IMPACT_REPORT" && f.Level == DescriptorCompatibilityLevel.Unsupported);
    }

    [Fact]
    public void ImpactPathTruncated_AddsAnalysisIncompleteDiagnostic()
    {
        var change = new DescriptorChange { Ref = TestRef, Kind = DescriptorChangeKind.Added };
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var impactReport = new DescriptorImpactAnalysisReport
        {
            ChangeSet = cs,
            AffectedDescriptors = Array.Empty<AffectedDescriptor>(),
            Paths = Array.Empty<DescriptorImpactPath>(),
            MaxSeverity = DescriptorImpactSeverity.Low,
            Diagnostics = new[]
            {
                new DescriptorImpactDiagnostic(
SeverityLevel.Warning, new DiagnosticCode("IMPACT_PATH_TRUNCATED"),
                    "Path truncated", TestRef, null)
            }
        };

        var result = Analyzer.Analyze(Array.Empty<IDescriptor>(), Array.Empty<IDescriptor>(), cs, impactReport);

        result.Diagnostics.Should().Contain(d =>
            d.Code == "COMPAT_ANALYSIS_INCOMPLETE" && d.Severity == SeverityLevel.Warning);
    }

    [Fact]
    public void ImpactAmbiguousUnpinned_AddsVersionAmbiguityDiagnostic()
    {
        var change = new DescriptorChange { Ref = TestRef, Kind = DescriptorChangeKind.Added };
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var impactReport = new DescriptorImpactAnalysisReport
        {
            ChangeSet = cs,
            AffectedDescriptors = Array.Empty<AffectedDescriptor>(),
            Paths = Array.Empty<DescriptorImpactPath>(),
            MaxSeverity = DescriptorImpactSeverity.Low,
            Diagnostics = new[]
            {
                new DescriptorImpactDiagnostic(
SeverityLevel.Warning, new DiagnosticCode("IMPACT_AMBIGUOUS_UNPINNED_TARGET"),
                    "Ambiguous unpinned target", TestRef, null)
            }
        };

        var result = Analyzer.Analyze(Array.Empty<IDescriptor>(), Array.Empty<IDescriptor>(), cs, impactReport);

        result.Diagnostics.Should().Contain(d =>
            d.Code == "COMPAT_VERSION_AMBIGUITY" && d.Severity == SeverityLevel.Warning);
    }

    [Fact]
    public void DuplicateDescriptorRefs_AddsDiagnostic()
    {
        // Create two descriptors with the same (Namespace, Id, Version)
        var d1 = new TestDescriptor("test", "D1", 1, "hash1");
        var d2 = new TestDescriptor("test", "D1", 1, "hash2");
        var refKey = new DescriptorRef("test", "D1", 1);
        var change = new DescriptorChange { Ref = refKey, Kind = DescriptorChangeKind.Added };
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var impactReport = new DescriptorImpactAnalysisReport
        {
            ChangeSet = cs,
            AffectedDescriptors = Array.Empty<AffectedDescriptor>(),
            Paths = Array.Empty<DescriptorImpactPath>(),
            MaxSeverity = DescriptorImpactSeverity.Low,
            Diagnostics = Array.Empty<DescriptorImpactDiagnostic>()
        };

        var result = Analyzer.Analyze(new IDescriptor[] { d1, d2 }, Array.Empty<IDescriptor>(), cs, impactReport);

        result.Diagnostics.Should().Contain(d =>
            d.Code == "COMPAT_DUPLICATE_DESCRIPTOR_REF" && d.Severity == SeverityLevel.Warning);
    }

    [Fact]
    public void ChangeSetMismatch_AddsDiagnostic()
    {
        var change = new DescriptorChange { Ref = TestRef, Kind = DescriptorChangeKind.Added };
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        // Use a genuinely different changeSet (different kind) to trigger mismatch
        var differentChange = new DescriptorChange { Ref = TestRef, Kind = DescriptorChangeKind.Removed };
        var differentCs = new DescriptorChangeSet { Changes = new[] { differentChange } };
        var impactReport = new DescriptorImpactAnalysisReport
        {
            ChangeSet = differentCs,
            AffectedDescriptors = Array.Empty<AffectedDescriptor>(),
            Paths = Array.Empty<DescriptorImpactPath>(),
            MaxSeverity = DescriptorImpactSeverity.Low,
            Diagnostics = Array.Empty<DescriptorImpactDiagnostic>()
        };

        var result = Analyzer.Analyze(Array.Empty<IDescriptor>(), Array.Empty<IDescriptor>(), cs, impactReport);

        result.Diagnostics.Should().Contain(d =>
            d.Code == "COMPAT_CHANGESET_MISMATCH" && d.Severity == SeverityLevel.Error);
    }

    // Minimal test descriptor
    private sealed class TestDescriptor : IDescriptor, IVersionedDescriptor
    {
        public string Namespace { get; }
        public string Id { get; }
        public int Version { get; }
        public string Name => Id;
        public DescriptorKind Kind => DescriptorKind.Schema;
        public DescriptorState State => DescriptorState.Active;
        public string ContractHash { get; }
        public string DefinitionHash => "";
        public string? SupersededById => null;

        public TestDescriptor(string ns, string id, int version, string contractHash)
        {
            Namespace = ns;
            Id = id;
            Version = version;
            ContractHash = contractHash;
        }
    }
}
