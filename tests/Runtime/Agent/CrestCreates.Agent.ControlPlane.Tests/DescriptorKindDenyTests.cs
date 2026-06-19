using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Tests;

/// <summary>
/// End-to-end tests verifying that DeniedDescriptorKinds actually fires
/// through the full facade → authorization pipeline. These tests exist
/// because an earlier bug left DeniedDescriptorKinds inert: the facade
/// never populated DescriptorKindConstraint on the permission requirement,
/// so the authorization service could never match a denied kind.
///
/// After the two-phase auth redesign, kind resolution runs AFTER coarse
/// authorization (tool-name integrity, manifest, permission/category/actor
/// deny), and kind deny is fail-closed: if DeniedDescriptorKinds is
/// configured and the kind cannot be resolved, the invocation is denied.
/// </summary>
public class DescriptorKindDenyTests : AgentControlPlaneTestBase
{
    /// <summary>
    /// Options that allow mutating tools but deny the "Event" descriptor kind.
    /// This proves that DeniedDescriptorKinds overrides category-level defaults.
    /// </summary>
    private static AgentToolAuthorizationOptions OptionsWithEventDenied => new()
    {
        Mode = AgentToolAuthorizationMode.ExplicitPolicy,
        AllowReadOnlyToolsByDefault = true,
        AllowMutationToolsByDefault = true,
        AllowActivationHandoffToolsByDefault = true,
        DeniedDescriptorKinds = new HashSet<string>(StringComparer.Ordinal) { "Event" }
    };

    // ── Direct kind from request ──

    [Fact]
    public async Task CreateDescriptorDraft_DeniedDescriptorKind_IsRejected()
    {
        var service = CreateService(OptionsWithEventDenied);
        var context = CreateContext("CreateDescriptorDraft");

        var request = new CreateDescriptorDraftRequest
        {
            DescriptorKind = DescriptorKind.Event,
            DescriptorId = "test.desc-001",
            Operation = DraftAbstractions.DescriptorDraftOperation.Create,
            Payload = new TestDraftPayload(DescriptorKind.Event, "test.desc-001", "TestEvent")
        };

        var result = await service.CreateDescriptorDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.Denied);
        result.Diagnostics.Should().Contain(d => d.Code == "DESC_KIND_DENIED");
    }

    [Fact]
    public async Task CreateDescriptorDraft_AllowedDescriptorKind_Succeeds()
    {
        var service = CreateService(OptionsWithEventDenied);
        var context = CreateContext("CreateDescriptorDraft");

        var request = new CreateDescriptorDraftRequest
        {
            DescriptorKind = DescriptorKind.Schema,
            DescriptorId = "test.desc-002",
            Operation = DraftAbstractions.DescriptorDraftOperation.Create,
            Payload = new TestDraftPayload(DescriptorKind.Schema, "test.desc-002", "TestSchema")
        };

        DraftStoreMock.Setup(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await service.CreateDescriptorDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.Success);
    }

    [Fact]
    public async Task DeniedDescriptorKind_Overrides_ExplicitAllowPermission()
    {
        // Explicitly allow DraftCreate permission but deny Event kind — deny must win
        var options = new AgentToolAuthorizationOptions
        {
            Mode = AgentToolAuthorizationMode.ExplicitPolicy,
            AllowReadOnlyToolsByDefault = true,
            AllowMutationToolsByDefault = false,
            AllowActivationHandoffToolsByDefault = false,
            AllowedPermissions = new HashSet<string>(StringComparer.Ordinal) { AgentToolPermissionName.DraftCreate },
            DeniedDescriptorKinds = new HashSet<string>(StringComparer.Ordinal) { "Event" }
        };

        var service = CreateService(options);
        var context = CreateContext("CreateDescriptorDraft");

        var request = new CreateDescriptorDraftRequest
        {
            DescriptorKind = DescriptorKind.Event,
            DescriptorId = "test.desc-001",
            Operation = DraftAbstractions.DescriptorDraftOperation.Create,
            Payload = new TestDraftPayload(DescriptorKind.Event, "test.desc-001", "TestEvent")
        };

        var result = await service.CreateDescriptorDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.Denied);
        result.Diagnostics.Should().Contain(d => d.Code == "DESC_KIND_DENIED");
    }

    // ── Kind from draft store ──

    [Fact]
    public async Task DraftOperation_OnDeniedKind_IsRejected()
    {
        var service = CreateService(OptionsWithEventDenied);
        var context = CreateContext("GetDescriptorDraft");

        var draft = CreateEventDraft();
        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);

        var result = await service.GetDescriptorDraftAsync(context, "draft-001");

        result.Status.Should().Be(AgentToolResultStatus.Denied);
        result.Diagnostics.Should().Contain(d => d.Code == "DESC_KIND_DENIED");
    }

    // ── Kind from descriptor catalog ──

    [Fact]
    public async Task GetDescriptorByRef_DeniedDescriptorKind_IsRejected()
    {
        var service = CreateService(OptionsWithEventDenied);
        var context = CreateContext("GetDescriptorByRef");

        DescriptorCatalogMock.Setup(c => c.GetAll())
            .Returns([CreateTestDescriptor(kind: DescriptorKind.Event)]);

        var result = await service.GetDescriptorByRefAsync(context, CreateDescriptorRef());

        result.Status.Should().Be(AgentToolResultStatus.Denied);
        result.Diagnostics.Should().Contain(d => d.Code == "DESC_KIND_DENIED");
    }

    // ── Fail-closed: unresolvable kind is denied when DeniedDescriptorKinds is configured ──

    [Fact]
    public async Task DraftOperation_UnresolvableKind_IsDenied_FailClosed()
    {
        // When the draft doesn't exist, the kindResolver returns null.
        // With DeniedDescriptorKinds configured, null kind → deny (fail-closed).
        var service = CreateService(OptionsWithEventDenied);
        var context = CreateContext("GetDescriptorDraft");

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Draft?)null);

        var result = await service.GetDescriptorDraftAsync(context, "nonexistent");

        result.Status.Should().Be(AgentToolResultStatus.Denied);
        result.Diagnostics.Should().Contain(d => d.Code == "DESC_KIND_DENIED");
    }

    [Fact]
    public async Task DraftOperation_UnresolvableKind_IsAllowed_WhenNoKindsDenied()
    {
        // When no DeniedDescriptorKinds is configured, null kind → allowed (not fail-closed)
        var service = CreateService(AgentToolAuthorizationOptions.DevelopmentDefaults);
        var context = CreateContext("GetDescriptorDraft");

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Draft?)null);

        var result = await service.GetDescriptorDraftAsync(context, "nonexistent");

        // Not denied by kind — but NotFound because the draft doesn't exist
        result.Status.Should().Be(AgentToolResultStatus.NotFound);
    }

    [Fact]
    public async Task CatalogFailure_IsDenied_FailClosed()
    {
        // When the catalog throws, kindResolver fails, kind is null,
        // and fail-closed applies if DeniedDescriptorKinds is configured.
        var service = CreateService(OptionsWithEventDenied);
        var context = CreateContext("GetDescriptorByRef");

        DescriptorCatalogMock.Setup(c => c.GetAll())
            .Throws(new InvalidOperationException("Catalog failure"));

        var result = await service.GetDescriptorByRefAsync(context, CreateDescriptorRef());

        result.Status.Should().Be(AgentToolResultStatus.Denied);
        result.Diagnostics.Should().Contain(d => d.Code == "DESC_KIND_DENIED");
    }

    [Fact]
    public async Task CatalogFailure_IsAllowed_WhenNoKindsDenied()
    {
        // When no DeniedDescriptorKinds is configured, catalog failure → not denied by kind
        var service = CreateService(AgentToolAuthorizationOptions.DevelopmentDefaults);
        var context = CreateContext("GetDescriptorByRef");

        DescriptorCatalogMock.Setup(c => c.GetAll())
            .Throws(new InvalidOperationException("Catalog failure"));

        var result = await service.GetDescriptorByRefAsync(context, CreateDescriptorRef());

        // Not denied by kind — the action itself fails with the catalog exception
        result.Status.Should().Be(AgentToolResultStatus.Failed);
        result.Diagnostics.Should().Contain(d => d.Code == "TOOL_INVOCATION_FAILED");
    }

    // ── Coarse auth gates resource access ──

    [Fact]
    public async Task CoarseAuth_DeniedTool_DoesNotTouchStore()
    {
        // Verify that when coarse auth denies the tool, the kindResolver never runs
        // (and thus the store is never accessed)
        var options = new AgentToolAuthorizationOptions
        {
            Mode = AgentToolAuthorizationMode.ExplicitPolicy,
            AllowReadOnlyToolsByDefault = false,
            AllowMutationToolsByDefault = false,
            AllowActivationHandoffToolsByDefault = false,
            DeniedDescriptorKinds = new HashSet<string>(StringComparer.Ordinal) { "Event" }
        };

        var service = CreateService(options);
        var context = CreateContext("CreateDescriptorDraft");

        var request = new CreateDescriptorDraftRequest
        {
            DescriptorKind = DescriptorKind.Event,
            DescriptorId = "test.desc-001",
            Operation = DraftAbstractions.DescriptorDraftOperation.Create,
            Payload = new TestDraftPayload(DescriptorKind.Event, "test.desc-001", "TestEvent")
        };

        var result = await service.CreateDescriptorDraftAsync(context, request);

        // Denied by coarse auth (DraftCreate is a mutating tool, not allowed)
        result.Status.Should().Be(AgentToolResultStatus.Denied);

        // The store was never accessed — kindResolver never ran
        DraftStoreMock.Verify(s => s.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        DraftStoreMock.Verify(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Version-aware descriptor ref kind resolution ──

    [Fact]
    public async Task GetDescriptorByRef_VersionedRef_ResolvesCorrectKind()
    {
        var service = CreateService(OptionsWithEventDenied);
        var context = CreateContext("GetDescriptorByRef");

        // Set up catalog with an Event descriptor (versioned)
        var versionedDesc = CreateTestDescriptor(kind: DescriptorKind.Event);
        DescriptorCatalogMock.Setup(c => c.GetAll())
            .Returns([versionedDesc]);

        // Use a versioned ref — kind resolution should match version too
        var versionedRef = new DescriptorRef("test", "desc-001", 1);
        var result = await service.GetDescriptorByRefAsync(context, versionedRef);

        result.Status.Should().Be(AgentToolResultStatus.Denied);
        result.Diagnostics.Should().Contain(d => d.Code == "DESC_KIND_DENIED");
    }

    // ── Helper ──

    private Draft CreateEventDraft() => new()
    {
        TenantId = TestTenantId,
        DraftId = "draft-001",
        DescriptorKind = DescriptorKind.Event,
        DescriptorId = "test.desc-001",
        Operation = DraftAbstractions.DescriptorDraftOperation.Create,
        AuthorKind = DraftAbstractions.DescriptorDraftAuthorKind.Agent,
        AuthorId = TestActorId,
        CreatedAt = DateTimeOffset.UtcNow,
        Payload = new TestDraftPayload(DescriptorKind.Event, "test.desc-001", "Test"),
        Status = DraftAbstractions.DescriptorDraftStatus.Created
    };
}
