using System;
using CrestCreates.Data.Abstractions;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.DistributedTransaction.CAP.Abstractions;
using CrestCreates.DistributedTransaction.Models;
using CrestCreates.Integration.Cap.EfCore.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Integration.Cap.EfCore;

/// <summary>
/// EF Core + Npgsql 集成装配（#125）：
/// 1) 注册 <see cref="ICapManagedTransactionProvider"/>（原子发布的事务绑定边界）；
/// 2) 注册 CAP 持久化仓储（<c>IRepository&lt;TransactionLog/TransactionCompensation&gt;</c>，
///    由 <see cref="EfCoreCapRepository{TEntity, TKey}"/> 基于应用 scoped
///    <see cref="IDataBaseContext"/> 提供）。
/// 实体映射不在此处修改应用模型——应用须在 OnModelCreating 显式调用
/// <c>ApplyCrestCapPersistence()</c>；迁移由应用侧负责。
/// </summary>
public static class CrestCapEfCoreServiceCollectionExtensions
{
    public static IServiceCollection AddCrestCapEfCoreIntegration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ICapManagedTransactionProvider, EfCoreCapManagedTransactionProvider>();
        services.AddScoped<IRepository<TransactionLog, Guid>>(sp => new EfCoreCapRepository<TransactionLog, Guid>(
            sp.GetRequiredService<IDataBaseContext>(),
            sp.GetService<UnitOfWorkChainNode>()));
        services.AddScoped<IRepository<TransactionCompensation, Guid>>(sp => new EfCoreCapRepository<TransactionCompensation, Guid>(
            sp.GetRequiredService<IDataBaseContext>(),
            sp.GetService<UnitOfWorkChainNode>()));

        return services;
    }
}
