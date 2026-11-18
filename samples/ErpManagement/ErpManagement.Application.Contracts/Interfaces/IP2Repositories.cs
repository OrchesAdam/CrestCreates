using CrestCreates.Domain.Repositories;
using ErpManagement.Domain.Entities;

namespace ErpManagement.Application.Contracts.Interfaces;

public interface ISalesOrderRepository : ICrestRepositoryBase<SalesOrder, Guid>
{
    Task<SalesOrder?> GetWithLinesAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<List<SalesOrder>> GetAllWithLinesAsync(Guid tenantId, CancellationToken ct = default);
}

public interface ISalesShipmentRepository : ICrestRepositoryBase<SalesShipment, Guid>
{
    Task<SalesShipment?> GetWithLinesAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<List<SalesShipment>> GetAllWithLinesAsync(Guid tenantId, CancellationToken ct = default);
}

public interface IWorkOrderRepository : ICrestRepositoryBase<WorkOrder, Guid>
{
    Task<WorkOrder?> GetWithLinesAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<List<WorkOrder>> GetAllWithLinesAsync(Guid tenantId, CancellationToken ct = default);
}

public interface IArApLedgerRepository : ICrestRepositoryBase<ArApLedger, Guid>
{
    Task<ArApLedger?> GetWithLinesAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<List<ArApLedger>> GetAllWithLinesAsync(Guid tenantId, CancellationToken ct = default);
}
