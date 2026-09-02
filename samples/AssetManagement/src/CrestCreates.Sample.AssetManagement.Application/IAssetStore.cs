using CrestCreates.Sample.AssetManagement.Domain.Entities;

namespace CrestCreates.Sample.AssetManagement.Application;

public interface IAssetStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Asset asset, CancellationToken cancellationToken = default);
    Task<Asset?> GetAsync(string tenantId, Guid assetId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Asset>> ListAsync(string tenantId, CancellationToken cancellationToken = default);
    Task UpdateAsync(Asset asset, string expectedConcurrencyStamp, CancellationToken cancellationToken = default);
    Task SaveAssignmentAsync(Asset asset, string expectedConcurrencyStamp, AssetAssignment assignment, CancellationToken cancellationToken = default);
    Task SaveReturnAsync(Asset asset, string expectedConcurrencyStamp, Guid assignmentId, CancellationToken cancellationToken = default);
    Task SaveMaintenanceDecisionAsync(Asset asset, string expectedConcurrencyStamp, MaintenanceRecord record, CancellationToken cancellationToken = default);
}

public interface IAssetMaintenanceWorkflowStarter
{
    Task<AssetMaintenanceWorkflowLease> StartAsync(Guid assetId, string tenantId, string requesterId, CancellationToken cancellationToken = default);
    Task AbortAsync(AssetMaintenanceWorkflowLease lease, string reason, CancellationToken cancellationToken = default);
}

public sealed record AssetMaintenanceWorkflowLease(string WorkflowInstanceId, string HumanTaskId);
