using CrestCreates.Data.Abstractions;
using CrestCreates.DbContextProvider.Abstract;
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.Data.EFCore.DbContexts;

/// <summary>
/// EF Core 上下文解析：requiresNew 隔离期间返回环境推入的内层上下文（链校验 + 身份校验后）；
/// 否则返回本作用域上下文。供 DbContext 适配器与框架 DbContext 共用，
/// 保证默认装配与自定义装配的仓储上下文选择一致。
/// </summary>
internal static class EfCoreAmbientContext
{
    public static DbContext Resolve(
        IDataBaseContext self,
        DbContext own,
        UnitOfWorkChainNode? readerNode,
        string? currentTenantKey)
    {
        // 无环境帧时快速返回，避免为非关系型 Provider 计算逻辑键（连接串读取仅限关系型）。
        if (UnitOfWorkAmbientContext.Current is null)
        {
            return own;
        }

        if (!UnitOfWorkAmbientContext.TryResolveCurrent(
                self,
                readerNode,
                currentTenantKey,
                own.GetType().FullName,
                TryGetConnectionString(own),
                out var ambient))
        {
            return own;
        }

        return ambient.GetNativeContext() as DbContext ?? own;
    }

    private static string? TryGetConnectionString(DbContext context)
    {
        try
        {
            return context.Database.GetConnectionString();
        }
        catch (System.InvalidOperationException)
        {
            // 非关系型 Provider（如 InMemory）没有连接配置。
            return null;
        }
    }
}
