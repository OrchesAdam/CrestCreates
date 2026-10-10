using System;
using System.Threading;
using CrestCreates.DbContextProvider.Abstract;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// 工作单元环境上下文（强类型受管访问器）：requiresNew 隔离期间，内核把内层作用域的
    /// <see cref="IDataBaseContext"/> 推入环境，使已注入的业务依赖在操作时解析到当前 UoW
    /// 的上下文；写路径（Push/Restore）内部化，仅内核可操作。
    /// </summary>
    /// <remarks>
    /// 帧携带：资源上下文、owner 链节点、租户键与逻辑资源键（context 声明类型 + 连接配置）。
    /// 读方（适配器/DbContext）应以 <see cref="TryResolveCurrent"/> 解析：
    /// 1) descendant-or-self 链校验（独立 scope/其他宿主不可见他人帧）；
    /// 2) 先校验后路由——租户键、context 声明、连接配置与读方自身不一致时确定性拒绝，
    ///    不做静默重定向（合法 RequiresNew 是同一逻辑资源的新物理实例，父级依赖可跟随）。
    /// </remarks>
    public static class UnitOfWorkAmbientContext
    {
        private static readonly AsyncLocal<AmbientContextFrame?> CurrentFrame = new();

        /// <summary>
        /// 当前环境上下文（无 requiresNew 隔离时为 null）。
        /// </summary>
        public static IDataBaseContext? Current => CurrentFrame.Value?.Context;

        /// <summary>
        /// 读方解析当前资源（先链校验、后身份校验，最后路由）。
        /// </summary>
        /// <param name="self">读方自身的上下文（通常是适配器持有的实例）。</param>
        /// <param name="readerNode">读方所在 scope 的受管链节点；null 表示读方未接入链校验（旧式手工装配）。</param>
        /// <param name="currentTenantKey">读方当前租户键；null 表示不参与租户校验。</param>
        /// <param name="readerLogicalContextType">读方自身 context 的声明类型全名。</param>
        /// <param name="readerConnectionString">读方自身连接配置。</param>
        /// <param name="context">解析结果：可见帧的上下文或读方自身。</param>
        /// <returns>true 表示帧可见并应路由到 <paramref name="context"/>。</returns>
        /// <exception cref="InvalidOperationException">
        /// 帧在本链可见但身份不一致（租户/声明/连接配置）时抛出，指引新建 UoW。
        /// </exception>
        public static bool TryResolveCurrent(
            IDataBaseContext self,
            UnitOfWorkChainNode? readerNode,
            string? currentTenantKey,
            string? readerLogicalContextType,
            string? readerConnectionString,
            out IDataBaseContext context)
        {
            ArgumentNullException.ThrowIfNull(self);

            context = self;
            var frame = CurrentFrame.Value;
            if (frame is null || ReferenceEquals(frame.Context, self))
            {
                return false;
            }

            // 链可见性：帧 owner 必须为读方自身或其后代；未接入链的读方沿用既有可见性。
            if (readerNode is not null &&
                frame.OwnerNode is not null &&
                !frame.OwnerNode.IsSelfOrDescendantOf(readerNode))
            {
                return false;
            }

            // 先校验后路由：身份不一致属于「第二资源」，确定性拒绝而不是静默切换。
            if (!string.IsNullOrEmpty(frame.TenantKey) &&
                !string.IsNullOrEmpty(currentTenantKey) &&
                !string.Equals(frame.TenantKey, currentTenantKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The ambient unit-of-work resource belongs to tenant '{frame.TenantKey}' but the current " +
                    $"tenant is '{currentTenantKey}'. Switching tenant inside an active unit of work is not " +
                    "supported; start a new unit of work for the other tenant.");
            }

            if (!string.IsNullOrEmpty(frame.LogicalContextType) &&
                !string.IsNullOrEmpty(readerLogicalContextType) &&
                !string.Equals(frame.LogicalContextType, readerLogicalContextType, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The ambient unit-of-work resource uses context declaration '{frame.LogicalContextType}', " +
                    $"but this reader declares '{readerLogicalContextType}'. A second context declaration is a " +
                    "different logical resource; start a new unit of work for it instead of sharing the ambient one.");
            }

            if (!string.IsNullOrEmpty(frame.LogicalConnectionString) &&
                !string.IsNullOrEmpty(readerConnectionString) &&
                !string.Equals(frame.LogicalConnectionString, readerConnectionString, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The ambient unit-of-work resource uses a different connection configuration than this reader. " +
                    "A second connection is a different logical resource; start a new unit of work for it.");
            }

            context = frame.Context;
            return true;
        }

        /// <summary>
        /// 推入环境上下文（平台内部；调用方帧同步执行）。
        /// </summary>
        internal static IDisposable Push(
            IDataBaseContext context,
            UnitOfWorkChainNode? ownerNode,
            string? tenantKey)
        {
            ArgumentNullException.ThrowIfNull(context);

            var frame = new AmbientContextFrame(
                context,
                ownerNode,
                tenantKey,
                context.GetNativeContext().GetType().FullName,
                TryReadConnectionString(context),
                CurrentFrame.Value);

            CurrentFrame.Value = frame;
            return new FrameRestorer(frame);
        }

        private static string? TryReadConnectionString(IDataBaseContext context)
        {
            try
            {
                return context.ConnectionString;
            }
            catch (Exception)
            {
                // 非关系型 Provider 没有连接配置（如 InMemory）：逻辑键退化为声明类型。
                return null;
            }
        }

        private sealed class AmbientContextFrame
        {
            public AmbientContextFrame(
                IDataBaseContext context,
                UnitOfWorkChainNode? ownerNode,
                string? tenantKey,
                string? logicalContextType,
                string? logicalConnectionString,
                AmbientContextFrame? parent)
            {
                Context = context;
                OwnerNode = ownerNode;
                TenantKey = tenantKey;
                LogicalContextType = logicalContextType;
                LogicalConnectionString = logicalConnectionString;
                Parent = parent;
            }

            public IDataBaseContext Context { get; }

            public UnitOfWorkChainNode? OwnerNode { get; }

            public string? TenantKey { get; }

            public string? LogicalContextType { get; }

            public string? LogicalConnectionString { get; }

            public AmbientContextFrame? Parent { get; }
        }

        private sealed class FrameRestorer : IDisposable
        {
            private readonly AmbientContextFrame _frame;
            private bool _disposed;

            public FrameRestorer(AmbientContextFrame frame)
            {
                _frame = frame;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                // 仅当本帧仍是栈顶时恢复；乱序释放不允许回退别人的环境。
                if (ReferenceEquals(CurrentFrame.Value, _frame))
                {
                    CurrentFrame.Value = _frame.Parent;
                }
            }
        }
    }
}
