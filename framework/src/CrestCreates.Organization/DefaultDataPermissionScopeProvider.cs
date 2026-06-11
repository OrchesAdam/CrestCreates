using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization;

public sealed class DefaultDataPermissionScopeProvider : IDataPermissionScopeProvider
{
    private readonly IOrganizationIdentityService _identityService;

    public DefaultDataPermissionScopeProvider(IOrganizationIdentityService identityService)
    {
        _identityService = identityService;
    }

    public async Task<DataPermissionScope> GetScopeAsync(string userId, string permission, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        var context = await _identityService.GetContextAsync(userId, tenantId, cancellationToken);

        if (context.PrimaryOrganizationUnitId is null)
        {
            return new DataPermissionScope { Kind = DataPermissionScopeKind.Self, UserId = userId };
        }

        return new DataPermissionScope
        {
            Kind = DataPermissionScopeKind.OwnOrganization,
            UserId = userId,
            OrganizationUnitId = context.PrimaryOrganizationUnitId,
            OrganizationUnitIds = context.OrganizationUnitIds
        };
    }
}
