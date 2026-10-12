using System;

namespace CrestCreates.DistributedTransaction.CAP.Abstractions;

/// <summary>
/// 集成层提供的受管事务绑定边界（CAP 原子发布协议的一部分）。
/// Runtime 只消费本强类型契约；EF/具体 CAP storage 耦合与原生句柄借用留在集成项目。
/// </summary>
/// <remarks>
/// 硬边界：无论实现如何，都不得调用 CAP 的 Commit/Rollback/Dispose 去驱动借用句柄的
/// 生命周期——生命周期唯一归 UnitOfWork 内核。
/// </remarks>
public interface ICapManagedTransactionProvider
{
    /// <summary>
    /// NoVisibleWindow 路径的适配检查：检测到 scoped 上下文上存在未受管原生事务时抛
    /// <see cref="CapPublishRefusalException"/>（UnmanagedNativeTransaction）；无可检测上下文时 no-op。
    /// </summary>
    void EnsureNoUnmanagedNativeTransaction();

    /// <summary>
    /// Granted 路径：完成存储身份校验与原生事务借用，返回绑定。
    /// 绑定期间设置 <c>ICapPublisher.Transaction</c>（事务内写消息行 + 缓冲）；
    /// Dispose 时恢复先前的 publisher 上下文（瞬态语义）并解除借用引用（不释放句柄）。
    /// 任何拒绝抛 <see cref="CapPublishRefusalException"/>（带错误码），且不得产生写入副作用。
    /// </summary>
    ICapManagedTransactionBinding BindManagedTransaction(DotNetCore.CAP.ICapPublisher publisher);
}

/// <summary>
/// 受管事务绑定（瞬态）：仅在 <c>using</c> 作用域内有效；Dispose 恢复 publisher 上下文。
/// </summary>
public interface ICapManagedTransactionBinding : IDisposable
{
}

/// <summary>
/// 确定性发布拒绝：带错误码与诊断信息（不含连接凭据），拒绝发生在任何消息写入之前。
/// </summary>
public sealed class CapPublishRefusalException : InvalidOperationException
{
    public CapPublishRefusalException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>稳定的错误码（ManagedWindowRefused / DelayedPublishInManagedWindow /
    /// BorrowerUnavailable / UnmanagedNativeTransaction / StorageIdentityMismatch）。</summary>
    public string Code { get; }
}
