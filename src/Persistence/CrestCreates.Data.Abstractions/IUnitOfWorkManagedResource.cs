namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// 受管资源曝光（可选接口，由 Provider 工作单元实现）：把「本 UoW 选定的逻辑资源与
    /// 当前受管事务关联」交给内核，供两处准入共用同一权威：
    /// 1) 所有 owner 窗口的逻辑资源准入（第二 Context 声明/连接配置确定性拒绝，不只在隔离期间）；
    /// 2) CAP 参与准入（lease 签发与原生借用核对「同一受管窗口 + 同一实际资源/事务」）。
    /// 句柄仅用于身份关联；生命周期始终由内核掌握，实现不得暴露可操作入口。
    /// </summary>
    public interface IUnitOfWorkManagedResource
    {
        /// <summary>选定资源的声明种类（如 Context 声明类型全名）；无资源时为 null。</summary>
        string? ManagedResourceKind { get; }

        /// <summary>选定资源的连接配置（非关系型 Provider 为 null；比较退化为声明种类）。</summary>
        string? ManagedResourceConnectionString { get; }

        /// <summary>选定资源的原生上下文句柄（身份关联用；不提供生命周期操作）。</summary>
        object? ManagedResourceContext { get; }

        /// <summary>当前受管事务句柄；无事务（未开始/非事务）时为 null。</summary>
        object? ManagedTransactionHandle { get; }
    }
}
