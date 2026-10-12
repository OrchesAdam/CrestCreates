namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// 逻辑工作单元未完成退出的终结能力（可选接口，由 Provider 工作单元实现）。
    /// </summary>
    /// <remarks>
    /// 当 owner scope 未完成退出（未 Commit/Rollback）时，内核通过本接口：
    /// 1) 终结未完成事务（回滚 + 释放事务句柄）；
    /// 2) 丢弃未 flush 的跟踪写入，使残留状态不进入同一请求内的下一个工作单元。
    /// DI 持有的对象本身不释放（容器负责），本接口只终结事务与跟踪状态。
    /// 实现必须容忍重复调用与清理失败（不得替换原始业务异常）。
    /// 不支持该能力的 Provider 依赖其作用域释放语义并须在 Provider 矩阵中声明。
    /// </remarks>
    public interface IUnitOfWorkAbandonable
    {
        /// <summary>
        /// 若存在未完成事务则回滚并释放其句柄，并丢弃未 flush 的跟踪写入；无未完成工作时为 no-op。
        /// </summary>
        void AbandonPendingWork();
    }
}
