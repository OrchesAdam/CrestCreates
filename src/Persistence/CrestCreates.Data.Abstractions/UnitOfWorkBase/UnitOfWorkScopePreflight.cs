using System;
using System.Linq;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// Scope 创建前置校验（同步、无 I/O）：选项合法性、join 兼容性、Provider/隔离能力与租户快照。
    /// 所有拒绝都发生在业务执行与事务动作之前。
    /// </summary>
    internal static class UnitOfWorkScopePreflight
    {
        internal static void ValidateOptions(UnitOfWorkOptions options)
        {
            if (options.Propagation is not (UnitOfWorkPropagation.Required or UnitOfWorkPropagation.RequiresNew))
            {
                throw new InvalidOperationException(
                    $"Unsupported unit-of-work propagation '{options.Propagation}'. Supported values: Required, RequiresNew.");
            }

            if (options.Timeout is { } timeout && timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    timeout,
                    "Unit-of-work Timeout must be a positive duration.");
            }
        }

        internal static void ValidateJoin(UnitOfWorkScope owner, UnitOfWorkOptions options)
        {
            if (owner.State != UnitOfWorkState.Active)
            {
                throw new InvalidOperationException(
                    $"Cannot join a unit of work in state '{owner.State}'. Scope results remain readable, " +
                    "but new participation requires a new unit of work.");
            }

            if (options.IsTransactional != owner.IsTransactional)
            {
                throw new InvalidOperationException(
                    $"The requested transactional mode (IsTransactional={options.IsTransactional}) conflicts with " +
                    $"the active unit of work (IsTransactional={owner.IsTransactional}). Joining requires a matching value.");
            }

            if (options.Provider is { } requestedProvider && requestedProvider != owner.Provider)
            {
                throw new InvalidOperationException(
                    $"The requested provider '{requestedProvider}' conflicts with the active unit of work " +
                    $"provider '{owner.Provider}'. Joining requires a matching provider or null to inherit.");
            }

            if (options.IsolationLevel is { } requestedIsolation && requestedIsolation != owner.RequestedIsolation)
            {
                throw new InvalidOperationException(
                    $"The requested isolation level '{requestedIsolation}' cannot be validated against the active " +
                    $"unit of work (explicit isolation: '{owner.RequestedIsolation?.ToString() ?? "<unspecified>"}'). " +
                    "Joining requires a matching explicit isolation level on both scopes.");
            }

            if (options.Timeout is { } requestedTimeout &&
                owner.DeadlineUtc is { } ownerDeadline &&
                DateTimeOffset.UtcNow + requestedTimeout > ownerDeadline)
            {
                throw new InvalidOperationException(
                    "The requested timeout would extend beyond the active unit of work's remaining deadline. " +
                    "A joined scope may only tighten the deadline, never extend it.");
            }
        }

        internal static OrmProvider ResolveProvider(UnitOfWorkManager manager, OrmProvider? requested)
        {
            if (manager.Bindings is not null)
            {
                return manager.Bindings.ResolveProvider(requested, manager.ExplicitDefault);
            }

            return requested ?? manager.ExplicitDefault ?? OrmProvider.EfCore;
        }

        /// <summary>
        /// 解析受管资源的租户身份快照（host/null 是有效身份值，不能被当作关闭校验的开关）。
        /// </summary>
        internal static TenantKeySnapshot ResolveTenantSnapshot(
            UnitOfWorkManager manager,
            OrmProvider provider,
            IServiceProvider? serviceProvider)
        {
            var factory = manager.Bindings?.GetRequired(provider).TenantKeyFactory;
            if (factory is null || serviceProvider is null)
            {
                return TenantKeySnapshot.NotParticipating;
            }

            return new TenantKeySnapshot(factory(serviceProvider));
        }

        /// <summary>
        /// 执行前能力校验：显式隔离级别必须在 Provider 声明集合内（未声明即不支持，fail closed）。
        /// </summary>
        internal static void ValidateIsolationCapability(
            UnitOfWorkManager manager,
            OrmProvider provider,
            UnitOfWorkOptions options)
        {
            if (options.IsolationLevel is not { } level)
            {
                return;
            }

            var binding = manager.Bindings?.GetRequired(provider);
            if (binding is null)
            {
                // 自定义工厂路径：Provider 语义由自定义工厂自行解释（不经过能力索引）。
                return;
            }

            var supported = manager.ServiceProvider is not null
                ? binding.Capabilities.ResolveSupportedIsolationLevels(manager.ServiceProvider)
                : binding.Capabilities.SupportedIsolationLevels;

            if (!supported.Contains(level))
            {
                var declared = supported.Count == 0
                    ? "<none declared>"
                    : string.Join(", ", supported);
                throw new NotSupportedException(
                    $"The '{provider}' provider does not declare support for isolation level '{level}'. " +
                    $"Declared levels: {declared}. The request was rejected before executing any business code; " +
                    "do not request isolation levels the provider does not declare.");
            }
        }
    }
}
