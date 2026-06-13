using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Workflow.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Metadata.DescriptorCompatibility;

public sealed class WorkflowCompatibilityRule : IDescriptorCompatibilityRule
{
    public string RuleId => "Workflow";

    public bool CanAnalyze(DescriptorChange change, IDescriptor? before, IDescriptor? after)
    {
        return change.Kind is DescriptorChangeKind.ContractHashChanged or DescriptorChangeKind.Updated
            && (after is WorkflowDescriptor || before is WorkflowDescriptor);
    }

    public IReadOnlyList<DescriptorCompatibilityFinding> Analyze(
        DescriptorChange change, IDescriptor? before, IDescriptor? after,
        DescriptorImpactAnalysisReport impactReport, DescriptorCompatibilityAnalysisOptions options)
    {
        var findings = new List<DescriptorCompatibilityFinding>();
        var wb = before as WorkflowDescriptor;
        var wa = after as WorkflowDescriptor;
        if (wa == null) return findings;

        var affectedRefs = GetAffectedRefs(change, impactReport);

        if (wb != null)
        {
            // Variable schema
            if (!SchemaRefsEqual(wb.VariableSchema, wa.VariableSchema))
                findings.Add(MakeFinding(change, "COMPAT_WORKFLOW_VARIABLE_SCHEMA_CHANGED",
                    DescriptorCompatibilityLevel.Breaking, "Workflow variable schema ref changed.",
                    affectedRefs, "VariableSchema"));

            // Steps: compare by Id
            var beforeSteps = wb.Steps.ToDictionary(s => s.Id);
            var afterSteps = wa.Steps.ToDictionary(s => s.Id);

            foreach (var id in beforeSteps.Keys.Except(afterSteps.Keys))
                findings.Add(MakeFinding(change, "COMPAT_WORKFLOW_STEP_REMOVED",
                    DescriptorCompatibilityLevel.Breaking, $"Workflow step '{id}' removed.",
                    affectedRefs, $"Steps.{id}"));

            foreach (var id in afterSteps.Keys.Except(beforeSteps.Keys))
                findings.Add(MakeFinding(change, "COMPAT_WORKFLOW_STEP_ADDED",
                    DescriptorCompatibilityLevel.Risky, $"Workflow step '{id}' added.",
                    affectedRefs, $"Steps.{id}"));

            foreach (var id in beforeSteps.Keys.Intersect(afterSteps.Keys))
            {
                var bs = beforeSteps[id];
                var as_ = afterSteps[id];

                if (bs.Target.GetType() != as_.Target.GetType() ||
                    GetTargetRef(bs.Target) != GetTargetRef(as_.Target))
                    findings.Add(MakeFinding(change, "COMPAT_WORKFLOW_STEP_TARGET_CHANGED",
                        DescriptorCompatibilityLevel.Breaking, $"Step '{id}' target changed.",
                        affectedRefs, $"Steps.{id}.Target"));

                if (!bs.Transitions.SequenceEqual(as_.Transitions))
                    findings.Add(MakeFinding(change, "COMPAT_WORKFLOW_TRANSITIONS_CHANGED",
                        DescriptorCompatibilityLevel.Breaking, $"Step '{id}' transitions changed.",
                        affectedRefs, $"Steps.{id}.Transitions"));

                if (bs.OnError != as_.OnError)
                    findings.Add(MakeFinding(change, "COMPAT_WORKFLOW_ERROR_BEHAVIOR_CHANGED",
                        DescriptorCompatibilityLevel.Risky, $"Step '{id}' OnError changed.",
                        affectedRefs, $"Steps.{id}.OnError"));

                if (bs.Condition != as_.Condition || bs.InputMapping != as_.InputMapping ||
                    bs.OutputMapping != as_.OutputMapping)
                    findings.Add(MakeFinding(change, "COMPAT_WORKFLOW_MAPPING_CHANGED",
                        DescriptorCompatibilityLevel.Risky, $"Step '{id}' condition/mapping changed.",
                        affectedRefs, $"Steps.{id}.Mapping"));
            }

            // Default variable scope
            if (wb.DefaultVariableScope != wa.DefaultVariableScope)
                findings.Add(MakeFinding(change, "COMPAT_WORKFLOW_VARIABLE_SCOPE_CHANGED",
                    DescriptorCompatibilityLevel.Risky,
                    $"Workflow variable scope changed from {wb.DefaultVariableScope} to {wa.DefaultVariableScope}.",
                    affectedRefs, "DefaultVariableScope"));
        }

        return findings;
    }

    private static string GetTargetRef(InteractionTarget target) => target switch
    {
        CapabilityTarget ct => $"{ct.Capability.Id}@{ct.Capability.Version}",
        HumanTaskTarget ht => $"{ht.HumanTask.Id}@{ht.HumanTask.Version}",
        SubWorkflowTarget sw => $"{sw.SubWorkflow.Id}@{sw.SubWorkflow.Version}",
        _ => target.GetType().Name
    };

    private static bool SchemaRefsEqual(
        VersionedDescriptorRef<SchemaDescriptor>? a,
        VersionedDescriptorRef<SchemaDescriptor>? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        return a.Value.Id == b.Value.Id && a.Value.Version == b.Value.Version;
    }

    private static IReadOnlyList<DescriptorRef> GetAffectedRefs(DescriptorChange change, DescriptorImpactAnalysisReport report)
        => report.Paths.Where(p => p.SourceChange == change.Ref).Select(p => p.Affected).Distinct().ToArray();

    private static DescriptorCompatibilityFinding MakeFinding(
        DescriptorChange change, string ruleId, DescriptorCompatibilityLevel level,
        string message, IReadOnlyList<DescriptorRef> affectedRefs, string path)
        => new()
        {
            Subject = change.Ref, ChangeKind = change.Kind, Level = level,
            Kind = DescriptorCompatibilityFindingKind.Contract, RuleId = ruleId,
            Message = message, AffectedRefs = affectedRefs, Path = path
        };
}
