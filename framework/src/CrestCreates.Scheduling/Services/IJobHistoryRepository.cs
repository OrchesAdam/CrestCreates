using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Scheduling.Jobs;

namespace CrestCreates.Scheduling.Services;

public interface IJobHistoryRepository
{
    Task<IJobRecord> CreateAsync(IJobRecord record, CancellationToken ct = default);
    Task UpdateAsync(IJobRecord record, CancellationToken ct = default);
    Task<IJobRecord?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IEnumerable<IJobRecord>> GetByJobIdAsync(Guid jobUuid, int limit = 100, CancellationToken ct = default);
    Task<IEnumerable<IJobRecord>> GetByTenantAsync(Guid tenantId, int limit = 100, CancellationToken ct = default);
}
