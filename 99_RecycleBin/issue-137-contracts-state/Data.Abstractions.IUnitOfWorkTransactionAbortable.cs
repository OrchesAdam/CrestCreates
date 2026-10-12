using System;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// 逻辑工作单元的事务终结能力（可选接口，由 Provider 工作单元实现）。
    /// </summary>
    /// <remarks>
    /// 绑定路径的工作单元由 DI 作用域持有；当 scope 未完成退出（未 Commit/Rollback）时，
    /// 管理器通过本接口及时回滚并释放未完成事务，而不释放 DI 持有的对象本身——
    /// 容器负责对象释放，本接口只负责事务终结。未实现该接口的 Provider 依赖其
    /// 作用域释放语义（见各 Provider 支持矩阵）。
    /// </remarks>
    public interface IUnitOfWorkTransactionAbortable
    {
        /// <summary>
        /// 若存在未完成的显式事务则回滚并释放其句柄；无未完成事务时为 no-op。
        /// </summary>
        /// <remarks>实现必须容忍重复调用与清理失败（不得替换原始业务异常）。</remarks>
        void AbortPendingTransaction();
    }
}
