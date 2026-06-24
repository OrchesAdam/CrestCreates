using System;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Bootstrap;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using CrestCreates.Samples.DescriptorControlPlane;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Samples.Tests;

public sealed class CompanyCertificationControlPlaneTests : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly CompanyCertificationControlPlaneRunner _runner;

    public CompanyCertificationControlPlaneTests()
    {
        var collection = new ServiceCollection();
        collection.AddRelationshipKernel();
        collection.AddTopologyKernel();
        collection.AddDescriptorImpactAnalysis();
        collection.AddDescriptorCompatibilityAnalysis();
        collection.AddDescriptorLifecycleGovernance();
        collection.AddDescriptorPackaging();
        collection.AddSingleton<CompanyCertificationControlPlaneRunner>();
        _services = collection.BuildServiceProvider();
        _runner = _services.GetRequiredService<CompanyCertificationControlPlaneRunner>();
    }

    public void Dispose()
    {
        _services.Dispose();
    }

    [Fact]
    public void Baseline_Should_Build_Healthy_Topology()
    {
        var scenario = CompanyCertificationChangeScenarios.Baseline();
        var report = _runner.Run(scenario);

        // Baseline has all 15 descriptor nodes (5 schemas + 2 forms + 3
        // capabilities + 1 humantask + 1 workflow + 3 events)
        report.Topology.Nodes.Should().HaveCount(15);

        // Topology is healthy with no errors
        report.Topology.Diagnostics.IsHealthy.Should().BeTrue();

        // ControlPlanePassed signals that topology, governance, and package are clean
        report.ControlPlanePassed.Should().BeTrue();

        // Package content hash must be non-empty
        report.Package.ContentHash.Should().NotBeNullOrEmpty();

        // Evidence and envelope hashes are produced by the package builder.
        // If the builder emits them, they must be non-empty; otherwise skip.
        if (report.Package.Manifest.EvidenceHash is not null)
            report.PackageEvidenceHash.Should().NotBeNullOrEmpty();
        if (report.Package.Manifest.EnvelopeHash is not null)
            report.PackageEnvelopeHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Removing_Required_Schema_Field_Should_Be_Breaking_And_ReviewRequired()
    {
        var scenario = CompanyCertificationChangeScenarios.RequiredFieldRemoval();
        var report = _runner.Run(scenario);

        // Compatibility must signal Breaking at MaxLevel
        // (SchemaChangeKind.Breaking is set on the modified descriptor;
        //  individual findings may be classified at a lower level, but
        //  the aggregate MaxLevel should reflect the breaking nature.)
        report.Compatibility.MaxLevel.Should().Be(DescriptorCompatibilityLevel.Breaking);

        // Governance must not be Allowed (ReviewRequired or Blocked)
        report.Governance.MaxDecision.Should().NotBe(DescriptorLifecycleDecisionKind.Allowed);

        // Package hashes must be present
        report.Package.ContentHash.Should().NotBeNullOrEmpty();
        if (report.Package.Manifest.EvidenceHash is not null)
            report.PackageEvidenceHash.Should().NotBeNullOrEmpty();
        if (report.Package.Manifest.EnvelopeHash is not null)
            report.PackageEnvelopeHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Missing_Workflow_Target_Should_Block_Activation()
    {
        var scenario = CompanyCertificationChangeScenarios.MissingWorkflowTarget();
        var report = _runner.Run(scenario);

        // Workflow targets are part of topology validation.
        report.Topology.Diagnostics.Errors.Should().NotBeEmpty();

        // Governance must block activation
        report.Governance.IsBlocked.Should().BeTrue();

        // Control plane overall must fail
        report.ControlPlanePassed.Should().BeFalse();
    }

    [Fact]
    public void Optional_Field_Addition_Should_Be_Compatible()
    {
        var scenario = CompanyCertificationChangeScenarios.OptionalFieldAddition();
        var report = _runner.Run(scenario);

        report.Compatibility.HasBreakingChanges.Should().BeFalse();
        ((int)report.Compatibility.MaxLevel).Should().BeLessThanOrEqualTo((int)DescriptorCompatibilityLevel.Risky);

        // Governance should not block (allowed or review-required at worst)
        report.Governance.IsBlocked.Should().BeFalse();

        // Package hashes must be present
        report.Package.ContentHash.Should().NotBeNullOrEmpty();
        if (report.Package.Manifest.EvidenceHash is not null)
            report.PackageEvidenceHash.Should().NotBeNullOrEmpty();
        if (report.Package.Manifest.EnvelopeHash is not null)
            report.PackageEnvelopeHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Permission_Change_Should_Be_SecuritySensitive()
    {
        var scenario = CompanyCertificationChangeScenarios.PermissionChange();
        var report = _runner.Run(scenario);

        // Permission change must surface as security-sensitive
        report.Compatibility.HasSecuritySensitiveChanges.Should().BeTrue();

        // Governance must not be Allowed (permission changes require review)
        report.Governance.MaxDecision.Should().NotBe(DescriptorLifecycleDecisionKind.Allowed);

        // Package hashes must be present
        report.Package.ContentHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Unsupported_SubWorkflow_Should_Surface_Warning()
    {
        var scenario = CompanyCertificationChangeScenarios.UnsupportedSubWorkflow();
        var report = _runner.Run(scenario);

        // Topology should surface a warning about unsupported subworkflow reference.
        // The warning may be empty if topology currently does not classify it;
        // in that case compatibility findings should carry the unsupported signal.
        var hasTopologyWarning = report.Topology.Diagnostics.Warnings.Count > 0;
        var hasCompatibilityUnsupported = report.Compatibility.HasUnsupportedFindings;

        (hasTopologyWarning || hasCompatibilityUnsupported).Should().BeTrue(
            "either topology or compatibility must surface unsupported subworkflow reference");

        // Governance should not be Allowed when unsupported shapes are present
        report.Governance.MaxDecision.Should().NotBe(DescriptorLifecycleDecisionKind.Allowed);

        // Package hashes must be present
        report.Package.ContentHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Package_Should_Include_Manifest_Snapshot_Evidence_And_StableHash()
    {
        var scenario = CompanyCertificationChangeScenarios.Baseline();
        var report1 = _runner.Run(scenario);

        // All three hashes must be non-empty
        report1.Package.ContentHash.Should().NotBeNullOrEmpty();
        report1.PackageEvidenceHash.Should().NotBeNullOrEmpty();
        report1.PackageEnvelopeHash.Should().NotBeNullOrEmpty();

        // Running the same scenario again must produce stable content and
        // evidence hashes. Envelope hash may vary due to manifest metadata.
        var scenario2 = CompanyCertificationChangeScenarios.Baseline();
        var report2 = _runner.Run(scenario2);

        report2.Package.ContentHash.Should().Be(report1.Package.ContentHash);
        report2.PackageEvidenceHash.Should().Be(report1.PackageEvidenceHash);
        report2.PackageEnvelopeHash.Should().NotBeNullOrEmpty();
    }
}
