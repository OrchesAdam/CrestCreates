using CrestCreates.Integration.Cap.EfCore.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.Integration.Cap.EfCore.Persistence;

/// <summary>
/// 显式强类型 ModelBuilder 配置入口：应用在自身 <c>OnModelCreating</c> 中调用
/// <c>modelBuilder.ApplyCrestCapPersistence()</c> 应用 CAP 持久化实体映射。
/// 不自动修改任意应用 DbContext 模型、无 runtime 扫描 fallback；迁移由应用侧负责。
/// </summary>
public static class CrestCapModelBuilderExtensions
{
    public static ModelBuilder ApplyCrestCapPersistence(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new TransactionLogConfiguration());
        modelBuilder.ApplyConfiguration(new TransactionCompensationConfiguration());
        return modelBuilder;
    }
}
