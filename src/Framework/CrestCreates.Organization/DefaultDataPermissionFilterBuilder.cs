using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization;

public sealed class DefaultDataPermissionFilterBuilder : IDataPermissionFilterBuilder
{
    public DataPermissionFilter Build(DataPermissionScope scope, DataPermissionFieldMapping mapping)
    {
        return scope.Kind switch
        {
            DataPermissionScopeKind.None => new DataPermissionFilter { IsDenied = true },
            DataPermissionScopeKind.All => BuildAll(scope, mapping),
            DataPermissionScopeKind.Self => BuildSelf(scope, mapping),
            DataPermissionScopeKind.OwnOrganization => BuildOwnOrganization(scope, mapping),
            DataPermissionScopeKind.OwnOrganizationAndDescendants => BuildOwnOrganizationAndDescendants(scope, mapping),
            _ => new DataPermissionFilter { IsDenied = true }, // Custom / unknown → fail closed
        };
    }

    private static DataPermissionFilter BuildAll(DataPermissionScope scope, DataPermissionFieldMapping mapping)
    {
        if (mapping.HasTenantIdField && scope.TenantId is not null)
        {
            return new DataPermissionFilter
            {
                IsUnrestricted = false,
                Rules = new[]
                {
                    new DataPermissionFilterRule
                    {
                        FieldName = mapping.TenantIdField!,
                        Operator = DataPermissionFilterOperator.Equal,
                        Value = scope.TenantId
                    }
                }
            };
        }

        return new DataPermissionFilter { IsUnrestricted = true };
    }

    private static DataPermissionFilter BuildSelf(DataPermissionScope scope, DataPermissionFieldMapping mapping)
    {
        if (!mapping.HasUserIdField)
            return new DataPermissionFilter { IsDenied = true };

        var rules = new List<DataPermissionFilterRule>
        {
            new()
            {
                FieldName = mapping.UserIdField!,
                Operator = DataPermissionFilterOperator.Equal,
                Value = scope.UserId
            }
        };
        AppendTenantRule(rules, scope, mapping);
        return new DataPermissionFilter { Rules = rules };
    }

    private static DataPermissionFilter BuildOwnOrganization(DataPermissionScope scope, DataPermissionFieldMapping mapping)
    {
        if (!mapping.HasOrganizationUnitIdField || scope.OrganizationUnitId is null)
            return new DataPermissionFilter { IsDenied = true };

        var rules = new List<DataPermissionFilterRule>
        {
            new()
            {
                FieldName = mapping.OrganizationUnitIdField!,
                Operator = DataPermissionFilterOperator.Equal,
                Value = scope.OrganizationUnitId
            }
        };
        AppendTenantRule(rules, scope, mapping);
        return new DataPermissionFilter { Rules = rules };
    }

    private static DataPermissionFilter BuildOwnOrganizationAndDescendants(DataPermissionScope scope, DataPermissionFieldMapping mapping)
    {
        if (!mapping.HasOrganizationUnitIdField || scope.OrganizationUnitIds.Count == 0)
            return new DataPermissionFilter { IsDenied = true };

        var rules = new List<DataPermissionFilterRule>
        {
            new()
            {
                FieldName = mapping.OrganizationUnitIdField!,
                Operator = DataPermissionFilterOperator.In,
                Values = scope.OrganizationUnitIds
            }
        };
        AppendTenantRule(rules, scope, mapping);
        return new DataPermissionFilter { Rules = rules };
    }

    private static void AppendTenantRule(List<DataPermissionFilterRule> rules, DataPermissionScope scope, DataPermissionFieldMapping mapping)
    {
        if (mapping.HasTenantIdField && scope.TenantId is not null)
        {
            rules.Add(new DataPermissionFilterRule
            {
                FieldName = mapping.TenantIdField!,
                Operator = DataPermissionFilterOperator.Equal,
                Value = scope.TenantId
            });
        }
    }
}
