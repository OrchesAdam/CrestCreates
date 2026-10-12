using System;
using System.Threading;
using System.Threading.Tasks;
using DotNetCore.CAP;
using DotNetCore.CAP.Transport;

namespace CrestCreates.Integration.Cap.EfCore;

/// <summary>
/// 守护型 CAP 事务：包装借用的受管事务，禁止 CAP 侧驱动其生命周期（硬边界）。
/// Commit/Rollback 一律抛确定性异常（AutoCommit=false，正常流程不会触达）；
/// Dispose 只解除借用引用、不释放借用句柄；消息缓冲随实例丢弃——
/// 投递由 CAP 存储扫描恢复完成（无 flush 的必要）。
/// </summary>
internal sealed class GuardedPostgreSqlCapTransaction : PostgreSqlCapTransaction
{
    private const string GuardMessage =
        "The CAP transaction guards a borrowed unit-of-work transaction; CAP must not drive its lifecycle " +
        "(commit/rollback/dispose are owned by the unit-of-work kernel).";

    public GuardedPostgreSqlCapTransaction(IDispatcher dispatcher)
        : base(dispatcher)
    {
    }

    public override void Commit() => throw new InvalidOperationException(GuardMessage);

    public override Task CommitAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException(GuardMessage);

    public override void Rollback() => throw new InvalidOperationException(GuardMessage);

    public override Task RollbackAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException(GuardMessage);

    public override void Dispose() => DbTransaction = null;
}
