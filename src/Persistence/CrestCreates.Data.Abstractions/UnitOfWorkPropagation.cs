namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// 工作单元传播方式。
    /// </summary>
    public enum UnitOfWorkPropagation
    {
        /// <summary>
        /// 复用当前受管工作单元；无环境时新建。
        /// </summary>
        Required = 0,

        /// <summary>
        /// 在独立的子作用域中新建工作单元（同一逻辑资源的新物理实例）。
        /// </summary>
        RequiresNew = 1
    }
}
