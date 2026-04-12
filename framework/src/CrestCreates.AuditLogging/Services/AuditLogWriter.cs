using System;
using System.Threading.Tasks;
using CrestCreates.AuditLogging.Context;
using Microsoft.Extensions.Logging;

namespace CrestCreates.AuditLogging.Services
{
    /// <summary>
    /// 统一审计日志写入器实现
    /// </summary>
    public class AuditLogWriter : IAuditLogWriter
    {
        private readonly IAuditLogService _auditLogService;
        private readonly ILogger<AuditLogWriter> _logger;

        public AuditLogWriter(
            IAuditLogService auditLogService,
            ILogger<AuditLogWriter> logger)
        {
            _auditLogService = auditLogService;
            _logger = logger;
        }

        public async Task WriteAsync(AuditContext context)
        {
            try
            {
                var auditLog = context.ToAuditLog();
                await _auditLogService.CreateAsync(auditLog);
                _logger.LogDebug(
                    "Audit log written: {HttpMethod} {Url} -> {Status} ({Duration}ms)",
                    context.HttpMethod,
                    context.Url,
                    auditLog.Status,
                    auditLog.Duration);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write audit log for {Url}", context.Url);
                throw;
            }
        }
    }
}
