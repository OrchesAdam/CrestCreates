using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;
using DraftStore = CrestCreates.DescriptorDraft.Abstractions.IDescriptorDraftStore;

namespace CrestCreates.Agent.ControlPlane.Tests;

/// <summary>
/// Unit tests for the tenant-safe resource resolver.
/// Validates snapshot reuse, version-aware resolution, ambiguity handling,
/// and tenant scoping.
/// </summary>
public class DescriptorVisibilityPipelineTests
{
    private const string TestTenantId = "tenant-001";

    private readonly Mock<DraftStore> _draftStoreMock = new();
    private readonly Mock<IDescriptorCatalog> _catalogMock = new();

    private AgentControlPlaneResourceResolver CreateResolver() =>
        new(_draftStoreMock.Object, _catalogMock.Object);

    // ── Draft resolution ──

    [Fact]
    public async Task ResolveDraftAsync_Found_ReturnsSnapshot()
    {
        var draft = CreateTestDraft();
        _draftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);

        var resolver = CreateResolver();
        var result = await resolver.ResolveDraftAsync(TestTenantId, "draft-001", CancellationToken.None);

        result.Status.Should().Be(ResourceResolutionStatus.Resolved);
        result.Snapshot.Should().NotBeNull();
        result.Snapshot!.Draft.Should().BeSameAs(draft);
    }

    [Fact]
    public async Task ResolveDraftAsync_NotFound_ReturnsMissing()
    {
        _draftStoreMock.Setup(s => s.GetAsync(TestTenantId, "nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Draft?)null);

        var resolver = CreateResolver();
        var result = await resolver.ResolveDraftAsync(TestTenantId, "nonexistent", CancellationToken.None);

        result.Status.Should().Be(ResourceResolutionStatus.NotFound);
        result.Snapshot.Should().BeNull();
    }

    [Fact]
    public async Task ResolveDraftAsync_Cancellation_Propagates()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _draftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Throws(new OperationCanceledException());

        var resolver = CreateResolver();
        var act = () => resolver.ResolveDraftAsync(TestTenantId, "draft-001", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Descriptor resolution ──

    [Fact]
    public void ResolveDescriptor_ExactVersion_ReturnsSnapshot()
    {
        var descriptor = new TestVersionedDescriptor("test", "desc-001", version: 2);
        _catalogMock.Setup(c => c.GetAll()).Returns([descriptor]);

        var resolver = CreateResolver();
        var result = resolver.ResolveDescriptor(new DescriptorRef("test", "desc-001", 2));

        result.Status.Should().Be(ResourceResolutionStatus.Resolved);
        result.Snapshot.Should().NotBeNull();
        result.Snapshot!.Descriptor.Should().BeSameAs(descriptor);
        result.Snapshot.Ref.Version.Should().Be(2);
    }

    [Fact]
    public void ResolveDescriptor_VersionMismatch_ReturnsMissing()
    {
        var descriptor = new TestVersionedDescriptor("test", "desc-001", version: 1);
        _catalogMock.Setup(c => c.GetAll()).Returns([descriptor]);

        var resolver = CreateResolver();
        var result = resolver.ResolveDescriptor(new DescriptorRef("test", "desc-001", 2));

        result.Status.Should().Be(ResourceResolutionStatus.NotFound);
    }

    [Fact]
    public void ResolveDescriptor_Unpinned_SingleMatch_ReturnsSnapshot()
    {
        var descriptor = new TestVersionedDescriptor("test", "desc-001", version: 1);
        _catalogMock.Setup(c => c.GetAll()).Returns([descriptor]);

        var resolver = CreateResolver();
        var result = resolver.ResolveDescriptor(new DescriptorRef("test", "desc-001"));

        result.Status.Should().Be(ResourceResolutionStatus.Resolved);
        result.Snapshot!.Ref.Version.Should().Be(1);
    }

    [Fact]
    public void ResolveDescriptor_Unpinned_MultipleVersions_ReturnsAmbiguous()
    {
        var v1 = new TestVersionedDescriptor("test", "desc-001", version: 1);
        var v2 = new TestVersionedDescriptor("test", "desc-001", version: 2);
        _catalogMock.Setup(c => c.GetAll()).Returns([v1, v2]);

        var resolver = CreateResolver();
        var result = resolver.ResolveDescriptor(new DescriptorRef("test", "desc-001"));

        result.Status.Should().Be(ResourceResolutionStatus.Ambiguous);
    }

    [Fact]
    public void ResolveDescriptor_NotFound_ReturnsMissing()
    {
        _catalogMock.Setup(c => c.GetAll()).Returns([]);

        var resolver = CreateResolver();
        var result = resolver.ResolveDescriptor(new DescriptorRef("test", "desc-999"));

        result.Status.Should().Be(ResourceResolutionStatus.NotFound);
    }

    // ── Helpers ──

    private static Draft CreateTestDraft() => new()
    {
        TenantId = TestTenantId,
        DraftId = "draft-001",
        DescriptorKind = DescriptorKind.Event,
        DescriptorId = "test.desc-001",
        Operation = DraftAbstractions.DescriptorDraftOperation.Create,
        AuthorKind = DraftAbstractions.DescriptorDraftAuthorKind.Agent,
        AuthorId = "actor-001",
        CreatedAt = DateTimeOffset.UtcNow,
        Payload = new TestDraftPayload(DescriptorKind.Event, "test.desc-001", "Test"),
        Status = DraftAbstractions.DescriptorDraftStatus.Created
    };
}
