using System;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// 受管链节点：每个受管 scope 一个，标识该 scope 在受管链中的位置。
    /// </summary>
    /// <remarks>
    /// 内核创建的隔离子 scope 节点挂到当前节点下（AttachTo）；普通 DI scope 的节点
    /// 为独立根。读方（适配器/DbContext）以自身节点解析 ambient：仅当帧 owner 节点
    /// 是自身节点自身或其后代时帧可见（descendant-or-self），因此独立 scope / 其他
    /// 宿主的读方绝不跟随他人帧，而子树内预注入的业务依赖可以跟随隔离子 UoW。
    /// </remarks>
    public sealed class UnitOfWorkChainNode
    {
        private UnitOfWorkChainNode? _parent;

        /// <summary>节点标识（诊断用）。</summary>
        public Guid Id { get; } = Guid.NewGuid();

        /// <summary>父节点；未挂接时为 null（独立根）。</summary>
        public UnitOfWorkChainNode? Parent => _parent;

        /// <summary>
        /// 判断本节点是否为 <paramref name="candidate"/> 自身或其后代。
        /// </summary>
        public bool IsSelfOrDescendantOf(UnitOfWorkChainNode candidate)
        {
            ArgumentNullException.ThrowIfNull(candidate);

            for (var node = this; node is not null; node = node._parent)
            {
                if (ReferenceEquals(node, candidate))
                {
                    return true;
                }
            }

            return false;
        }

        internal void AttachTo(UnitOfWorkChainNode? parent)
        {
            _parent = parent;
        }
    }
}
