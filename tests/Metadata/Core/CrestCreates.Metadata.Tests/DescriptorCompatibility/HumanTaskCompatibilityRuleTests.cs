using Xunit;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.DescriptorCompatibility;
using CrestCreates.HumanTask.Abstractions;
using FluentAssertions;

namespace CrestCreates.Metadata.Tests.DescriptorCompatibility;

public class HumanTaskCompatibilityRuleTests
{
    private static readonly IDescriptorCompatibilityAnalyzer Analyzer = new DescriptorCompatibilityAnalyzer();

    private static HumanTaskDescriptor MakeHumanTask(
        string id = "H1", int version = 1, IReadOnlyList<CompletionOutcome>? outcomes = null,
        string? permission = null, string interactionId = "F1",
        AssigneeStrategy assigneeStrategy = AssigneeStrategy.SingleUser)
    {
        return new HumanTaskDescriptor
        {
            Id = id, Name = "TestTask", Version = version,
            State = DescriptorState.Active,
            Interaction = new VersionedDescriptorRef<IInteractionDescriptor> { Id = interactionId, Version = 1 },
            AssigneeStrategy = assigneeStrategy, Permissions = permission,
            Outcomes = outcomes ?? Array.Empty<CompletionOutcome>()
        };
    }

    private static CompletionOutcome MakeOutcome(CompletionCondition condition, string? capabilityId = null) => new()
    {
        Condition = condition,
        Capability = capabilityId != null ? new VersionedDescriptorRef<IVersionedDescriptor> { Id = capabilityId, Version = 1 } : null
    };

    private static DescriptorImpactAnalysisReport MakeImpactReport(DescriptorChangeSet changeSet, DescriptorRef changedRef, params DescriptorRef[] affectedRefs)
    {
        var paths = affectedRefs.Select(r => new DescriptorImpactPath { SourceChange = changedRef, Affected = r, Segments = Array.Empty<DescriptorImpactPathSegment>() }).ToArray();
        return new DescriptorImpactAnalysisReport { ChangeSet = changeSet, AffectedDescriptors = affectedRefs.Select(r => new AffectedDescriptor { Ref = r, Kind = DescriptorKind.HumanTask, Name = r.FullId, Severity = DescriptorImpactSeverity.Low, RuntimeAreas = new[] { DescriptorImpactRuntimeArea.HumanTask }, Paths = paths.Where(p => p.Affected == r).ToArray() }).ToArray(), Paths = paths, MaxSeverity = DescriptorImpactSeverity.Low, Diagnostics = Array.Empty<DescriptorImpactDiagnostic>() };
    }

    private static DescriptorChange MakeChange(string id, int version) => new()
    {
        Ref = new DescriptorRef("humantask", id, version), Kind = DescriptorChangeKind.ContractHashChanged, BeforeContractHash = "h1", AfterContractHash = "h2"
    };

    [Fact] public void HumanTask_InteractionChanged_Breaking()
    {
        var before = MakeHumanTask(interactionId: "F1");
        var after = MakeHumanTask(interactionId: "F2");
        var change = MakeChange("H1", 1);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, MakeImpactReport(cs, change.Ref));
        result.Findings.Should().Contain(f => f.RuleId == "COMPAT_HUMANTASK_INTERACTION_CHANGED" && f.Level == DescriptorCompatibilityLevel.Breaking);
    }

    [Fact] public void HumanTask_AssigneeStrategyChanged_Risky()
    {
        var before = MakeHumanTask(assigneeStrategy: AssigneeStrategy.SingleUser);
        var after = MakeHumanTask(assigneeStrategy: AssigneeStrategy.RoundRobin);
        var change = MakeChange("H1", 1);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, MakeImpactReport(cs, change.Ref));
        result.Findings.Should().Contain(f => f.RuleId == "COMPAT_HUMANTASK_ASSIGNEE_STRATEGY_CHANGED" && f.Level == DescriptorCompatibilityLevel.Risky);
    }

    [Fact] public void HumanTask_OutcomeRemoved_Breaking()
    {
        var before = MakeHumanTask(outcomes: new[] { MakeOutcome(CompletionCondition.Approve), MakeOutcome(CompletionCondition.Reject) });
        var after = MakeHumanTask(outcomes: new[] { MakeOutcome(CompletionCondition.Approve) });
        var change = MakeChange("H1", 1);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, MakeImpactReport(cs, change.Ref));
        result.Findings.Should().Contain(f => f.RuleId == "COMPAT_HUMANTASK_OUTCOME_REMOVED" && f.Level == DescriptorCompatibilityLevel.Breaking);
    }

    [Fact] public void HumanTask_OutcomeCapabilityChanged_Breaking()
    {
        var before = MakeHumanTask(outcomes: new[] { MakeOutcome(CompletionCondition.Approve, "C1") });
        var after = MakeHumanTask(outcomes: new[] { MakeOutcome(CompletionCondition.Approve, "C2") });
        var change = MakeChange("H1", 1);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, MakeImpactReport(cs, change.Ref));
        result.Findings.Should().Contain(f => f.RuleId == "COMPAT_HUMANTASK_OUTCOME_CAPABILITY_CHANGED" && f.Level == DescriptorCompatibilityLevel.Breaking);
        result.HasBreakingChanges.Should().BeTrue();
    }

    [Fact] public void HumanTask_PermissionChanged_SecuritySensitive()
    {
        var before = MakeHumanTask(permission: null);
        var after = MakeHumanTask(permission: "admin");
        var change = MakeChange("H1", 1);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, MakeImpactReport(cs, change.Ref));
        result.Findings.Should().Contain(f => f.RuleId == "COMPAT_HUMANTASK_PERMISSION_CHANGED" && f.Level == DescriptorCompatibilityLevel.SecuritySensitive);
    }
}
