using CrestCreates.Organization;
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization.Tests;

public class OrganizationIdentityServiceTests
{
    private static async Task<DefaultOrganizationIdentityService> CreateServiceAsync(
        List<UserOrganizationMembership>? memberships = null,
        List<UserOrganizationRoleAssignment>? roleAssignments = null)
    {
        var store = new InMemoryOrganizationStore();
        if (memberships is not null)
            foreach (var m in memberships)
                await store.SaveMembershipAsync(m);
        if (roleAssignments is not null)
            foreach (var r in roleAssignments)
                await store.SaveRoleAssignmentAsync(r);
        return new DefaultOrganizationIdentityService(store);
    }

    [Fact]
    public async Task GetContext_ReturnsOrganizationsRolesPositions()
    {
        var memberships = new List<UserOrganizationMembership>
        {
            new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1", IsPrimary = true, IsActive = true, PositionId = "pos-1", CreatedAt = DateTimeOffset.UtcNow.AddDays(-1) },
            new() { Id = "m-2", UserId = "user-1", OrganizationUnitId = "dept-2", IsActive = true, PositionId = "pos-2", CreatedAt = DateTimeOffset.UtcNow },
        };
        var roleAssignments = new List<UserOrganizationRoleAssignment>
        {
            new() { Id = "ra-1", UserId = "user-1", RoleId = "admin", IsActive = true },
            new() { Id = "ra-2", UserId = "user-1", RoleId = "user", IsActive = true },
            new() { Id = "ra-3", UserId = "user-1", RoleId = "inactive-role", IsActive = false },
        };
        var service = await CreateServiceAsync(memberships, roleAssignments);
        var context = await service.GetContextAsync("user-1");
        context.UserId.Should().Be("user-1");
        context.PrimaryOrganizationUnitId.Should().Be("dept-1");
        context.OrganizationUnitIds.Should().BeEquivalentTo(["dept-1", "dept-2"]);
        context.RoleIds.Should().BeEquivalentTo(["admin", "user"]);
        context.PositionIds.Should().BeEquivalentTo(["pos-1", "pos-2"]);
    }

    [Fact]
    public async Task GetContext_DeduplicatesIds()
    {
        var memberships = new List<UserOrganizationMembership>
        {
            new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1", IsActive = true, PositionId = "pos-1" },
            new() { Id = "m-2", UserId = "user-1", OrganizationUnitId = "dept-1", IsActive = true, PositionId = "pos-1" },
        };
        var service = await CreateServiceAsync(memberships);
        var context = await service.GetContextAsync("user-1");
        context.OrganizationUnitIds.Should().HaveCount(1);
        context.PositionIds.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetContext_PrimaryUnitIsNull_WhenNoPrimary()
    {
        var memberships = new List<UserOrganizationMembership> { new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1", IsPrimary = false, IsActive = true } };
        var service = await CreateServiceAsync(memberships);
        var context = await service.GetContextAsync("user-1");
        context.PrimaryOrganizationUnitId.Should().BeNull();
    }

    [Fact]
    public async Task GetContext_ExcludesInactiveMemberships()
    {
        var memberships = new List<UserOrganizationMembership>
        {
            new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1", IsActive = true },
            new() { Id = "m-2", UserId = "user-1", OrganizationUnitId = "dept-2", IsActive = false },
        };
        var service = await CreateServiceAsync(memberships);
        var context = await service.GetContextAsync("user-1");
        context.OrganizationUnitIds.Should().BeEquivalentTo(["dept-1"]);
    }

    [Fact]
    public async Task IsInRole_ReturnsTrue_WhenActiveAssignment()
    {
        var roleAssignments = new List<UserOrganizationRoleAssignment> { new() { Id = "ra-1", UserId = "user-1", RoleId = "admin", IsActive = true } };
        var service = await CreateServiceAsync(roleAssignments: roleAssignments);
        (await service.IsInRoleAsync("user-1", "admin")).Should().BeTrue();
    }

    [Fact]
    public async Task IsInRole_ReturnsFalse_WhenInactiveAssignment()
    {
        var roleAssignments = new List<UserOrganizationRoleAssignment> { new() { Id = "ra-1", UserId = "user-1", RoleId = "admin", IsActive = false } };
        var service = await CreateServiceAsync(roleAssignments: roleAssignments);
        (await service.IsInRoleAsync("user-1", "admin")).Should().BeFalse();
    }

    [Fact]
    public async Task IsInRole_ReturnsFalse_WhenNoAssignment()
    {
        var service = await CreateServiceAsync();
        (await service.IsInRoleAsync("user-1", "admin")).Should().BeFalse();
    }

    [Fact]
    public async Task HasPosition_ReturnsTrue_WhenActiveMembership()
    {
        var memberships = new List<UserOrganizationMembership> { new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1", PositionId = "pos-1", IsActive = true } };
        var service = await CreateServiceAsync(memberships);
        (await service.HasPositionAsync("user-1", "pos-1")).Should().BeTrue();
    }

    [Fact]
    public async Task HasPosition_ReturnsFalse_WhenInactiveMembership()
    {
        var memberships = new List<UserOrganizationMembership> { new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1", PositionId = "pos-1", IsActive = false } };
        var service = await CreateServiceAsync(memberships);
        (await service.HasPositionAsync("user-1", "pos-1")).Should().BeFalse();
    }

    [Fact]
    public async Task HasPosition_ReturnsFalse_WhenNoPosition()
    {
        var memberships = new List<UserOrganizationMembership> { new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1", IsActive = true } };
        var service = await CreateServiceAsync(memberships);
        (await service.HasPositionAsync("user-1", "pos-unknown")).Should().BeFalse();
    }

    [Fact]
    public async Task GetUserOrganizationUnitIds_ReturnsDistinctActive()
    {
        var memberships = new List<UserOrganizationMembership>
        {
            new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1", IsActive = true },
            new() { Id = "m-2", UserId = "user-1", OrganizationUnitId = "dept-1", IsActive = true },
            new() { Id = "m-3", UserId = "user-1", OrganizationUnitId = "dept-2", IsActive = false },
        };
        var service = await CreateServiceAsync(memberships);
        var ids = await service.GetUserOrganizationUnitIdsAsync("user-1");
        ids.Should().BeEquivalentTo(["dept-1"]);
    }

    [Fact]
    public async Task GetUserRoleIds_ReturnsDistinctActive()
    {
        var roleAssignments = new List<UserOrganizationRoleAssignment>
        {
            new() { Id = "ra-1", UserId = "user-1", RoleId = "admin", IsActive = true },
            new() { Id = "ra-2", UserId = "user-1", RoleId = "admin", IsActive = true },
            new() { Id = "ra-3", UserId = "user-1", RoleId = "user", IsActive = false },
        };
        var service = await CreateServiceAsync(roleAssignments: roleAssignments);
        var ids = await service.GetUserRoleIdsAsync("user-1");
        ids.Should().BeEquivalentTo(["admin"]);
    }

    [Fact]
    public async Task GetUserPositionIds_ReturnsDistinctActive()
    {
        var memberships = new List<UserOrganizationMembership>
        {
            new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1", PositionId = "pos-1", IsActive = true },
            new() { Id = "m-2", UserId = "user-1", OrganizationUnitId = "dept-2", PositionId = "pos-1", IsActive = true },
            new() { Id = "m-3", UserId = "user-1", OrganizationUnitId = "dept-3", PositionId = "pos-2", IsActive = false },
        };
        var service = await CreateServiceAsync(memberships);
        var ids = await service.GetUserPositionIdsAsync("user-1");
        ids.Should().BeEquivalentTo(["pos-1"]);
    }
}
