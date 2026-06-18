using System;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.AuditLog;

namespace CrestCreates.Data.EFCore.Repositories;

/// <summary>
/// 兼容旧类名，实际实现已移至 <see cref="EfCoreAuditLogRepository"/>.
/// </summary>
public class AuditLogRepository : EfCoreAuditLogRepository
{
    public AuditLogRepository(
        IDataBaseContext dbContext,
        CrestCreates.MultiTenancy.Abstract.ICurrentTenant currentTenant)
        : base(dbContext, currentTenant)
    {
    }
}
