using CrestCreates.Data.Abstractions;
using CrestCreates.DbContextProvider.Abstract;
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.Data.EFCore.DbContexts;

/// <summary>
/// EF Core 上下文解析：requiresNew 隔离期间返回环境推入的内层上下文；
/// 否则返回本作用域上下文。供 DbContext 适配器与框架 DbContext 共用，
/// 保证默认装配与自定义装配的仓储上下文选择一致。
/// </summary>
internal static class EfCoreAmbientContext
{
    public static DbContext Resolve(IDataBaseContext self, DbContext own)
    {
        if (UnitOfWorkAmbientContext.Current is not IDataBaseContext ambient
            || ReferenceEquals(ambient, self))
        {
            return own;
        }

        return ambient.GetNativeContext() as DbContext ?? own;
    }
}
