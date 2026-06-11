using CrestCreates.Organization;
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization.Tests;

public class DataPermissionFilterBuilderTests
{
    private static readonly DefaultDataPermissionFilterBuilder _builder = new();

    [Fact]
    public void Build_NoneScope_ReturnsDenied()
    {
        var scope = new DataPermissionScope { Kind = DataPermissionScopeKind.None };
        var mapping = new DataPermissionFieldMapping();
        var filter = _builder.Build(scope, mapping);
        filter.IsDenied.Should().BeTrue();
        filter.IsUnrestricted.Should().BeFalse();
        filter.Rules.Should().BeEmpty();
    }

    [Fact]
    public void Build_AllScope_ReturnsUnrestricted()
    {
        var scope = new DataPermissionScope { Kind = DataPermissionScopeKind.All };
        var mapping = new DataPermissionFieldMapping();
        var filter = _builder.Build(scope, mapping);
        filter.IsUnrestricted.Should().BeTrue();
        filter.IsDenied.Should().BeFalse();
        filter.Rules.Should().BeEmpty();
    }

    [Fact]
    public void Build_SelfScope_WithUserIdField_ReturnsEqualRule()
    {
        var scope = new DataPermissionScope { Kind = DataPermissionScopeKind.Self, UserId = "user-1" };
        var mapping = new DataPermissionFieldMapping { UserIdField = "CreatorId" };
        var filter = _builder.Build(scope, mapping);
        filter.IsDenied.Should().BeFalse();
        filter.IsUnrestricted.Should().BeFalse();
        filter.Rules.Should().HaveCount(1);
        filter.Rules[0].FieldName.Should().Be("CreatorId");
        filter.Rules[0].Operator.Should().Be(DataPermissionFilterOperator.Equal);
        filter.Rules[0].Value.Should().Be("user-1");
    }

    [Fact]
    public void Build_SelfScope_WithoutUserIdField_ReturnsDenied()
    {
        var scope = new DataPermissionScope { Kind = DataPermissionScopeKind.Self, UserId = "user-1" };
        var mapping = new DataPermissionFieldMapping();
        var filter = _builder.Build(scope, mapping);
        filter.IsDenied.Should().BeTrue();
    }

    [Fact]
    public void Build_OwnOrganization_ReturnsEqualRule()
    {
        var scope = new DataPermissionScope
        {
            Kind = DataPermissionScopeKind.OwnOrganization,
            OrganizationUnitId = "dept-1"
        };
        var mapping = new DataPermissionFieldMapping { OrganizationUnitIdField = "OrgUnitId" };
        var filter = _builder.Build(scope, mapping);
        filter.IsDenied.Should().BeFalse();
        filter.Rules.Should().HaveCount(1);
        filter.Rules[0].FieldName.Should().Be("OrgUnitId");
        filter.Rules[0].Operator.Should().Be(DataPermissionFilterOperator.Equal);
        filter.Rules[0].Value.Should().Be("dept-1");
    }

    [Fact]
    public void Build_OwnOrganization_WithoutOrgField_ReturnsDenied()
    {
        var scope = new DataPermissionScope
        {
            Kind = DataPermissionScopeKind.OwnOrganization,
            OrganizationUnitId = "dept-1"
        };
        var mapping = new DataPermissionFieldMapping();
        var filter = _builder.Build(scope, mapping);
        filter.IsDenied.Should().BeTrue();
    }

    [Fact]
    public void Build_OwnOrganizationAndDescendants_ReturnsInRule()
    {
        var scope = new DataPermissionScope
        {
            Kind = DataPermissionScopeKind.OwnOrganizationAndDescendants,
            OrganizationUnitIds = new[] { "dept-1", "team-3", "team-4" }
        };
        var mapping = new DataPermissionFieldMapping { OrganizationUnitIdField = "OrgUnitId" };
        var filter = _builder.Build(scope, mapping);
        filter.IsDenied.Should().BeFalse();
        filter.Rules.Should().HaveCount(1);
        filter.Rules[0].FieldName.Should().Be("OrgUnitId");
        filter.Rules[0].Operator.Should().Be(DataPermissionFilterOperator.In);
        filter.Rules[0].Values.Should().BeEquivalentTo(new[] { "dept-1", "team-3", "team-4" });
    }

    [Fact]
    public void Build_OwnOrganizationAndDescendants_WithoutOrgField_ReturnsDenied()
    {
        var scope = new DataPermissionScope
        {
            Kind = DataPermissionScopeKind.OwnOrganizationAndDescendants,
            OrganizationUnitIds = new[] { "dept-1" }
        };
        var mapping = new DataPermissionFieldMapping();
        var filter = _builder.Build(scope, mapping);
        filter.IsDenied.Should().BeTrue();
    }

    [Fact]
    public void Build_WithTenantIdField_AppendsTenantRule()
    {
        var scope = new DataPermissionScope
        {
            Kind = DataPermissionScopeKind.OwnOrganization,
            OrganizationUnitId = "dept-1",
            TenantId = "tenant-A"
        };
        var mapping = new DataPermissionFieldMapping
        {
            OrganizationUnitIdField = "OrgUnitId",
            TenantIdField = "TenantId"
        };
        var filter = _builder.Build(scope, mapping);
        filter.IsDenied.Should().BeFalse();
        filter.Rules.Should().HaveCount(2);
        filter.Rules[1].FieldName.Should().Be("TenantId");
        filter.Rules[1].Operator.Should().Be(DataPermissionFilterOperator.Equal);
        filter.Rules[1].Value.Should().Be("tenant-A");
    }

    [Fact]
    public void Build_WithTenantIdField_ButNullTenantId_SkipsTenantRule()
    {
        var scope = new DataPermissionScope
        {
            Kind = DataPermissionScopeKind.OwnOrganization,
            OrganizationUnitId = "dept-1",
            TenantId = null
        };
        var mapping = new DataPermissionFieldMapping
        {
            OrganizationUnitIdField = "OrgUnitId",
            TenantIdField = "TenantId"
        };
        var filter = _builder.Build(scope, mapping);
        filter.Rules.Should().HaveCount(1);
    }

    [Fact]
    public void Build_WithoutTenantIdField_SkipsTenantRule()
    {
        var scope = new DataPermissionScope
        {
            Kind = DataPermissionScopeKind.OwnOrganization,
            OrganizationUnitId = "dept-1",
            TenantId = "tenant-A"
        };
        var mapping = new DataPermissionFieldMapping
        {
            OrganizationUnitIdField = "OrgUnitId"
        };
        var filter = _builder.Build(scope, mapping);
        filter.Rules.Should().HaveCount(1);
    }

    [Fact]
    public void Build_AllScope_WithTenantId_ReturnsTenantScoped()
    {
        var scope = new DataPermissionScope
        {
            Kind = DataPermissionScopeKind.All,
            TenantId = "tenant-A"
        };
        var mapping = new DataPermissionFieldMapping { TenantIdField = "TenantId" };
        var filter = _builder.Build(scope, mapping);

        filter.IsUnrestricted.Should().BeFalse();
        filter.IsDenied.Should().BeFalse();
        filter.Rules.Should().HaveCount(1);
        filter.Rules[0].FieldName.Should().Be("TenantId");
        filter.Rules[0].Operator.Should().Be(DataPermissionFilterOperator.Equal);
        filter.Rules[0].Value.Should().Be("tenant-A");
    }

    [Fact]
    public void Build_CustomScope_ReturnsDenied()
    {
        var scope = new DataPermissionScope { Kind = DataPermissionScopeKind.Custom };
        var mapping = new DataPermissionFieldMapping();
        var filter = _builder.Build(scope, mapping);
        filter.IsDenied.Should().BeTrue();
    }
}
