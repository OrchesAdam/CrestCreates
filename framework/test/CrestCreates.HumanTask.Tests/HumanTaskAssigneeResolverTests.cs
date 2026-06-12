using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.HumanTask.Tests;

public class HumanTaskAssigneeResolverTests
{
    private static HumanTaskDescriptor CreateDescriptor(
        string id = "ht_01",
        AssigneeStrategy strategy = AssigneeStrategy.SingleUser)
    {
        return new HumanTaskDescriptor
        {
            Id = id,
            Name = "test",
            Version = 1,
            AssigneeStrategy = strategy,
            Interaction = new VersionedDescriptorRef<IInteractionDescriptor>("form_01", 1)
        };
    }

    private readonly DefaultHumanTaskAssigneeResolver _resolver = new();

    [Fact]
    public async Task AssigneeResolver_ExplicitUser_AssignsUser()
    {
        var descriptor = CreateDescriptor();
        var request = new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01",
            AssigneeUserId = "user-1"
        };

        var resolution = await _resolver.ResolveAsync(descriptor, request);

        resolution.AssigneeUserId.Should().Be("user-1");
        resolution.IsAssigned.Should().BeTrue();
        resolution.CandidateRoleIds.Should().BeEmpty();
    }

    [Fact]
    public async Task AssigneeResolver_ExplicitRole_AssignsRole()
    {
        var descriptor = CreateDescriptor();
        var request = new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01",
            AssigneeRoleId = "role-manager"
        };

        var resolution = await _resolver.ResolveAsync(descriptor, request);

        resolution.AssigneeRoleId.Should().Be("role-manager");
        resolution.IsAssigned.Should().BeTrue();
        resolution.AssigneeUserId.Should().BeNull();
    }

    [Fact]
    public async Task AssigneeResolver_UserTakesPrecedence_WhenUserAndRoleBothProvided()
    {
        var descriptor = CreateDescriptor();
        var request = new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01",
            AssigneeUserId = "user-1",
            AssigneeRoleId = "role-manager"
        };

        var resolution = await _resolver.ResolveAsync(descriptor, request);

        resolution.AssigneeUserId.Should().Be("user-1");
        resolution.AssigneeRoleId.Should().BeNull();
        resolution.CandidateRoleIds.Should().ContainSingle("role-manager");
        resolution.IsAssigned.Should().BeTrue();
    }

    [Fact]
    public async Task AssigneeResolver_SingleUserWithoutExplicitAssignee_ReturnsUnassigned()
    {
        var descriptor = CreateDescriptor(strategy: AssigneeStrategy.SingleUser);
        var request = new HumanTaskCreationRequest { HumanTaskId = "ht_01" };

        var resolution = await _resolver.ResolveAsync(descriptor, request);

        resolution.IsUnassigned.Should().BeTrue();
        resolution.IsAssigned.Should().BeFalse();
        resolution.HasCandidates.Should().BeFalse();
        resolution.AssigneeResolutionReason.Should().BeNull();
    }

    [Fact]
    public async Task AssigneeResolver_CandidateGroup_WithExplicitRole_AssignsRole()
    {
        var descriptor = CreateDescriptor(strategy: AssigneeStrategy.CandidateGroup);
        var request = new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01",
            AssigneeRoleId = "role-reviewers"
        };

        var resolution = await _resolver.ResolveAsync(descriptor, request);

        resolution.AssigneeRoleId.Should().Be("role-reviewers");
        resolution.IsAssigned.Should().BeTrue();
    }

    [Fact]
    public async Task AssigneeResolver_RoundRobin_ReturnsUnassigned()
    {
        var descriptor = CreateDescriptor(strategy: AssigneeStrategy.RoundRobin);
        var request = new HumanTaskCreationRequest { HumanTaskId = "ht_01" };

        var resolution = await _resolver.ResolveAsync(descriptor, request);

        resolution.IsUnassigned.Should().BeTrue();
        resolution.AssigneeResolutionReason.Should().Be(
            "RoundRobin strategy is not yet implemented");
    }

    [Fact]
    public async Task AssigneeResolver_LeastLoaded_ReturnsUnassigned()
    {
        var descriptor = CreateDescriptor(strategy: AssigneeStrategy.LeastLoaded);
        var request = new HumanTaskCreationRequest { HumanTaskId = "ht_01" };

        var resolution = await _resolver.ResolveAsync(descriptor, request);

        resolution.IsUnassigned.Should().BeTrue();
        resolution.AssigneeResolutionReason.Should().Be(
            "LeastLoaded strategy is not yet implemented");
    }

    [Fact]
    public async Task AssigneeResolver_RequestOrgAndPosition_StoresContext()
    {
        var descriptor = CreateDescriptor();
        var request = new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01",
            RequestedOrganizationUnitId = "org-dept-1",
            RequestedPositionId = "pos-manager"
        };

        var resolution = await _resolver.ResolveAsync(descriptor, request);

        resolution.OrganizationUnitId.Should().Be("org-dept-1");
        resolution.PositionId.Should().Be("pos-manager");
        resolution.IsAssigned.Should().BeFalse();
        resolution.HasCandidates.Should().BeFalse();
    }

    [Fact]
    public async Task AssigneeResolver_WhitespaceUserId_IsTreatedAsNull()
    {
        var descriptor = CreateDescriptor();
        var request = new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01",
            AssigneeUserId = "   ",
            AssigneeRoleId = "role-manager"
        };

        var resolution = await _resolver.ResolveAsync(descriptor, request);

        resolution.AssigneeRoleId.Should().Be("role-manager");
        resolution.AssigneeUserId.Should().BeNull();
    }

    [Fact]
    public async Task AssigneeResolver_WhitespaceOrgPosition_IsNotStoredInUnassigned()
    {
        var descriptor = CreateDescriptor();
        var request = new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01",
            RequestedOrganizationUnitId = "  ",
            RequestedPositionId = "\t"
        };

        var resolution = await _resolver.ResolveAsync(descriptor, request);

        resolution.IsUnassigned.Should().BeTrue();
    }
}
