using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization;

public sealed class DefaultDataPermissionScopeProvider : IDataPermissionScopeProvider
{
    private readonly IOrganizationIdentityService _identityService;
    private readonly IOrganizationHierarchyService _hierarchyService;
    private readonly IDataPermissionScopeRuleStore _ruleStore;

    public DefaultDataPermissionScopeProvider(
        IOrganizationIdentityService identityService,
        IOrganizationHierarchyService hierarchyService,
        IDataPermissionScopeRuleStore ruleStore)
    {
        _identityService = identityService;
        _hierarchyService = hierarchyService;
        _ruleStore = ruleStore;
    }

    public async Task<DataPermissionScope> GetScopeAsync(
        DataPermissionScopeRequest request,
        CancellationToken cancellationToken = default)
    {
        // Step 1: Check rule store for explicit scope kind
        if (request.Resource is not null)
        {
            var ruleKind = await _ruleStore.GetScopeKindAsync(
                request.Resource, request.Action, request.Permission, request.TenantId, cancellationToken);

            if (ruleKind is not null)
                return await ResolveByKindAsync(ruleKind.Value, request, cancellationToken);
        }

        // Step 2: Fall back to org-membership-based scope
        var context = await _identityService.GetContextAsync(
            request.UserId, request.TenantId, cancellationToken);

        if (context.PrimaryOrganizationUnitId is null)
        {
            return new DataPermissionScope
            {
                Kind = DataPermissionScopeKind.Self,
                UserId = request.UserId,
                TenantId = request.TenantId,
                Resource = request.Resource,
                Action = request.Action,
                Permission = request.Permission
            };
        }

        return new DataPermissionScope
        {
            Kind = DataPermissionScopeKind.OwnOrganization,
            UserId = request.UserId,
            TenantId = request.TenantId,
            Resource = request.Resource,
            Action = request.Action,
            Permission = request.Permission,
            OrganizationUnitId = context.PrimaryOrganizationUnitId,
            OrganizationUnitIds = context.OrganizationUnitIds
        };
    }

    // Old overload — adapter
    public Task<DataPermissionScope> GetScopeAsync(
        string userId,
        string permission,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
        => GetScopeAsync(new DataPermissionScopeRequest
        {
            UserId = userId,
            Permission = permission,
            TenantId = tenantId
        }, cancellationToken);

    private async Task<DataPermissionScope> ResolveByKindAsync(
        DataPermissionScopeKind kind,
        DataPermissionScopeRequest request,
        CancellationToken ct)
    {
        // Self and All don't need identity context
        if (kind is DataPermissionScopeKind.Self or DataPermissionScopeKind.All)
        {
            return new DataPermissionScope
            {
                Kind = kind,
                UserId = request.UserId,
                TenantId = request.TenantId,
                Resource = request.Resource,
                Action = request.Action,
                Permission = request.Permission
            };
        }

        // None and Custom are always deny at provider level
        if (kind is DataPermissionScopeKind.None or DataPermissionScopeKind.Custom)
        {
            return new DataPermissionScope { Kind = DataPermissionScopeKind.None };
        }

        // OwnOrganization / OwnOrganizationAndDescendants need identity context
        var context = await _identityService.GetContextAsync(
            request.UserId, request.TenantId, ct);

        if (kind == DataPermissionScopeKind.OwnOrganization)
        {
            if (context.PrimaryOrganizationUnitId is null)
                return new DataPermissionScope { Kind = DataPermissionScopeKind.None };

            return new DataPermissionScope
            {
                Kind = kind,
                UserId = request.UserId,
                TenantId = request.TenantId,
                Resource = request.Resource,
                Action = request.Action,
                Permission = request.Permission,
                OrganizationUnitId = context.PrimaryOrganizationUnitId,
                OrganizationUnitIds = context.OrganizationUnitIds
            };
        }

        // OwnOrganizationAndDescendants
        if (context.PrimaryOrganizationUnitId is null)
            return new DataPermissionScope { Kind = DataPermissionScopeKind.None };

        var descendants = await _hierarchyService.GetDescendantsAsync(
            context.PrimaryOrganizationUnitId, request.TenantId, ct);

        var allIds = new List<string> { context.PrimaryOrganizationUnitId };
        allIds.AddRange(descendants.Select(d => d.Id));

        return new DataPermissionScope
        {
            Kind = kind,
            UserId = request.UserId,
            TenantId = request.TenantId,
            Resource = request.Resource,
            Action = request.Action,
            Permission = request.Permission,
            OrganizationUnitId = context.PrimaryOrganizationUnitId,
            OrganizationUnitIds = allIds
        };
    }
}
