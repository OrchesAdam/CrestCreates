using System;
using System.Threading.Tasks;
using CrestCreates.Integration.Cap.EfCore;
using Xunit;

namespace CrestCreates.DistributedTransaction.CAP.IntegrationTests;

/// <summary>
/// #125 矩阵 11：守护型 CAP 事务绝不驱动借用句柄生命周期（Commit/Rollback 抛确定性异常；
/// Dispose 只解除引用、不释放句柄）。
/// </summary>
public class GuardedCapTransactionTests
{
    [Fact]
    public void Commit_variants_are_guarded()
    {
        var guarded = new GuardedPostgreSqlCapTransaction(null!);

        Assert.Contains("guards a borrowed", Assert.Throws<InvalidOperationException>(() => guarded.Commit()).Message);
        Assert.Contains("guards a borrowed", Assert.Throws<InvalidOperationException>(() => guarded.Rollback()).Message);
        Assert.Contains(
            "guards a borrowed",
            (Assert.ThrowsAsync<InvalidOperationException>(() => guarded.CommitAsync())).GetAwaiter().GetResult().Message);
        Assert.Contains(
            "guards a borrowed",
            (Assert.ThrowsAsync<InvalidOperationException>(() => guarded.RollbackAsync())).GetAwaiter().GetResult().Message);
    }

    [Fact]
    public void Dispose_only_detaches_and_never_disposes_the_borrowed_handle()
    {
        var borrowed = new TrackedDisposable();
        var guarded = new GuardedPostgreSqlCapTransaction(null!) { DbTransaction = borrowed };

        guarded.Dispose();

        Assert.False(borrowed.Disposed, "CAP dispose must never release the borrowed unit-of-work handle");
        Assert.Null(guarded.DbTransaction);
    }

    private sealed class TrackedDisposable : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
