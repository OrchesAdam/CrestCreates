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

    [Fact]
    public async Task DraftOperation_OnDeniedKind_IsRejected()
    {
        // Verify kind deny works for operations that resolve kind from the draft store
        var service = CreateService(OptionsWithEventDenied);
        var context = CreateContext("GetDescriptorDraft");

        // Set up a draft with Event kind in the store
        var draft = new Draft
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

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);

        var result = await service.GetDescriptorDraftAsync(context, "draft-001");

        result.Status.Should().Be(AgentToolResultStatus.Denied);
        result.Diagnostics.Should().Contain(d => d.Code == "DESC_KIND_DENIED");
    }

    [Fact]
    public async Task GetDescriptorByRef_DeniedDescriptorKind_IsRejected()
    {
        var service = CreateService(OptionsWithEventDenied);
        var context = CreateContext("GetDescriptorByRef");

        // Set up catalog with an Event descriptor
        DescriptorCatalogMock.Setup(c => c.GetAll())
            .Returns([CreateTestDescriptor(kind: DescriptorKind.Event)]);

        var result = await service.GetDescriptorByRefAsync(context, CreateDescriptorRef());

        result.Status.Should().Be(AgentToolResultStatus.Denied);
        result.Diagnostics.Should().Contain(d => d.Code == "DESC_KIND_DENIED");
    }
}
