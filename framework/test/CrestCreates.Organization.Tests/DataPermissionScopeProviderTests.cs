using CrestCreates.Organization;
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization.Tests;

public class DataPermissionScopeProviderTests
{
    [Fact]
    public async Task GetScope_ReturnsSelf_WhenNoOrganization()
    {
        var store = new InMemoryOrganizationStore();
        var identityService = new DefaultOrganizationIdentityService(store);
        var provider = new DefaultDataPermissionScopeProvider(identityService);
        var scope = await provider.GetScopeAsync("user-1", "read:documents");
        scope.Kind.Should().Be(DataPermissionScopeKind.Self);
        scope.UserId.Should().Be("user-1");
        scope.OrganizationUnitId.Should().BeNull();
    }

    [Fact]
    public async Task GetScope_ReturnsOwnOrganization_WhenPrimaryExists()
    {
        var store = new InMemoryOrganizationStore();
        await store.SaveMembershipAsync(new UserOrganizationMembership { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1", IsPrimary = true, IsActive = true });
        var identityService = new DefaultOrganizationIdentityService(store);
        var provider = new DefaultDataPermissionScopeProvider(identityService);
        var scope = await provider.GetScopeAsync("user-1", "read:documents");
        scope.Kind.Should().Be(DataPermissionScopeKind.OwnOrganization);
        scope.UserId.Should().Be("user-1");
        scope.OrganizationUnitId.Should().Be("dept-1");
    }
}
