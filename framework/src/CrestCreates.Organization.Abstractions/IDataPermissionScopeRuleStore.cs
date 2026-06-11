namespace CrestCreates.Organization.Abstractions;

public interface IDataPermissionScopeRuleStore
{
    Task<DataPermissionScopeKind?> GetScopeKindAsync(
        string resource,
        string? action,
        string? permission,
        string? tenantId = null,
        CancellationToken cancellationToken = default);

    Task SaveRuleAsync(
        DataPermissionScopeRule rule,
        CancellationToken cancellationToken = default);
}
