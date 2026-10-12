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
        TenantKeySnapshot readerTenant)
    {
        // 数据准入守卫（先于一切路由与提前返回）：租户身份（含 host/null）、终态/释放拒绝，
        // 以及逻辑资源身份（owner 窗口记录的声明种类/连接配置 vs 读方自身——第二 Context
        // 声明/连接配置在根窗口与隔离窗口同样确定性拒绝）。
        UnitOfWorkExecutionContext.ValidateDataAccess(
            readerNode,
            readerTenant,
            own.GetType().FullName,
            TryGetConnectionString(own));

        // 无环境帧（requiresNew 推入）时返回本作用域上下文；逻辑资源准入已在上方完成。
        if (UnitOfWorkAmbientContext.Current is null)
        {
            return own;
        }

        if (!UnitOfWorkAmbientContext.TryResolveCurrent(
                self,
                readerNode,
                readerTenant,
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
        catch (System.Exception)
        {
            // 非关系型 Provider（如 InMemory）：没有连接配置，逻辑键退化为声明种类。
            return null;
        }
    }
}
