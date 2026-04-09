using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.AuditLogging.Entities;
using CrestCreates.Domain.Repositories;

namespace CrestCreates.AuditLogging.Services
{
    public class AuditLogService : IAuditLogService
    {
        private readonly IRepository<AuditLog, Guid> _auditLogRepository;

        public AuditLogService(IRepository<AuditLog, Guid> auditLogRepository)
        {
            _auditLogRepository = auditLogRepository;
        }

        public async Task CreateAsync(AuditLog auditLog)
        {
            await _auditLogRepository.AddAsync(auditLog);
        }

        public async Task<IEnumerable<AuditLog>> GetListAsync(
            string? userId = null,
            string? action = null,
            DateTime? startTime = null,
            DateTime? endTime = null,
            int skip = 0,
            int take = 100
        )
        {
            var filteredLogs = await _auditLogRepository.FindAsync(log =>
                (string.IsNullOrEmpty(userId) || log.UserId == userId) &&
                (string.IsNullOrEmpty(action) || log.Action == action) &&
                (!startTime.HasValue || log.CreationTime >= startTime.Value) &&
                (!endTime.HasValue || log.CreationTime <= endTime.Value));

            return filteredLogs
                .OrderByDescending(log => log.CreationTime)
                .Skip(skip)
                .Take(take)
                .ToList();
        }

        public async Task<long> GetCountAsync(
            string? userId = null,
            string? action = null,
            DateTime? startTime = null,
            DateTime? endTime = null
        )
        {
            var filteredLogs = await _auditLogRepository.FindAsync(log =>
                (string.IsNullOrEmpty(userId) || log.UserId == userId) &&
                (string.IsNullOrEmpty(action) || log.Action == action) &&
                (!startTime.HasValue || log.CreationTime >= startTime.Value) &&
                (!endTime.HasValue || log.CreationTime <= endTime.Value));

            return filteredLogs.Count;
        }

        public async Task DeleteAsync(DateTime olderThan)
        {
            var logsToDelete = await _auditLogRepository.FindAsync(log => log.CreationTime < olderThan);

            foreach (var log in logsToDelete)
            {
                await _auditLogRepository.DeleteAsync(log);
            }
        }
    }
}
