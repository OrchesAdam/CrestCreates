using CrestCreates.Domain.Features;
using CrestCreates.Domain.Shared.Exceptions;
using CrestCreates.MultiTenancy.Abstract;

namespace ErpManagement.Application.Services;

internal static class ErpFeatureAccess
{
    public static async Task RequireAsync(this IFeatureChecker checker, string feature, CancellationToken ct)
    {
        if (!await checker.IsEnabledAsync(feature, ct))
            throw new CrestValidationException($"功能 {feature} 未启用。");
    }

    public static Guid GetErpTenantId(this ICurrentTenant tenant)
        => tenant.Id is null or "" ? Guid.Empty
            : Guid.TryParse(tenant.Id, out var id) && id != Guid.Empty ? id
            : throw new CrestValidationException("当前租户缺少有效的 ERP 租户 GUID。");
}
