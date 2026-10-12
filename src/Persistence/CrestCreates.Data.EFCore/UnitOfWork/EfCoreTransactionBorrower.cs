using CrestCreates.Data.Abstractions;
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
/// 借用与 lease 签发共用同一受管事务准入权威：仅在读方可见链上最近的 owner 窗口
/// 已开始、为事务型、Active、未释放，且其记录的实际资源/事务与解析结果同一关联时借出；
/// 原生未受管事务、未开始/非事务/终态窗口与独立宿主一律拒绝。
/// 普通业务与通用 lease 消费者不应依赖本接口。
/// </remarks>
public interface IEfCoreTransactionBorrower
{
    /// <summary>
    /// 借用当前受管窗口有效资源的原生上下文与事务句柄；
    /// 无活动受管事务关联（含未开始、非事务、终态、释放、未受管原生事务）时返回 false。
    /// </summary>
    bool TryBorrowCurrent(out DbContext? context, out IDbContextTransaction? transaction);
}

internal sealed class EfCoreTransactionBorrower : IEfCoreTransactionBorrower
{
    private readonly IDataBaseContext _dataBaseContext;
    private readonly UnitOfWorkChainNode? _chainNode;

    public EfCoreTransactionBorrower(IDataBaseContext dataBaseContext, UnitOfWorkChainNode? chainNode)
    {
        _dataBaseContext = dataBaseContext;
        _chainNode = chainNode;
    }

    public bool TryBorrowCurrent(out DbContext? context, out IDbContextTransaction? transaction)
    {
        context = null;
        transaction = null;

        // 与 lease 签发同源的受管事务准入：Provider 适配允许拿到原生对象，不等于绕过参与范围校验。
        if (!UnitOfWorkExecutionContext.TryGetCurrentManagedTransaction(
                _chainNode,
                out var managedTransaction,
                out var managedContext,
                out _))
        {
            return false;
        }

        if (_dataBaseContext.GetNativeContext() is DbContext dbContext &&
            ReferenceEquals(dbContext, managedContext) &&
            dbContext.Database.CurrentTransaction is { } currentTransaction &&
            ReferenceEquals(currentTransaction, managedTransaction))
        {
            context = dbContext;
            transaction = currentTransaction;
            return true;
        }

        return false;
    }
}
