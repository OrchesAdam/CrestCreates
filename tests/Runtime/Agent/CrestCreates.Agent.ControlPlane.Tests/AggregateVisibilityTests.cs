using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Agent.ControlPlane.Tests;

/// <summary>
/// Tests for aggregate descriptor/draft visibility filtering.
/// Verifies that broad searches and draft lists return only visible
/// descriptors under the invocation's visibility scope.
/// </summary>
public class AggregateVisibilityTests : AgentControlPlaneTestBase
{
    private static AgentToolAuthorizationOptions WorkflowOnlyPolicy => new()
    {
        Mode = AgentToolAuthorizationMode.DevelopmentAllowAll,
        DeniedDescriptorKinds = { "Event" }
    };

    // ── Descriptor search ──

    [Fact]
    public async Task SearchDescriptors_Broad_Returns_Only_Visible_Kinds()
    {
        var service = CreateService(WorkflowOnlyPolicy);
        var context = CreateContext("SearchDescriptors");

        DescriptorCatalogMock.Setup(c => c.GetAll())
            .Returns([
                CreateTestDescriptor(ns: "a", id: "wf-001", kind: DescriptorKind.Workflow, name: "OrderWorkflow"),
                CreateTestDescriptor(ns: "a", id: "ev-001", kind: DescriptorKind.Event, name: "OrderPlaced")
            ]);

        var result = await service.SearchDescriptorsAsync(context, new DescriptorSearchRequest { MaxResults = 100 });

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.Descriptors.Should().HaveCount(1);
        result.Value.Descriptors[0].Kind.Should().Be(DescriptorKind.Workflow);
        result.Value.TotalCount.Should().Be(1);
        result.Value.WasTruncated.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == "RESULTS_SECURITY_TRIMMED");
    }

    [Fact]
    public async Task SearchDescriptors_ExplicitDeniedKind_IsDenied()
    {
        var service = CreateService(WorkflowOnlyPolicy);
        var context = CreateContext("SearchDescriptors");

        DescriptorCatalogMock.Setup(c => c.GetAll())
            .Returns([
                CreateTestDescriptor(ns: "a", id: "ev-001", kind: DescriptorKind.Event, name: "OrderPlaced")
            ]);

        var result = await service.SearchDescriptorsAsync(context, new DescriptorSearchRequest
        {
            Kind = DescriptorKind.Event,
            MaxResults = 100
        });

        // Spec §6.2: "If a caller explicitly supplies a denied DescriptorKind filter, return Denied."
        // An explicit denied kind is not the same as a broad search that happens to return
        // zero visible results — the caller is probing a specific denied kind.
        result.Status.Should().Be(AgentToolResultStatus.Denied);
        result.Diagnostics.Should().Contain(d => d.Code == "DESC_KIND_DENIED");
    }

    [Fact]
    public async Task SearchDescriptors_CatalogFailure_Returns_Failed()
    {
        var service = CreateService(WorkflowOnlyPolicy);
        var context = CreateContext("SearchDescriptors");

        DescriptorCatalogMock.Setup(c => c.GetAll())
            .Throws(new InvalidOperationException("Catalog unavailable"));

        var result = await service.SearchDescriptorsAsync(context, new DescriptorSearchRequest { MaxResults = 100 });

        result.Status.Should().Be(AgentToolResultStatus.Failed);
        result.Value.Should().BeNull();
    }

    // ── Draft list ──

    [Fact]
    public async Task ListDescriptorDrafts_Returns_Only_Visible_Kinds()
    {
        var service = CreateService(WorkflowOnlyPolicy);
        var context = CreateContext("ListDescriptorDrafts");

        DraftStoreMock.Setup(s => s.ListAsync(TestTenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                CreateTestDraft(draftId: "draft-wf-001", kind: DescriptorKind.Workflow, descriptorId: "wf-001"),
                CreateTestDraft(draftId: "draft-ev-001", kind: DescriptorKind.Event, descriptorId: "ev-001"),
                CreateTestDraft(draftId: "draft-wf-002", kind: DescriptorKind.Workflow, descriptorId: "wf-002")
            ]);

        var result = await service.ListDescriptorDraftsAsync(context, null);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.Drafts.Should().HaveCount(2);
        result.Value.Drafts.Should().AllSatisfy(d => d.DescriptorKind.Should().Be(DescriptorKind.Workflow));
        result.Value.TotalCount.Should().Be(2);
        result.Diagnostics.Should().Contain(d => d.Code == "RESULTS_SECURITY_TRIMMED");
    }

    [Fact]
    public async Task ListDescriptorDrafts_StoreFailure_Returns_Failed()
    {
        var service = CreateService(WorkflowOnlyPolicy);
        var context = CreateContext("ListDescriptorDrafts");

        DraftStoreMock.Setup(s => s.ListAsync(TestTenantId, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Store unavailable"));

        var result = await service.ListDescriptorDraftsAsync(context, null);

        result.Status.Should().Be(AgentToolResultStatus.Failed);
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task ListDescriptorDrafts_Audit_Contains_Only_Visible_Drafts()
    {
        var service = CreateService(WorkflowOnlyPolicy);
        var context = CreateContext("ListDescriptorDrafts");

        DraftStoreMock.Setup(s => s.ListAsync(TestTenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                CreateTestDraft(draftId: "draft-wf-001", kind: DescriptorKind.Workflow, descriptorId: "wf-001"),
                CreateTestDraft(draftId: "draft-ev-001", kind: DescriptorKind.Event, descriptorId: "ev-001")
            ]);

        var result = await service.ListDescriptorDraftsAsync(context, null);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.AuditRecord!.TouchedDraftIds.Should().HaveCount(1);
        result.AuditRecord.TouchedDraftIds.Should().Contain("draft-wf-001");
        result.AuditRecord.TouchedDraftIds.Should().NotContain("draft-ev-001");
    }
}
