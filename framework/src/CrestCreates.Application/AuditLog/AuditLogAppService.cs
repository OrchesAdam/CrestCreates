using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.AuditLog;
using CrestCreates.Application.Contracts.DTOs.Common;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.Application.AuditLog;

/// <summary>
/// 审计日志查询应用服务
/// </summary>
[CrestService]
public class AuditLogAppService : IAuditLogAppService
{
    private readonly IAuditLogRepository _repository;

    public AuditLogAppService(IAuditLogRepository repository)
    {
        _repository = repository;
    }

    public async Task<PagedResultDto<AuditLogListItemDto>> GetListAsync(
        AuditLogPagedRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var result = await _repository.GetPagedListAsync(
            request.StartTime,
            request.EndTime,
            request.UserId,
            request.UserName,
            request.TenantId,
            request.Status,
            request.HttpMethod,
            request.Keyword,
            request.PageIndex,
            request.PageSize,
            cancellationToken);

        var items = result.Items.Select(item => new AuditLogListItemDto
        {
            Id = item.Id,
            ExecutionTime = item.ExecutionTime,
            Duration = item.Duration,
            UserId = item.UserId,
            UserName = item.UserName,
            TenantId = item.TenantId,
            HttpMethod = item.HttpMethod,
            Url = item.Url,
            ServiceName = item.ServiceName,
            MethodName = item.MethodName,
            Status = item.Status,
            ClientIpAddress = item.ClientIpAddress,
            ExtraProperties = item.ExtraProperties
        }).ToList();

        return new PagedResultDto<AuditLogListItemDto>(
            items,
            result.TotalCount,
            result.PageIndex,
            result.PageSize);
    }
}
