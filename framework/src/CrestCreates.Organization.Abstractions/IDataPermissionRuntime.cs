namespace CrestCreates.Organization.Abstractions;

public interface IDataPermissionRuntime
{
    Task<DataPermissionScope> ResolveScopeAsync(
        DataPermissionScopeRequest request,
        CancellationToken cancellationToken = default);

    DataPermissionFilter BuildFilter(
        DataPermissionScope scope,
        DataPermissionFieldMapping mapping);
}
