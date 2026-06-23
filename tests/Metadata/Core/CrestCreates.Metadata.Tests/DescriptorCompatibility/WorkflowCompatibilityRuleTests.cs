using Xunit;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.DescriptorCompatibility;
using CrestCreates.Workflow.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;

namespace CrestCreates.Metadata.Tests.DescriptorCompatibility;

public class WorkflowCompatibilityRuleTests
{
    private static readonly IDescriptorCompatibilityAnalyzer Analyzer = new DescriptorCompatibilityAnalyzer();

    private static WorkflowDescriptor MakeWorkflow(string id = "W1", int version = 1,
        IReadOnlyList<WorkflowStep>? steps = null, string? variableSchemaId = null)
    {
        return new WorkflowDescriptor
        {
            Id = id, Name = "TestWorkflow", Version = version,
            State = DescriptorState.Active,
            VariableSchema = variableSchemaId != null ? new VersionedDescriptorRef<SchemaDescriptor> { Id = variableSchemaId, Version = 1 } : null,
            Steps = steps ?? Array.Empty<WorkflowStep>(),
            DefaultVariableScope = WorkflowVariableScope.Workflow
        };
    }

    private static WorkflowStep MakeStep(string stepId, params string[] transitions) => new()
    {
        Id = stepId, Name = stepId,
        Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor> { Id = "C1", Version = 1 } },
        Transitions = transitions, OnError = StepErrorBehavior.Fail
    };

    private static WorkflowStep MakeStepWithTarget(string stepId, string targetId) => new()
    {
        Id = stepId, Name = stepId,
        Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor> { Id = targetId, Version = 1 } },
        Transitions = Array.Empty<string>(), OnError = StepErrorBehavior.Fail
    };

    private static DescriptorImpactAnalysisReport EmptyReport(DescriptorChangeSet changeSet) => new()
    {
        ChangeSet = changeSet, AffectedDescriptors = Array.Empty<AffectedDescriptor>(),
        Paths = Array.Empty<DescriptorImpactPath>(), MaxSeverity = DescriptorImpactSeverity.Low,
        Diagnostics = Array.Empty<DescriptorImpactDiagnostic>()
    };

    [Fact] public void Workflow_VariableSchemaChanged_Breaking()
    {
        var before = MakeWorkflow();
        var after = MakeWorkflow(variableSchemaId: "S1");
        var change = new DescriptorChange { Ref = new DescriptorRef("workflow", "W1", 1), Kind = DescriptorChangeKind.ContractHashChanged, BeforeContractHash = "h1", AfterContractHash = "h2" };
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, EmptyReport(cs));
        result.Findings.Should().Contain(f => f.RuleId == "COMPAT_WORKFLOW_VARIABLE_SCHEMA_CHANGED" && f.Level == DescriptorCompatibilityLevel.Breaking);
    }

    [Fact] public void Workflow_StepRemoved_Breaking()
    {
        var before = MakeWorkflow(steps: new[] { MakeStep("step1"), MakeStep("step2") });
        var after = MakeWorkflow(steps: new[] { MakeStep("step1") });
        var change = new DescriptorChange { Ref = new DescriptorRef("workflow", "W1", 1), Kind = DescriptorChangeKind.ContractHashChanged, BeforeContractHash = "h1", AfterContractHash = "h2" };
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, EmptyReport(cs));
        result.Findings.Should().Contain(f => f.RuleId == "COMPAT_WORKFLOW_STEP_REMOVED" && f.Level == DescriptorCompatibilityLevel.Breaking);
    }

    [Fact] public void Workflow_StepAdded_Risky()
    {
        var before = MakeWorkflow(steps: new[] { MakeStep("step1") });
        var after = MakeWorkflow(steps: new[] { MakeStep("step1"), MakeStep("step2") });
        var change = new DescriptorChange { Ref = new DescriptorRef("workflow", "W1", 1), Kind = DescriptorChangeKind.ContractHashChanged, BeforeContractHash = "h1", AfterContractHash = "h2" };
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, EmptyReport(cs));
        result.Findings.Should().Contain(f => f.RuleId == "COMPAT_WORKFLOW_STEP_ADDED" && f.Level == DescriptorCompatibilityLevel.Risky);
    }

    [Fact] public void Workflow_StepTargetChanged_Breaking()
    {
        var before = MakeWorkflow(steps: new[] { MakeStepWithTarget("step1", "C1") });
        var after = MakeWorkflow(steps: new[] { MakeStepWithTarget("step1", "C2") });
        var change = new DescriptorChange { Ref = new DescriptorRef("workflow", "W1", 1), Kind = DescriptorChangeKind.ContractHashChanged, BeforeContractHash = "h1", AfterContractHash = "h2" };
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, EmptyReport(cs));
        result.Findings.Should().Contain(f => f.RuleId == "COMPAT_WORKFLOW_STEP_TARGET_CHANGED" && f.Level == DescriptorCompatibilityLevel.Breaking);
    }

    [Fact] public void Workflow_TransitionsChanged_Breaking()
    {
        var before = MakeWorkflow(steps: new[] { MakeStep("step1", "step2") });
        var after = MakeWorkflow(steps: new[] { MakeStep("step1", "step3") });
        var change = new DescriptorChange { Ref = new DescriptorRef("workflow", "W1", 1), Kind = DescriptorChangeKind.ContractHashChanged, BeforeContractHash = "h1", AfterContractHash = "h2" };
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, EmptyReport(cs));
        result.Findings.Should().Contain(f => f.RuleId == "COMPAT_WORKFLOW_TRANSITIONS_CHANGED" && f.Level == DescriptorCompatibilityLevel.Breaking);
    }
}
