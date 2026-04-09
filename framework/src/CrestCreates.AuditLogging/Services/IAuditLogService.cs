using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CrestCreates.AuditLogging.Entities;

namespace CrestCreates.AuditLogging.Services
{
    public interface IAuditLogService
    {
        Task CreateAsync(AuditLog auditLog);
        Task<IEnumerable<AuditLog>> GetListAsync(
            string? userId = null,
            string? action = null,
            DateTime? startTime = null,
            DateTime? endTime = null,
            int skip = 0,
            int take = 100
        );
        Task<long> GetCountAsync(
            string? userId = null,
            string? action = null,
            DateTime? startTime = null,
            DateTime? endTime = null
        );
        Task DeleteAsync(DateTime olderThan);
    }
}
