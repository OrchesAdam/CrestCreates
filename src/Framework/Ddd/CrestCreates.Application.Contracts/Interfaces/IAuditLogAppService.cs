using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.AuditLog;
using CrestCreates.Application.Contracts.DTOs.Common;

namespace CrestCreates.Application.Contracts.Interfaces;

/// <summary>
/// 审计日志查询应用服务接口
/// </summary>
public interface IAuditLogAppService
{
    /// <summary>
    /// 分页查询审计日志
    /// </summary>
    Task<PagedResultDto<AuditLogListItemDto>> GetListAsync(AuditLogPagedRequestDto request, CancellationToken cancellationToken = default);
}
