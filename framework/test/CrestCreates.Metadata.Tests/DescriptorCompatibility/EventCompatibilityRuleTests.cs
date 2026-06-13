using Xunit;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.DescriptorCompatibility;
using CrestCreates.Event.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;

namespace CrestCreates.Metadata.Tests.DescriptorCompatibility;

public class EventCompatibilityRuleTests
{
    private static readonly IDescriptorCompatibilityAnalyzer Analyzer = new DescriptorCompatibilityAnalyzer();

    private static EventDescriptor MakeEvent(string id = "E1", int version = 1,
        string schemaId = "S1", int schemaVersion = 1, SchemaChangeKind changeKind = SchemaChangeKind.Additive)
    {
        return new EventDescriptor
        {
            Id = id, Name = "TestEvent", Version = version,
            State = DescriptorState.Active, ContractHash = "hash", DefinitionHash = "defhash",
            PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor> { Id = schemaId, Version = schemaVersion },
            Category = EventCategory.Domain, Semantic = EventSemantic.Fact,
            Importance = EventImportance.Business, ChangeKind = changeKind
        };
    }

    private static GeneratedEventDescriptor MakeGeneratedEvent(string id = "GE1", int version = 1,
        string schemaId = "S1", int schemaVersion = 1, EventReliability reliability = EventReliability.BestEffort)
    {
        return new GeneratedEventDescriptor
        {
            Id = id, Name = "TestGenEvent", Version = version,
            State = DescriptorState.Active,
            PayloadSchemaRef = new VersionedDescriptorRef<SchemaDescriptor> { Id = schemaId, Version = schemaVersion },
            Scope = EventScope.Local, Reliability = reliability,
            Importance = EventImportance.Business, ChangeKind = SchemaChangeKind.Additive,
            PayloadType = typeof(string)
        };
    }

    private static DescriptorImpactAnalysisReport EmptyReport(DescriptorChangeSet changeSet) => new()
    {
        ChangeSet = changeSet, AffectedDescriptors = Array.Empty<AffectedDescriptor>(),
        Paths = Array.Empty<DescriptorImpactPath>(), MaxSeverity = DescriptorImpactSeverity.Low,
        Diagnostics = Array.Empty<DescriptorImpactDiagnostic>()
    };

    [Fact] public void Event_PayloadSchemaChanged_Breaking()
    {
        var before = MakeEvent(schemaId: "S1", schemaVersion: 1);
        var after = MakeEvent(schemaId: "S2", schemaVersion: 2);
        var change = new DescriptorChange { Ref = new DescriptorRef("event", "E1", 1), Kind = DescriptorChangeKind.ContractHashChanged, BeforeContractHash = "h1", AfterContractHash = "h2" };
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, EmptyReport(cs));
        result.Findings.Should().Contain(f => f.RuleId == "COMPAT_EVENT_PAYLOAD_SCHEMA_CHANGED" && f.Level == DescriptorCompatibilityLevel.Breaking);
    }

    [Fact] public void Event_DeclaredBreaking_UpgradesToBreaking()
    {
        var before = MakeEvent();
        var after = MakeEvent(changeKind: SchemaChangeKind.Breaking);
        var change = new DescriptorChange { Ref = new DescriptorRef("event", "E1", 1), Kind = DescriptorChangeKind.ContractHashChanged, BeforeContractHash = "h1", AfterContractHash = "h2" };
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, EmptyReport(cs));
        result.Findings.Should().Contain(f => f.RuleId == "COMPAT_EVENT_DECLARED_BREAKING" && f.Level == DescriptorCompatibilityLevel.Breaking);
    }

    [Fact] public void GeneratedEvent_PayloadSchemaRefChanged_Breaking()
    {
        var before = MakeGeneratedEvent(schemaId: "S1");
        var after = MakeGeneratedEvent(schemaId: "S2", schemaVersion: 2);
        var change = new DescriptorChange { Ref = new DescriptorRef("event", "GE1", 1), Kind = DescriptorChangeKind.ContractHashChanged, BeforeContractHash = "h1", AfterContractHash = "h2" };
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, EmptyReport(cs));
        result.Findings.Should().Contain(f => f.RuleId == "COMPAT_EVENT_PAYLOAD_SCHEMA_CHANGED" && f.Level == DescriptorCompatibilityLevel.Breaking);
    }

    [Fact] public void GeneratedEvent_ReliabilityChanged_Risky()
    {
        var before = MakeGeneratedEvent(reliability: EventReliability.BestEffort);
        var after = MakeGeneratedEvent(reliability: EventReliability.AtLeastOnce);
        var change = new DescriptorChange { Ref = new DescriptorRef("event", "GE1", 1), Kind = DescriptorChangeKind.ContractHashChanged, BeforeContractHash = "h1", AfterContractHash = "h2" };
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, EmptyReport(cs));
        result.Findings.Should().Contain(f => f.RuleId == "COMPAT_EVENT_RELIABILITY_CHANGED" && f.Level == DescriptorCompatibilityLevel.Risky);
    }
}
