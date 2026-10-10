using System.Data;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// 统一工作单元选项（唯一生效定义）。
    /// </summary>
    /// <remarks>
    /// null 语义（join 时继承，见设计 §4.1）：
    /// IsolationLevel/Timeout/Provider 为 null 时表示未指定——join 继承外层，新建取默认。
    /// 显式值与外层不一致（隔离级别/Provider/事务开关）或超时超过外层剩余期限时，执行前确定性失败。
    /// </remarks>
    public sealed class UnitOfWorkOptions
    {
        /// <summary>事务开关。join 时必须与外层一致，否则执行前诊断。</summary>
        public bool IsTransactional { get; set; } = true;

        /// <summary>传播方式（Required / RequiresNew）。</summary>
        public UnitOfWorkPropagation Propagation { get; set; } = UnitOfWorkPropagation.Required;

        /// <summary>隔离级别；null = 未指定（join 继承外层，新建使用 Provider 默认）。</summary>
        public IsolationLevel? IsolationLevel { get; set; }

        /// <summary>本次执行的合作式截止时间；null = 无额外截止（join 继承外层剩余）。</summary>
        public System.TimeSpan? Timeout { get; set; }

        /// <summary>ORM 提供者；null = join 继承外层 / 新建按默认解析规则。</summary>
        public OrmProvider? Provider { get; set; }
    }
}
