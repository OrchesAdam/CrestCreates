using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.AuditLog;
using CrestCreates.Application.Contracts.DTOs.Common;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Application.Contracts.Permissions;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.Web.PlatformManagement;

/// <summary>
/// Self-contract projection for audit-log query (G-09). The service contract takes a complex
/// <see cref="AuditLogPagedRequestDto"/>, which the wire binder can only bind as a JSON body;
/// a clean REST read uses flattened scalar query parameters instead, so this projection assembles
/// the DTO and delegates to the real service (BookHttpProjection precedent).
/// </summary>
[CapabilityCompatibilityProjection(
    ContractType = typeof(AuditLogHttpProjection),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/audit-logs",
    CapabilityIdPrefix = "compat.appservice.audit-log")]
public sealed class AuditLogHttpProjection(IAuditLogAppService service)
{
    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { PlatformHttpPermissions.AuditLog.Read })]
    public Task<PagedResultDto<AuditLogListItemDto>> GetListAsync(
        string? keyword = null,
        int? status = null,
        string? httpMethod = null,
        string? tenantId = null,
        string? userId = null,
        string? userName = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int? pageIndex = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default)
        => service.GetListAsync(
            new AuditLogPagedRequestDto
            {
                Keyword = keyword,
                Status = status,
                HttpMethod = httpMethod,
                TenantId = tenantId,
                UserId = userId,
                UserName = userName,
                StartTime = startTime,
                EndTime = endTime,
                PageIndex = pageIndex ?? 0,
                PageSize = pageSize ?? 10
            },
            cancellationToken);
}