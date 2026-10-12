using CrestCreates.DbContextProvider.Abstract;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CrestCreates.Data.EFCore.UnitOfWork;

/// <summary>
/// EF Core 原生事务借用边界（仅限 Provider/集成适配使用，如 CAP SDK 适配）。
/// </summary>
/// <remarks>
/// 借用规则：调用方**不得 Dispose / Commit / Rollback** 返回的句柄，
/// 仅可在同一事务内附加命令（如消息记录写入）；生命周期操作始终由 UoW 内核掌握。
/// 普通业务与通用 lease 消费者不应依赖本接口。
/// </remarks>
public interface IEfCoreTransactionBorrower
{
    /// <summary>
    /// 借用当前受管窗口有效资源的原生上下文与事务句柄；无活动受管事务时返回 false。
    /// </summary>
    bool TryBorrowCurrent(out DbContext? context, out IDbContextTransaction? transaction);
}

internal sealed class EfCoreTransactionBorrower : IEfCoreTransactionBorrower
{
    private readonly IDataBaseContext _dataBaseContext;

    public EfCoreTransactionBorrower(IDataBaseContext dataBaseContext)
    {
        _dataBaseContext = dataBaseContext;
    }

    public bool TryBorrowCurrent(out DbContext? context, out IDbContextTransaction? transaction)
    {
        if (_dataBaseContext.GetNativeContext() is DbContext dbContext &&
            dbContext.Database.CurrentTransaction is { } currentTransaction)
        {
            context = dbContext;
            transaction = currentTransaction;
            return true;
        }

        context = null;
        transaction = null;
        return false;
    }
}
