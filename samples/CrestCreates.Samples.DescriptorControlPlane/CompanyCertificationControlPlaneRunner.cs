using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.Samples.DescriptorControlPlane;

/// <summary>
/// Sample-local control-plane runner that composes the descriptor platform
/// services (topology, change set, impact, compatibility, lifecycle governance,
/// package) into a synchronous analysis pipeline for a given
/// <see cref="CompanyCertificationChangeScenario"/>.
///
/// No runtime execution, capability handlers, workflow host, HumanTask completion,
/// persistence, UI, OCR, or distributed event bus - purely a deterministic
/// control-plane analysis.
/// </summary>
public sealed class CompanyCertificationControlPlaneRunner
{
    private readonly IDescriptorTopologyBuilder _topologyBuilder;
    private readonly IDescriptorChangeSetBuilder _changeSetBuilder;
    private readonly IDescriptorImpactAnalyzer _impactAnalyzer;
    private readonly IDescriptorCompatibilityAnalyzer _compatibilityAnalyzer;
    private readonly IDescriptorLifecycleGovernanceService _governanceService;
    private readonly IDescriptorPackageBuilder _packageBuilder;

    public CompanyCertificationControlPlaneRunner(
        IDescriptorTopologyBuilder topologyBuilder,
        IDescriptorChangeSetBuilder changeSetBuilder,
        IDescriptorImpactAnalyzer impactAnalyzer,
        IDescriptorCompatibilityAnalyzer compatibilityAnalyzer,
        IDescriptorLifecycleGovernanceService governanceService,
        IDescriptorPackageBuilder packageBuilder)
    {
        _topologyBuilder = topologyBuilder;
        _changeSetBuilder = changeSetBuilder;
        _impactAnalyzer = impactAnalyzer;
        _compatibilityAnalyzer = compatibilityAnalyzer;
        _governanceService = governanceService;
        _packageBuilder = packageBuilder;
    }

    /// <summary>
    /// Synchronous control-plane analysis pipeline.
    ///
    /// 1. Build topology from <paramref name="scenario"/>.After
    /// 2. Build change set from Before / After
    /// 3. Run impact analysis
    /// 4. Run compatibility analysis
    /// 5. Evaluate lifecycle governance for Activate transitions
    ///    (transitions are derived from change set; empty baseline adds a
    ///    default workflow activation)
    /// 6. Build descriptor package with topology/impact/compatibility/governance
    ///    evidence embedded
    /// 7. Return the report
    /// </summary>
    public CompanyCertificationControlPlaneReport Run(CompanyCertificationChangeScenario scenario)
    {
        // 1. Build topology from scenario.After
        var topology = _topologyBuilder.Build(scenario.After);

        // 2. Build change set from scenario.Before / scenario.After
        var changeSet = _changeSetBuilder.Build(scenario.Before, scenario.After);

        // 3. Run impact analysis
        var impact = _impactAnalyzer.Analyze(topology, changeSet);

        // 4. Run compatibility analysis
        var compatibility = _compatibilityAnalyzer.Analyze(
            scenario.Before, scenario.After, changeSet, impact);

        // 5. Evaluate lifecycle governance for Activate
        var transitions = BuildActivateTransitions(changeSet);

        var governanceRequest = new DescriptorLifecycleGovernanceRequest
        {
            Transitions = transitions,
            ValidationReport = ValidationReport.Empty,
            BindingReport = new RuntimeBindingReport(),
            TopologyDiagnostics = topology.Diagnostics,
            ImpactReport = impact,
            CompatibilityReport = compatibility,
        };

        var governance = _governanceService.Evaluate(governanceRequest);

        // 6. Build descriptor package including evidence
        var packageRequest = new DescriptorPackageBuildRequest
        {
            PackageId = SanitizePackageId(scenario.Name),
            PackageVersion = "1",
            Name = scenario.Name,
            Source = "CompanyCertificationControlPlane",
            Descriptors = scenario.After,
            TopologySnapshot = topology,
            ImpactReport = impact,
            CompatibilityReport = compatibility,
            GovernanceReport = governance,
        };

        var package = _packageBuilder.Build(packageRequest);

        // 7. Return report
        return new CompanyCertificationControlPlaneReport
        {
            ScenarioName = scenario.Name,
            Topology = topology,
            ChangeSet = changeSet,
            Impact = impact,
            Compatibility = compatibility,
            Governance = governance,
            Package = package,
        };
    }

    /// <summary>
    /// Derives <see cref="DescriptorLifecycleOperation.Activate"/> transitions
    /// from the change-set's changed refs.
    ///
    /// When the change set is empty (e.g. Baseline scenario where Before == After),
    /// a single Activate transition for workflow descriptor
    /// <c>wf_company_certification</c> version 1 is included so that governance
    /// has a subject to evaluate.
    /// </summary>
    private static IReadOnlyList<DescriptorLifecycleTransition> BuildActivateTransitions(
        DescriptorChangeSet changeSet)
    {
        var transitions = new List<DescriptorLifecycleTransition>(changeSet.Changes.Count + 1);

        foreach (var change in changeSet.Changes)
        {
            transitions.Add(new DescriptorLifecycleTransition
            {
                Subject = change.Ref,
                Operation = DescriptorLifecycleOperation.Activate,
                FromState = change.BeforeState,
                ToState = change.AfterState,
                Reason = $"{change.Kind} detected in change set",
            });
        }

        // Empty baseline - include default workflow activation
        if (transitions.Count == 0)
        {
            transitions.Add(new DescriptorLifecycleTransition
            {
                Subject = new DescriptorRef("workflow", "wf_company_certification", 1),
                Operation = DescriptorLifecycleOperation.Activate,
                Reason = "Empty baseline - default workflow activation",
            });
        }

        return transitions.AsReadOnly();
    }

    /// <summary>
    /// Converts a human-readable scenario name into a machine-friendly package id.
    /// </summary>
    private static string SanitizePackageId(string name)
    {
        var sanitized = string.Concat(name.Select(c =>
            char.IsLetterOrDigit(c) ? c : '-')).Trim('-');
        return string.IsNullOrEmpty(sanitized) ? "unnamed" : sanitized.ToLowerInvariant();
    }
}
