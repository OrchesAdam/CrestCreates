namespace CrestCreates.Organization.Abstractions;

public interface IDataPermissionScopeProvider
{
    Task<DataPermissionScope> GetScopeAsync(
        DataPermissionScopeRequest request,
        CancellationToken cancellationToken = default);

    Task<DataPermissionScope> GetScopeAsync(
        string userId, string permission, string? tenantId = null,
        CancellationToken cancellationToken = default);
}
