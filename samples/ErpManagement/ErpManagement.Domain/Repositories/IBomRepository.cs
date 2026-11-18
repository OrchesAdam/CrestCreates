using CrestCreates.Domain.Repositories;
using ErpManagement.Domain.Entities;

namespace ErpManagement.Domain.Repositories;

public interface IBomRepository : ICrestRepositoryBase<Bom, Guid>
{
    Task<Bom?> GetWithLinesAsync(Guid id, CancellationToken ct = default);
    Task<List<Bom>> GetAllWithLinesAsync(CancellationToken ct = default);
    Task<List<Bom>> GetByMaterialWithLinesAsync(Guid materialId, CancellationToken ct = default);
}