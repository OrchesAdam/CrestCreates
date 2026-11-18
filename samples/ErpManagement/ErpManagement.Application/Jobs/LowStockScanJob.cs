using CrestCreates.Domain.Repositories;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Scheduling.Jobs;
using ErpManagement.Application.Services;
using ErpManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ErpManagement.Application.Jobs;

public sealed class LowStockScanJob(
    ICurrentTenant tenant,
    ICrestRepositoryBase<StockBalance, Guid> balances,
    ICrestRepositoryBase<Material, Guid> materials,
    ILogger<LowStockScanJob> logger) : IJob
{
    public async Task ExecuteAsync(JobExecutionContext<NoArgs> context, CancellationToken ct = default)
    {
        using var host = tenant.Change((ITenantInfo?)null);
        var count = await new LowStockQuery(balances, materials).MaterialIds(tenantId: null).CountAsync(ct);
        logger.LogInformation("ERP low-stock scan found {MaterialCount} materials in the host database.", count);
    }
}
