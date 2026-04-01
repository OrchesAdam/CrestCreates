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
            string userId = null,
            string action = null,
            DateTime? startTime = null,
            DateTime? endTime = null,
            int skip = 0,
            int take = 100
        )
        {
            var allLogs = await _auditLogRepository.GetAllAsync();
            var filteredLogs = allLogs.AsQueryable();

            if (!string.IsNullOrEmpty(userId))
            {
                filteredLogs = filteredLogs.Where(log => log.UserId == userId);
            }

            if (!string.IsNullOrEmpty(action))
            {
                filteredLogs = filteredLogs.Where(log => log.Action == action);
            }

            if (startTime.HasValue)
            {
                filteredLogs = filteredLogs.Where(log => log.CreationTime >= startTime.Value);
            }

            if (endTime.HasValue)
            {
                filteredLogs = filteredLogs.Where(log => log.CreationTime <= endTime.Value);
            }

            return filteredLogs.OrderByDescending(log => log.CreationTime)
                .Skip(skip)
                .Take(take)
                .ToList();
        }

        public async Task<long> GetCountAsync(
            string userId = null,
            string action = null,
            DateTime? startTime = null,
            DateTime? endTime = null
        )
        {
            var allLogs = await _auditLogRepository.GetAllAsync();
            var filteredLogs = allLogs.AsQueryable();

            if (!string.IsNullOrEmpty(userId))
            {
                filteredLogs = filteredLogs.Where(log => log.UserId == userId);
            }

            if (!string.IsNullOrEmpty(action))
            {
                filteredLogs = filteredLogs.Where(log => log.Action == action);
            }

            if (startTime.HasValue)
            {
                filteredLogs = filteredLogs.Where(log => log.CreationTime >= startTime.Value);
            }

            if (endTime.HasValue)
            {
                filteredLogs = filteredLogs.Where(log => log.CreationTime <= endTime.Value);
            }

            return filteredLogs.Count();
        }

        public async Task DeleteAsync(DateTime olderThan)
        {
            var allLogs = await _auditLogRepository.GetAllAsync();
            var logsToDelete = allLogs.Where(log => log.CreationTime < olderThan).ToList();

            foreach (var log in logsToDelete)
            {
                await _auditLogRepository.DeleteAsync(log);
            }
        }
    }
}