using CrestCreates.Organization;
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization.Tests;

public class OrganizationHierarchyServiceTests
{
    private static async Task<DefaultOrganizationHierarchyService> CreateServiceAsync(
        List<OrganizationUnit> orgUnits,
        List<UserOrganizationMembership>? memberships = null)
    {
        var store = new InMemoryOrganizationStore();
        foreach (var unit in orgUnits)
            await store.SaveOrganizationUnitAsync(unit);
        if (memberships is not null)
            foreach (var m in memberships)
                await store.SaveMembershipAsync(m);
        return new DefaultOrganizationHierarchyService(store);
    }

    [Fact]
    public async Task GetAncestors_ReturnsParentChain()
    {
        var orgUnits = new List<OrganizationUnit>
        {
            new() { Id = "root", Name = "Root" },
            new() { Id = "dept", Name = "Department", ParentId = "root" },
            new() { Id = "team", Name = "Team", ParentId = "dept" },
        };
        var service = await CreateServiceAsync(orgUnits);
        var ancestors = await service.GetAncestorsAsync("team");
        ancestors.Should().HaveCount(2);
        ancestors[0].Id.Should().Be("dept");
        ancestors[1].Id.Should().Be("root");
    }

    [Fact]
    public async Task GetAncestors_ReturnsEmpty_WhenNoParent()
    {
        var orgUnits = new List<OrganizationUnit> { new() { Id = "root", Name = "Root" } };
        var service = await CreateServiceAsync(orgUnits);
        var ancestors = await service.GetAncestorsAsync("root");
        ancestors.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDescendants_ReturnsChildren()
    {
        var orgUnits = new List<OrganizationUnit>
        {
            new() { Id = "root", Name = "Root" },
            new() { Id = "dept1", Name = "Dept1", ParentId = "root" },
            new() { Id = "team1", Name = "Team1", ParentId = "dept1" },
            new() { Id = "dept2", Name = "Dept2", ParentId = "root" },
        };
        var service = await CreateServiceAsync(orgUnits);
        var descendants = await service.GetDescendantsAsync("root");
        descendants.Should().HaveCount(3);
        descendants.Select(d => d.Id).Should().Contain(["dept1", "team1", "dept2"]);
    }

    [Fact]
    public async Task GetDescendants_ReturnsEmpty_WhenLeafNode()
    {
        var orgUnits = new List<OrganizationUnit> { new() { Id = "root", Name = "Root" }, new() { Id = "leaf", Name = "Leaf", ParentId = "root" } };
        var service = await CreateServiceAsync(orgUnits);
        var descendants = await service.GetDescendantsAsync("leaf");
        descendants.Should().BeEmpty();
    }

    [Fact]
    public async Task IsDescendantOf_ReturnsTrue()
    {
        var orgUnits = new List<OrganizationUnit> { new() { Id = "root" }, new() { Id = "dept", ParentId = "root" }, new() { Id = "team", ParentId = "dept" } };
        var service = await CreateServiceAsync(orgUnits);
        (await service.IsDescendantOfAsync("team", "root")).Should().BeTrue();
    }

    [Fact]
    public async Task IsDescendantOf_ReturnsFalse_WhenNotDescendant()
    {
        var orgUnits = new List<OrganizationUnit> { new() { Id = "root" }, new() { Id = "dept", ParentId = "root" } };
        var service = await CreateServiceAsync(orgUnits);
        (await service.IsDescendantOfAsync("root", "dept")).Should().BeFalse();
    }

    [Fact]
    public async Task IsDescendantOf_ReturnsFalse_WhenSame()
    {
        var service = await CreateServiceAsync(new List<OrganizationUnit> { new() { Id = "root" } });
        (await service.IsDescendantOfAsync("root", "root")).Should().BeFalse();
    }

    [Fact]
    public async Task GetAncestors_DetectsCycle_ThrowsHierarchyException()
    {
        var orgUnits = new List<OrganizationUnit> { new() { Id = "a", ParentId = "c" }, new() { Id = "b", ParentId = "a" }, new() { Id = "c", ParentId = "b" } };
        var service = await CreateServiceAsync(orgUnits);
        await Assert.ThrowsAsync<OrganizationHierarchyException>(() => service.GetAncestorsAsync("a"));
    }

    [Fact]
    public async Task GetDescendants_DetectsCycle_ThrowsHierarchyException()
    {
        var orgUnits = new List<OrganizationUnit> { new() { Id = "a", ParentId = "c" }, new() { Id = "b", ParentId = "a" }, new() { Id = "c", ParentId = "b" } };
        var service = await CreateServiceAsync(orgUnits);
        await Assert.ThrowsAsync<OrganizationHierarchyException>(() => service.GetDescendantsAsync("a"));
    }

    [Fact]
    public async Task IsUserInOrganization_ReturnsTrue_WhenActiveMember()
    {
        var orgUnits = new List<OrganizationUnit> { new() { Id = "dept" } };
        var memberships = new List<UserOrganizationMembership> { new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept", IsActive = true } };
        var service = await CreateServiceAsync(orgUnits, memberships);
        (await service.IsUserInOrganizationAsync("user-1", "dept")).Should().BeTrue();
    }

    [Fact]
    public async Task IsUserInOrganization_ReturnsFalse_WhenInactiveMember()
    {
        var orgUnits = new List<OrganizationUnit> { new() { Id = "dept" } };
        var memberships = new List<UserOrganizationMembership> { new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept", IsActive = false } };
        var service = await CreateServiceAsync(orgUnits, memberships);
        (await service.IsUserInOrganizationAsync("user-1", "dept")).Should().BeFalse();
    }

    [Fact]
    public async Task IsUserInOrganization_ReturnsFalse_WhenNotMember()
    {
        var orgUnits = new List<OrganizationUnit> { new() { Id = "dept" } };
        var memberships = new List<UserOrganizationMembership> { new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "other", IsActive = true } };
        var service = await CreateServiceAsync(orgUnits, memberships);
        (await service.IsUserInOrganizationAsync("user-1", "dept")).Should().BeFalse();
    }

    [Fact]
    public async Task IsUserInDescendantOrganization_ReturnsTrue_WhenUserInDescendant()
    {
        var orgUnits = new List<OrganizationUnit> { new() { Id = "root" }, new() { Id = "dept", ParentId = "root" }, new() { Id = "team", ParentId = "dept" } };
        var memberships = new List<UserOrganizationMembership> { new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "team", IsActive = true } };
        var service = await CreateServiceAsync(orgUnits, memberships);
        (await service.IsUserInDescendantOrganizationAsync("user-1", "root")).Should().BeTrue();
    }

    [Fact]
    public async Task IsUserInDescendantOrganization_ReturnsFalse_WhenUserInUnrelatedOrg()
    {
        var orgUnits = new List<OrganizationUnit> { new() { Id = "root" }, new() { Id = "dept", ParentId = "root" }, new() { Id = "other" } };
        var memberships = new List<UserOrganizationMembership> { new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "other", IsActive = true } };
        var service = await CreateServiceAsync(orgUnits, memberships);
        (await service.IsUserInDescendantOrganizationAsync("user-1", "root")).Should().BeFalse();
    }

    [Fact]
    public async Task GetAncestors_IsolatesByTenant()
    {
        var orgUnits = new List<OrganizationUnit>
        {
            new() { Id = "dept", TenantId = "t1", ParentId = "root-t1" },
            new() { Id = "root-t1", TenantId = "t1" },
            new() { Id = "dept", TenantId = "t2", ParentId = "root-t2" },
            new() { Id = "root-t2", TenantId = "t2" },
        };
        var service = await CreateServiceAsync(orgUnits);
        var ancestors = await service.GetAncestorsAsync("dept", "t1");
        ancestors.Should().HaveCount(1);
        ancestors[0].Id.Should().Be("root-t1");
    }

    [Fact]
    public async Task GetAncestors_CrossTenantParent_Excluded()
    {
        var orgUnits = new List<OrganizationUnit> { new() { Id = "dept", TenantId = "t1", ParentId = "root-t2" }, new() { Id = "root-t2", TenantId = "t2" } };
        var service = await CreateServiceAsync(orgUnits);
        var ancestors = await service.GetAncestorsAsync("dept", "t1");
        ancestors.Should().BeEmpty();
    }
}
