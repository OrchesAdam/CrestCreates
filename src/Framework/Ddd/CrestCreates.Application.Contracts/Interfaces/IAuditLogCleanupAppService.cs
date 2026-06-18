using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.AuditLog;

namespace CrestCreates.Application.Contracts.Interfaces;

/// <summary>
/// 审计日志清理应用服务接口
/// </summary>
public interface IAuditLogCleanupAppService
{
    /// <summary>
    /// 清理旧审计日志
    /// </summary>
    Task<CleanupAuditLogsResultDto> ProcessCleanupAsync(
        CleanupAuditLogsRequestDto request,
        CancellationToken cancellationToken = default);
}
