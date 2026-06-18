using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Tenants;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.MultiTenancy;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.Application.Tenants;

[CrestService]
public class TenantDiagnosticsAppService
{
    private readonly ITenantRepository _tenantRepository;
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionGrantRepository _permissionGrantRepository;
    private readonly ITenantDomainMappingRepository _domainMappingRepository;
    private readonly IConnectionStringProtector _protector;

    public TenantDiagnosticsAppService(
        ITenantRepository tenantRepository,
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IPermissionGrantRepository permissionGrantRepository,
        ITenantDomainMappingRepository domainMappingRepository,
        IConnectionStringProtector protector)
    {
        _tenantRepository = tenantRepository;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _permissionGrantRepository = permissionGrantRepository;
        _domainMappingRepository = domainMappingRepository;
        _protector = protector;
    }

    public async Task<TenantDiagnosticsDto> DiagnoseAsync(
        string tenantName,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantRepository.FindByNameAsync(tenantName, cancellationToken)
            ?? throw new InvalidOperationException($"租户 '{tenantName}' 不存在");

        return await DiagnoseTenantAsync(tenant, cancellationToken);
    }

    public async Task<TenantDiagnosticsDto> DiagnoseByIdAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantRepository.GetAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"租户 ID '{tenantId}' 不存在");

        return await DiagnoseTenantAsync(tenant, cancellationToken);
    }

    private async Task<TenantDiagnosticsDto> DiagnoseTenantAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        var tenantIdStr = tenant.Id.ToString();
        var diagnostics = new TenantDiagnosticsDto
        {
            TenantId = tenant.Id,
            Name = tenant.Name,
            DiagnosedAt = DateTime.UtcNow,
            Status = new TenantStatusDetails
            {
                LifecycleState = tenant.LifecycleState.ToString(),
                IsActive = tenant.IsActive,
                ArchivedTime = tenant.ArchivedTime,
                CreationTime = tenant.CreationTime,
                LastModificationTime = tenant.LastModificationTime
            },
            ConnectionStrings = DiagnoseConnectionStrings(tenant),
            Admin = await DiagnoseAdminAsync(tenantIdStr, cancellationToken),
            Statistics = await DiagnoseStatisticsAsync(tenantIdStr, cancellationToken)
        };

        diagnostics.OverallStatus = CalculateHealthStatus(diagnostics);

        if (!diagnostics.Status.IsActive && diagnostics.Status.LifecycleState != TenantLifecycleState.Active.ToString())
        {
            diagnostics.Warnings.Add($"租户状态为 {diagnostics.Status.LifecycleState}");
        }

        if (!diagnostics.ConnectionStrings.HasDefaultConnectionString)
        {
            diagnostics.Warnings.Add("租户没有配置默认连接串");
        }

        if (!diagnostics.Admin.HasAdmin)
        {
            diagnostics.Warnings.Add("租户没有管理员用户");
        }

        return diagnostics;
    }

    private ConnectionStringSummary DiagnoseConnectionStrings(Tenant tenant)
    {
        var summary = new ConnectionStringSummary
        {
            TotalCount = tenant.ConnectionStrings.Count,
            HasDefaultConnectionString = tenant.ConnectionStrings.Any(cs => cs.Name == TenantConnectionString.DefaultName),
            DefaultConnectionStringMasked = _protector.Mask(tenant.GetDefaultConnectionString() ?? string.Empty),
            NamedConnectionStrings = tenant.ConnectionStrings
                .Where(cs => cs.Name != TenantConnectionString.DefaultName)
                .Select(cs => $"{cs.Name} ({_protector.Mask(cs.Value)})")
                .ToList()
        };

        return summary;
    }

    private async Task<AdminSummary> DiagnoseAdminAsync(string tenantId, CancellationToken cancellationToken)
    {
        var users = await _userRepository.GetListByTenantIdAsync(tenantId, cancellationToken);
        var adminUser = users.FirstOrDefault(u => u.IsSuperAdmin && u.IsActive);

        if (adminUser == null)
        {
            return new AdminSummary { HasAdmin = false };
        }

        return new AdminSummary
        {
            HasAdmin = true,
            AdminUserId = adminUser.Id.ToString(),
            AdminUserName = adminUser.UserName,
            AdminEmail = adminUser.Email,
            IsAdminActive = adminUser.IsActive
        };
    }

    private async Task<Statistics> DiagnoseStatisticsAsync(string tenantId, CancellationToken cancellationToken)
    {
        var userCount = await _userRepository.GetCountByTenantIdAsync(tenantId, cancellationToken);
        var roleCount = await _roleRepository.GetCountByTenantIdAsync(tenantId, cancellationToken);
        var permissionGrants = await _permissionGrantRepository.GetListByTenantIdAsync(tenantId, cancellationToken);

        Guid tenantGuid;
        var domainMappingCount = 0;
        if (Guid.TryParse(tenantId, out tenantGuid))
        {
            var domainMappings = await _domainMappingRepository.GetByTenantIdAsync(tenantGuid, cancellationToken);
            domainMappingCount = domainMappings.Count;
        }

        return new Statistics
        {
            UserCount = userCount,
            RoleCount = roleCount,
            PermissionGrantCount = permissionGrants.Count,
            DomainMappingCount = domainMappingCount
        };
    }

    private TenantHealthStatus CalculateHealthStatus(TenantDiagnosticsDto diagnostics)
    {
        var status = new TenantHealthStatus
        {
            IsHealthy = true,
            Level = "Healthy"
        };

        if (diagnostics.Status.LifecycleState == TenantLifecycleState.Archived.ToString())
        {
            status.IsArchived = true;
            status.IsHealthy = false;
            status.Level = "Archived";
            status.Issues.Add("租户已归档");
        }
        else if (diagnostics.Status.LifecycleState == TenantLifecycleState.Deleted.ToString())
        {
            status.IsHealthy = false;
            status.Level = "Deleted";
            status.Issues.Add("租户已删除");
        }
        else if (!diagnostics.Status.IsActive)
        {
            status.IsActive = false;
            status.IsHealthy = false;
            status.Level = "Suspended";
            status.Issues.Add("租户已停用");
        }

        if (!diagnostics.ConnectionStrings.HasDefaultConnectionString)
        {
            status.IsHealthy = false;
            status.Level = "Degraded";
            status.Issues.Add("缺少连接串配置");
        }

        if (!diagnostics.Admin.HasAdmin)
        {
            status.IsHealthy = false;
            status.Level = "Degraded";
            status.Issues.Add("缺少管理员");
        }

        if (diagnostics.Warnings.Count > 0 && status.IsHealthy)
        {
            status.Level = "Warning";
        }

        return status;
    }
}
