using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.ContextPack.Abstractions;
using CrestCreates.Sample.AssetManagement.Contracts;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Sample.AssetManagement.E2E.Tests;

public sealed class AssetWorkflowFormSchemaContextAcceptanceTests
{
    [Fact]
    public async Task RuntimeScenarioContextPack_TraversesWorkflowTaskFormAndMaintenanceSchema()
    {
        await using var harness = await AssetControlPlaneApprovalHarness.CreateAsync(
            tenantId: "asset-workflow-form-schema-context-tenant",
            authorId: "asset-workflow-form-schema-context-author");

        var baseline = AssetControlPlaneApprovalHarness.BuildDeployedBaseline();
        var workflow = new DescriptorRef("workflow", AssetContractIds.MaintenanceWorkflow, 1);
        var humanTask = new DescriptorRef("humantask", AssetContractIds.MaintenanceHumanTask, 1);
        var form = new DescriptorRef("form", AssetContractIds.MaintenanceForm, 1);
        var schema = new DescriptorRef(
            "schema", "asset-management.schema.maintenance-decision", 1);
        var expectedRefs = new[] { workflow, humanTask, form, schema };

        var topology = harness.Services
            .GetRequiredService<IDescriptorTopologyBuilder>()
            .Build(baseline);
        var contextRequest = AssetAuthoringContextRequestFactory.Create(
            harness.TenantId,
            "Author the existing Asset maintenance workflow context.",
            workflow);
        var contextPack = harness.Services
            .GetRequiredService<IMetadataContextPackBuilder>()
            .Build(contextRequest, topology, baseline);

        contextPack.Descriptors
            .Should()
            .HaveCount(4);
        contextPack.Descriptors
            .Select(descriptor => descriptor.Ref)
            .Should()
            .BeEquivalentTo(expectedRefs);
        contextPack.Descriptors
            .Should()
            .OnlyContain(descriptor => expectedRefs.Contains(descriptor.Ref));
        contextPack.Descriptors
            .Where(descriptor => descriptor.Ref == workflow
                || descriptor.Ref == humanTask
                || descriptor.Ref == form
                || descriptor.Ref == schema)
            .Should()
            .OnlyContain(descriptor => descriptor.Hashes != null);

        contextPack.Relationships.Should().HaveCount(3);
        contextPack.Relationships.Should().ContainSingle(relationship =>
            relationship.From == workflow
            && relationship.To == humanTask
            && relationship.Kind == RelationshipKind.Uses
            && relationship.Role == null);
        contextPack.Relationships.Should().ContainSingle(relationship =>
            relationship.From == humanTask
            && relationship.To == form
            && relationship.Kind == RelationshipKind.Uses
            && relationship.Role == "Interaction");
        contextPack.Relationships.Should().ContainSingle(relationship =>
            relationship.From == form
            && relationship.To == schema
            && relationship.Kind == RelationshipKind.Uses
            && relationship.Role == "Schema");

        var descriptorRefs = contextPack.Descriptors.Select(descriptor => descriptor.Ref).ToHashSet();
        contextPack.Relationships.Should().OnlyContain(relationship =>
            descriptorRefs.Contains(relationship.From)
            && descriptorRefs.Contains(relationship.To));
        contextPack.Summary.WasTruncated.Should().BeFalse();
        contextPack.Diagnostics.Should().NotContain(d =>
            d.Code == MetadataContextPackDiagnosticCodes.TruncatedByDepth
            || d.Code == MetadataContextPackDiagnosticCodes.TruncatedByCount);
    }
}
