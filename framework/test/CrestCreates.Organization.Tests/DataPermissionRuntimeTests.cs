using CrestCreates.Organization;
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization.Tests;

public class DataPermissionRuntimeTests
{
    [Fact]
    public async Task ResolveScopeAsync_DelegatesToScopeProvider()
    {
        var store = new InMemoryOrganizationStore();
        var identityService = new DefaultOrganizationIdentityService(store);
        var hierarchyService = new DefaultOrganizationHierarchyService(store);
        var ruleStore = new InMemoryDataPermissionScopeRuleStore();
        var scopeProvider = new DefaultDataPermissionScopeProvider(identityService, hierarchyService, ruleStore);
        var filterBuilder = new DefaultDataPermissionFilterBuilder();
        var runtime = new DefaultDataPermissionRuntime(scopeProvider, filterBuilder);

        var request = new DataPermissionScopeRequest { UserId = "user-1" };
        var scope = await runtime.ResolveScopeAsync(request);
        scope.Kind.Should().Be(DataPermissionScopeKind.Self);
    }

    [Fact]
    public void BuildFilter_DelegatesToFilterBuilder()
    {
        var scope = new DataPermissionScope { Kind = DataPermissionScopeKind.All };
        var mapping = new DataPermissionFieldMapping();

        var scopeProvider = new DefaultDataPermissionScopeProvider(
            new DefaultOrganizationIdentityService(new InMemoryOrganizationStore()),
            new DefaultOrganizationHierarchyService(new InMemoryOrganizationStore()),
            new InMemoryDataPermissionScopeRuleStore());
        var filterBuilder = new DefaultDataPermissionFilterBuilder();
        var runtime = new DefaultDataPermissionRuntime(scopeProvider, filterBuilder);

        var filter = runtime.BuildFilter(scope, mapping);
        filter.IsUnrestricted.Should().BeTrue();
    }

    [Fact]
    public async Task EndToEnd_ResolveThenBuild_ProducesExpectedFilter()
    {
        var store = new InMemoryOrganizationStore();
        await store.SaveMembershipAsync(new UserOrganizationMembership
        {
            Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1",
            IsPrimary = true, IsActive = true
        });

        var identityService = new DefaultOrganizationIdentityService(store);
        var hierarchyService = new DefaultOrganizationHierarchyService(store);
        var ruleStore = new InMemoryDataPermissionScopeRuleStore();
        var scopeProvider = new DefaultDataPermissionScopeProvider(identityService, hierarchyService, ruleStore);
        var filterBuilder = new DefaultDataPermissionFilterBuilder();
        var runtime = new DefaultDataPermissionRuntime(scopeProvider, filterBuilder);

        var request = new DataPermissionScopeRequest { UserId = "user-1" };
        var scope = await runtime.ResolveScopeAsync(request);
        scope.Kind.Should().Be(DataPermissionScopeKind.OwnOrganization);

        var mapping = new DataPermissionFieldMapping { OrganizationUnitIdField = "OrgId" };
        var filter = runtime.BuildFilter(scope, mapping);
        filter.Rules.Should().HaveCount(1);
        filter.Rules[0].FieldName.Should().Be("OrgId");
        filter.Rules[0].Operator.Should().Be(DataPermissionFilterOperator.Equal);
        filter.Rules[0].Value.Should().Be("dept-1");
    }
}
