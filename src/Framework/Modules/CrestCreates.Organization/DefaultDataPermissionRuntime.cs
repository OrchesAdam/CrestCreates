using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization;

public sealed class DefaultDataPermissionRuntime : IDataPermissionRuntime
{
    private readonly IDataPermissionScopeProvider _scopeProvider;
    private readonly IDataPermissionFilterBuilder _filterBuilder;

    public DefaultDataPermissionRuntime(
        IDataPermissionScopeProvider scopeProvider,
        IDataPermissionFilterBuilder filterBuilder)
    {
        _scopeProvider = scopeProvider;
        _filterBuilder = filterBuilder;
    }

    public Task<DataPermissionScope> ResolveScopeAsync(
        DataPermissionScopeRequest request,
        CancellationToken cancellationToken = default)
        => _scopeProvider.GetScopeAsync(request, cancellationToken);

    public DataPermissionFilter BuildFilter(
        DataPermissionScope scope,
        DataPermissionFieldMapping mapping)
        => _filterBuilder.Build(scope, mapping);
}
