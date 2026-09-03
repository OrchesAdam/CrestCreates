using CrestCreates.Authorization.Abstractions;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Domain.Shared.DataFilter;
using CrestCreates.Domain.Shared.Enums;
using CrestCreates.Sample.AssetManagement.Contracts;
using CrestCreates.Sample.AssetManagement.Contracts.Dtos;
using CrestCreates.Sample.AssetManagement.Domain;
using CrestCreates.Sample.AssetManagement.Domain.Entities;

namespace CrestCreates.Sample.AssetManagement.Application;

public sealed class AssetApplicationService
{
    private readonly IAssetStore _store;
    private readonly ICurrentUser _currentUser;
    private readonly IDataPermissionFilter _dataPermissionFilter;
    private readonly IPermissionChecker _permissionChecker;
    private readonly IAssetMaintenanceWorkflowStarter _workflowStarter;

    public AssetApplicationService(IAssetStore store, ICurrentUser currentUser, IDataPermissionFilter dataPermissionFilter, IPermissionChecker permissionChecker, IAssetMaintenanceWorkflowStarter workflowStarter)
    {
        _store = store;
        _currentUser = currentUser;
        _dataPermissionFilter = dataPermissionFilter;
        _permissionChecker = permissionChecker;
        _workflowStarter = workflowStarter;
    }

    public async Task<AssetResult> RegisterAsync(RegisterAssetInput input, string tenantId, string userId, CancellationToken ct)
    {
        await CheckAsync(AssetPermissions.Assets.Register, ct);
        RequireContext(tenantId, userId);
        EnsureOrganizationAllowed(input.OrganizationId);
        var asset = new Asset(Guid.NewGuid(), tenantId, input.AssetTag, input.Name, input.Description, input.Category, input.OrganizationId, input.Location, userId);
        await _store.AddAsync(asset, ct);
        return Map(asset);
    }

    public async Task<AssetResult> GetAsync(Guid assetId, string tenantId, CancellationToken ct)
    {
        await CheckAsync(AssetPermissions.Assets.Read, ct);
        return Map(await RequireVisibleAsync(assetId, tenantId, ct));
    }

    public async Task<IReadOnlyList<AssetResult>> QueryAsync(AssetQueryInput input, string tenantId, CancellationToken ct)
    {
        await CheckAsync(AssetPermissions.Assets.Search, ct);
        var assets = await _store.ListAsync(tenantId, ct);
        var query = await ApplyAssetDataPermissionAsync(assets.AsQueryable());
        if (!string.IsNullOrWhiteSpace(input.Search))
            query = query.Where(asset => asset.AssetTag.Contains(input.Search, StringComparison.OrdinalIgnoreCase)
                || asset.Name.Contains(input.Search, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(input.Status)
            && Enum.TryParse<AssetStatus>(input.Status, true, out var status))
            query = query.Where(asset => asset.Status == status);
        if (input.OrganizationId is Guid organizationId)
            query = query.Where(asset => asset.OrganizationId == organizationId);
        return query.OrderBy(asset => asset.AssetTag, StringComparer.Ordinal)
            .ThenBy(asset => asset.Id)
            .Select(Map)
            .ToArray();
    }

    public async Task<AssetResult> UpdateAsync(Guid assetId, UpdateAssetInput input, string tenantId, string userId, CancellationToken ct)
    {
        await CheckAsync(AssetPermissions.Assets.Update, ct);
        var asset = await RequireVisibleAsync(assetId, tenantId, ct);
        var expected = asset.ConcurrencyStamp;
        asset.UpdateDetails(input.Name, input.Description, input.Category, input.Location, userId);
        await _store.UpdateAsync(asset, expected, ct);
        return Map(asset);
    }

    public async Task<AssetResult> AssignAsync(Guid assetId, AssignAssetInput input, string tenantId, string userId, CancellationToken ct)
    {
        await CheckAsync(AssetPermissions.Assets.Assign, ct);
        var asset = await RequireVisibleAsync(assetId, tenantId, ct);
        EnsureOrganizationAllowed(input.OrganizationId);
        var expected = asset.ConcurrencyStamp;
        var assignment = asset.Assign(input.UserId, input.OrganizationId, userId);
        await _store.SaveAssignmentAsync(asset, expected, assignment, ct);
        return Map(asset);
    }

    public async Task<AssetResult> ReturnAsync(Guid assetId, string tenantId, string userId, CancellationToken ct)
    {
        await CheckAsync(AssetPermissions.Assets.Return, ct);
        var asset = await RequireVisibleAsync(assetId, tenantId, ct);
        if (asset.Status == AssetStatus.Available)
            return Map(asset);
        var expected = asset.ConcurrencyStamp;
        var assignmentId = asset.ActiveAssignmentId;
        asset.Return(userId);
        if (assignmentId is Guid id)
            await _store.SaveReturnAsync(asset, expected, id, ct);
        else
            await _store.UpdateAsync(asset, expected, ct);
        return Map(asset);
    }

    public async Task<AssetResult> TransferAsync(Guid assetId, TransferAssetInput input, string tenantId, string userId, CancellationToken ct)
    {
        await CheckAsync(AssetPermissions.Assets.Transfer, ct);
        var asset = await RequireVisibleAsync(assetId, tenantId, ct);
        if (asset.Status == AssetStatus.Assigned)
            throw new CapabilityFailureException("CAPABILITY_DECISION_CONFLICT", "An assigned asset must be returned before it can be transferred.");
        EnsureOrganizationAllowed(input.OrganizationId);
        var expected = asset.ConcurrencyStamp;
        asset.Transfer(input.OrganizationId, input.Location, userId);
        await _store.UpdateAsync(asset, expected, ct);
        return Map(asset);
    }

    public async Task<AssetOperationResult> RequestMaintenanceAsync(Guid assetId, MaintenanceRequestInput input, string tenantId, string userId, CancellationToken ct)
    {
        await CheckAsync(AssetPermissions.Assets.RequestMaintenance, ct);
        if (string.IsNullOrWhiteSpace(input.Reason))
            throw new ArgumentException("Maintenance reason is required.", nameof(input));
        var asset = await RequireVisibleAsync(assetId, tenantId, ct);
        if (asset.Status is AssetStatus.Retired or AssetStatus.MaintenancePending)
            throw new InvalidOperationException($"Asset '{asset.AssetTag}' cannot enter maintenance from status '{asset.Status}'.");
        var workflow = await _workflowStarter.StartAsync(assetId, tenantId, userId, ct);
        try
        {
            var expected = asset.ConcurrencyStamp;
            asset.RequestMaintenance(workflow.WorkflowInstanceId, userId);
            await _store.UpdateAsync(asset, expected, ct);
        }
        catch
        {
            // SQLite business data and the PostgreSQL Runtime provider are
            // separate durable authorities. Close the Runtime wait if the
            // business transition fails after workflow suspension.
            await _workflowStarter.AbortAsync(workflow, "Asset maintenance business transition failed.", CancellationToken.None);
            throw;
        }
        return new AssetOperationResult { AssetId = assetId, Status = asset.Status.ToString(), WorkflowInstanceId = workflow.WorkflowInstanceId, HumanTaskId = workflow.HumanTaskId };
    }

    public async Task<AssetResult> ApplyMaintenanceDecisionAsync(Guid assetId, MaintenanceDecisionInput input, string tenantId, string userId, string requesterId, string workflowInstanceId, CancellationToken ct)
    {
        await CheckAsync(AssetPermissions.Assets.CompleteMaintenance, ct);
        if (string.IsNullOrWhiteSpace(requesterId))
            throw new CapabilityFailureException("CAPABILITY_CONTEXT_REQUIRED", "The maintenance requester is required.");
        var asset = await RequireVisibleAsync(assetId, tenantId, ct);
        var expected = asset.ConcurrencyStamp;
        asset.ApplyMaintenanceDecision(input.Approved, workflowInstanceId, userId);
        await _store.SaveMaintenanceDecisionAsync(asset, expected,
            new MaintenanceRecord(Guid.NewGuid(), tenantId, asset.Id, asset.OrganizationId, workflowInstanceId, requesterId, userId, input.Note, input.Approved), ct);
        return Map(asset);
    }

    private async Task<Asset?> VisibleAsync(Guid assetId, string tenantId, CancellationToken ct)
    {
        RequireContext(tenantId, null);
        var asset = await _store.GetAsync(tenantId, assetId, ct);
        if (asset is null)
            return null;
        var query = await ApplyAssetDataPermissionAsync(new[] { asset }.AsQueryable());
        return query.SingleOrDefault();
    }

    private Task<IQueryable<Asset>> ApplyAssetDataPermissionAsync(IQueryable<Asset> query)
    {
        var scope = (DataScope)_currentUser.DataScopeValue;
        if (scope == DataScope.Organization && !_currentUser.OrganizationId.HasValue)
        {
            // The legacy Organization filter reads the singular organization
            // identity. A plural identity alone must never widen this scope
            // to the whole tenant.
            return Task.FromResult<IQueryable<Asset>>(Array.Empty<Asset>().AsQueryable());
        }

        if (scope == DataScope.OrganizationAndSub && _currentUser.OrganizationIds.Count == 0)
        {
            // Asset visibility must remain fail-closed when the hierarchy
            // roots are absent.
            return Task.FromResult<IQueryable<Asset>>(Array.Empty<Asset>().AsQueryable());
        }

        return _dataPermissionFilter.ApplyFilterAsync(query);
    }

    private async Task<Asset> RequireVisibleAsync(Guid assetId, string tenantId, CancellationToken ct)
        => await VisibleAsync(assetId, tenantId, ct)
            ?? throw new CapabilityFailureException("CAPABILITY_RESOURCE_NOT_FOUND", $"Asset '{assetId}' is unavailable in the current data scope.");

    private async Task CheckAsync(string permission, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            await _permissionChecker.CheckAsync(permission);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new CapabilityFailureException("CAPABILITY_FORBIDDEN", exception.Message);
        }
    }

    private void EnsureOrganizationAllowed(Guid? organizationId)
    {
        if (organizationId is null)
            return;
        if ((DataScope)_currentUser.DataScopeValue is DataScope.Tenant or DataScope.All)
            return;
        if (!_currentUser.IsInOrganization(organizationId.Value))
            throw new CapabilityFailureException("CAPABILITY_FORBIDDEN", "The requested organization is outside the caller's data permission scope.");
    }

    private static void RequireContext(string tenantId, string? userId)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || userId is not null && string.IsNullOrWhiteSpace(userId))
            throw new CapabilityFailureException("CAPABILITY_CONTEXT_REQUIRED", "A trusted tenant and user context is required.");
    }

    private static AssetResult Map(Asset asset) => new()
    {
        Id = asset.Id,
        TenantId = asset.TenantId,
        OrganizationId = asset.OrganizationId,
        AssetTag = asset.AssetTag,
        Name = asset.Name,
        Description = asset.Description,
        Category = asset.Category,
        Location = asset.Location,
        Status = asset.Status.ToString(),
        AssignedUserId = asset.AssignedUserId,
        ActiveAssignmentId = asset.ActiveAssignmentId,
        MaintenanceWorkflowInstanceId = asset.MaintenanceWorkflowInstanceId,
        ConcurrencyStamp = asset.ConcurrencyStamp
    };
}
