using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Scheduling.Jobs;
using CrestCreates.Scheduling.Services;

namespace CrestCreates.Scheduling.Tests.Jobs;

public class InMemoryJobHistoryRepository : IJobHistoryRepository
{
    private readonly List<JobRecord> _records = new();
    private readonly object _lock = new();

    public Task<IJobRecord> CreateAsync(IJobRecord record, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var newRecord = new JobRecord
            {
                Id = Guid.NewGuid(),
                JobName = record.JobName,
                JobGroup = record.JobGroup,
                JobUuid = record.JobUuid,
                CronExpression = record.CronExpression,
                Result = record.Result,
                CreatedAt = DateTimeOffset.UtcNow,
                StartedAt = record.StartedAt,
                FinishedAt = record.FinishedAt,
                TenantId = record.TenantId,
                OrganizationId = record.OrganizationId,
                UserId = record.UserId,
                ArgsJson = record.ArgsJson,
                AttemptNumber = record.AttemptNumber,
                ErrorMessage = record.ErrorMessage,
                StackTrace = record.StackTrace
            };
            _records.Add(newRecord);
            return Task.FromResult<IJobRecord>(newRecord);
        }
    }

    public Task UpdateAsync(IJobRecord record, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var existing = _records.FirstOrDefault(r => r.Id == record.Id);
            if (existing != null)
            {
                _records.Remove(existing);
                _records.Add((JobRecord)record);
            }
        }
        return Task.CompletedTask;
    }

    public Task<IJobRecord?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IJobRecord?>(_records.FirstOrDefault(r => r.Id == id));
        }
    }

    public Task<IEnumerable<IJobRecord>> GetByJobIdAsync(Guid jobUuid, int limit = 100, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IEnumerable<IJobRecord>>(_records.Where(r => r.JobUuid == jobUuid).Take(limit).ToList());
        }
    }

    public Task<IEnumerable<IJobRecord>> GetByTenantAsync(Guid tenantId, int limit = 100, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IEnumerable<IJobRecord>>(_records.Where(r => r.TenantId == tenantId).Take(limit).ToList());
        }
    }

    public List<JobRecord> GetAllRecords() => _records.ToList();

    public void Clear()
    {
        lock (_lock)
        {
            _records.Clear();
        }
    }
}
