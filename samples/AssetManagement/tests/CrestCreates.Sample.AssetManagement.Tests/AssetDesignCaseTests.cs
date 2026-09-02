using CrestCreates.Authorization.Abstractions;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Domain.Shared.DataFilter;
using CrestCreates.Domain.Shared.Enums;
using CrestCreates.Sample.AssetManagement.Application;
using CrestCreates.Sample.AssetManagement.Contracts.Dtos;
using CrestCreates.Sample.AssetManagement.Domain;
using CrestCreates.Sample.AssetManagement.Domain.Entities;

namespace CrestCreates.Sample.AssetManagement.Tests;

public sealed class AssetDesignCaseTests
{
    [Fact]
    public void AssignedMaintenance_ApproveAndReject_PreserveAssignmentInvariant()
    {
        var asset = CreateAsset();
        var organizationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var assignment = asset.Assign("user-1", organizationId, "manager-1");

        asset.RequestMaintenance("workflow-1", "manager-1");
        asset.Status.Should().Be(AssetStatus.MaintenancePending);
        asset.AssignedUserId.Should().Be("user-1");
        asset.ActiveAssignmentId.Should().Be(assignment.Id);
        asset.ApplyMaintenanceDecision(approved: true, "workflow-1", "manager-1");
        asset.Status.Should().Be(AssetStatus.Assigned);
        asset.AssignedUserId.Should().Be("user-1");
        asset.ActiveAssignmentId.Should().Be(assignment.Id);

        asset.RequestMaintenance("workflow-2", "manager-1");
        asset.ApplyMaintenanceDecision(approved: false, "workflow-2", "manager-1");
        asset.Status.Should().Be(AssetStatus.Assigned);
        asset.AssignedUserId.Should().Be("user-1");
        asset.ActiveAssignmentId.Should().Be(assignment.Id);
    }

    [Fact]
    public void AssignedTransfer_IsRejectedBeforeOwnershipCanDrift()
    {
        var asset = CreateAsset();
        asset.Assign("user-1", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "manager-1");

        var act = () => asset.Transfer(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "Shanghai", "manager-1");

        act.Should().Throw<InvalidOperationException>();
        asset.OrganizationId.Should().Be(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        asset.ActiveAssignmentId.Should().NotBeNull();
    }

    [Fact]
    public async Task ApplicationTransfer_RejectsAssignedAssetThroughCapabilityFailure()
    {
        var asset = CreateAsset();
        asset.Assign("user-1", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "manager-1");
        var service = new AssetApplicationService(
            new ThrowingAssetStore(asset),
            new TestCurrentUser(),
            new AllowAllDataPermissionFilter(),
            new AllowAllPermissionChecker(),
            new RecordingWorkflowStarter());

        var act = () => service.TransferAsync(
            asset.Id,
            new TransferAssetInput { AssetId = asset.Id, OrganizationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Location = "Shanghai" },
            asset.TenantId,
            "manager-1",
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CapabilityFailureException>();
        exception.Which.ErrorCode.Should().Be("CAPABILITY_DECISION_CONFLICT");
    }

    [Fact]
    public async Task BusinessStoreFailureAfterWorkflowStart_AbortsRuntimeLease()
    {
        var store = new ThrowingAssetStore(CreateAsset());
        var starter = new RecordingWorkflowStarter();
        var service = new AssetApplicationService(
            store,
            new TestCurrentUser(),
            new AllowAllDataPermissionFilter(),
            new AllowAllPermissionChecker(),
            starter);

        var act = () => service.RequestMaintenanceAsync(
            store.Asset.Id,
            new MaintenanceRequestInput { AssetId = store.Asset.Id, Reason = "Battery replacement" },
            store.Asset.TenantId,
            "requester-1",
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        starter.Aborted.Should().BeTrue();
        starter.AbortedLease!.WorkflowInstanceId.Should().Be("workflow-1");
    }

    [Fact]
    public async Task OrganizationScopeWithoutOrganizationIds_FailsClosed()
    {
        var currentUser = new TestCurrentUser
        {
            DataScopeValue = (int)DataScope.Organization,
            OrganizationIds = []
        };
        var asset = CreateAsset();
        var service = new AssetApplicationService(
            new ThrowingAssetStore(asset) { ThrowOnUpdate = false },
            currentUser,
            new AllowAllDataPermissionFilter(),
            new AllowAllPermissionChecker(),
            new RecordingWorkflowStarter());

        var result = await service.QueryAsync(new AssetQueryInput(), asset.TenantId, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task MaintenanceDecision_PersistsRequesterIdentity()
    {
        var asset = CreateAsset();
        asset.RequestMaintenance("workflow-1", "requester-1");
        var store = new ThrowingAssetStore(asset) { ThrowOnUpdate = false };
        var service = new AssetApplicationService(
            store,
            new TestCurrentUser(),
            new AllowAllDataPermissionFilter(),
            new AllowAllPermissionChecker(),
            new RecordingWorkflowStarter());

        await service.ApplyMaintenanceDecisionAsync(
            asset.Id,
            new MaintenanceDecisionInput { AssetId = asset.Id, Approved = true, Note = "Completed" },
            asset.TenantId,
            "manager-1",
            "requester-1",
            "workflow-1",
            CancellationToken.None);

        store.SavedMaintenanceRecord!.RequestedBy.Should().Be("requester-1");
    }

    private static Asset CreateAsset()
        => new(Guid.NewGuid(), "tenant-a", "LAPTOP-001", "Laptop", "Test asset", "Equipment", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "Shanghai", "manager-1");

    private sealed class RecordingWorkflowStarter : IAssetMaintenanceWorkflowStarter
    {
        public bool Aborted { get; private set; }
        public AssetMaintenanceWorkflowLease? AbortedLease { get; private set; }

        public Task<AssetMaintenanceWorkflowLease> StartAsync(Guid assetId, string tenantId, string requesterId, CancellationToken cancellationToken = default)
            => Task.FromResult(new AssetMaintenanceWorkflowLease("workflow-1", "task-1"));

        public Task AbortAsync(AssetMaintenanceWorkflowLease lease, string reason, CancellationToken cancellationToken = default)
        {
            Aborted = true;
            AbortedLease = lease;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingAssetStore(Asset asset) : IAssetStore
    {
        public Asset Asset { get; } = asset;
        public bool ThrowOnUpdate { get; init; } = true;
        public MaintenanceRecord? SavedMaintenanceRecord { get; private set; }
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AddAsync(Asset asset, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Asset?> GetAsync(string tenantId, Guid assetId, CancellationToken cancellationToken = default) => Task.FromResult<Asset?>(Asset);
        public Task<IReadOnlyList<Asset>> ListAsync(string tenantId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Asset>>([Asset]);
        public Task UpdateAsync(Asset asset, string expectedConcurrencyStamp, CancellationToken cancellationToken = default)
            => ThrowOnUpdate ? Task.FromException(new InvalidOperationException("business store unavailable")) : Task.CompletedTask;
        public Task SaveAssignmentAsync(Asset asset, string expectedConcurrencyStamp, AssetAssignment assignment, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveReturnAsync(Asset asset, string expectedConcurrencyStamp, Guid assignmentId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveMaintenanceDecisionAsync(Asset asset, string expectedConcurrencyStamp, MaintenanceRecord record, CancellationToken cancellationToken = default)
        {
            SavedMaintenanceRecord = record;
            return Task.CompletedTask;
        }
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public string Id => "requester-1";
        public string UserName => Id;
        public bool IsAuthenticated => true;
        public string TenantId => "tenant-a";
        public string[] Roles => ["asset-manager"];
        public Guid? OrganizationId => Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        public IReadOnlyList<Guid> OrganizationIds { get; init; } = [Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")];
        public int DataScopeValue { get; init; } = (int)DataScope.Tenant;
        public bool IsSuperAdmin => false;
        public string FindClaimValue(string claimType) => string.Empty;
        public string[] FindClaimValues(string claimType) => [];
        public bool IsInRole(string roleName) => roleName == "asset-manager";
        public bool IsInOrganization(Guid orgId) => OrganizationIds.Contains(orgId);
    }

    private sealed class AllowAllPermissionChecker : IPermissionChecker
    {
        public Task<bool> IsGrantedAsync(string permissionName) => Task.FromResult(true);
        public Task<bool> IsGrantedAsync(System.Security.Claims.ClaimsPrincipal principal, string permissionName) => Task.FromResult(true);
        public Task<MultiplePermissionGrantResult> IsGrantedAsync(string[] permissionNames) => Task.FromResult(new MultiplePermissionGrantResult(permissionNames.ToDictionary(name => name, _ => true)));
        public Task<MultiplePermissionGrantResult> IsGrantedAsync(System.Security.Claims.ClaimsPrincipal principal, string[] permissionNames) => IsGrantedAsync(permissionNames);
        public Task CheckAsync(string permissionName) => Task.CompletedTask;
    }

    private sealed class AllowAllDataPermissionFilter : IDataPermissionFilter
    {
        public Task<IQueryable<TEntity>> ApplyFilterAsync<TEntity>(IQueryable<TEntity> query) where TEntity : class => Task.FromResult(query);
        public Task<IQueryable<TEntity>> ApplyTenantFilterAsync<TEntity>(IQueryable<TEntity> query) where TEntity : class => Task.FromResult(query);
        public Task<IQueryable<TEntity>> ApplyOrganizationFilterAsync<TEntity>(IQueryable<TEntity> query) where TEntity : class => Task.FromResult(query);
    }
}
