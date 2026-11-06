using System;
using CrestCreates.Domain.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// Owner scope 创建（同步；调用方帧内完成环境激活）：顶层 root 与 requiresNew 隔离子 scope。
    /// 隔离路径必须先 Push 子作用域 ambient 上下文再构造 UoW，失败时释放子作用域。
    /// </summary>
    internal static class UnitOfWorkScopeBootstrapper
    {
        internal static UnitOfWorkScope BeginRoot(
            UnitOfWorkManager manager,
            UnitOfWorkManager.AmbientFrame? frame,
            UnitOfWorkScope? executionParent,
            UnitOfWorkOptions options)
        {
            var provider = UnitOfWorkScopePreflight.ResolveProvider(manager, options.Provider);
            UnitOfWorkScopePreflight.ValidateIsolationCapability(manager, provider, options);
            var unitOfWork = manager.Factory.Create(provider);
            var tenantSnapshot = UnitOfWorkScopePreflight.ResolveTenantSnapshot(manager, provider, manager.ServiceProvider);
            var node = new UnitOfWorkChainNode();
            node.AttachTo(frame?.Scope.Node ?? manager.ChainNode);
            return UnitOfWorkScope.CreateOwner(
                manager,
                unitOfWork,
                provider,
                options,
                node,
                tenantSnapshot,
                parentFrame: frame,
                executionParent,
                ownedScope: null,
                ambientToken: null,
                ownsUnitOfWorkDirectly: manager.OwnsResolvedUnitOfWorks);
        }

        internal static UnitOfWorkScope BeginIsolated(
            UnitOfWorkManager manager,
            UnitOfWorkManager.AmbientFrame parentFrame,
            UnitOfWorkScope? executionParent,
            UnitOfWorkOptions options)
        {
            var provider = UnitOfWorkScopePreflight.ResolveProvider(manager, options.Provider);
            UnitOfWorkScopePreflight.ValidateIsolationCapability(manager, provider, options);
            var binding = manager.Bindings?.GetRequired(provider);

            if (binding is not null && !binding.SupportsRequiresNew)
            {
                throw new NotSupportedException(
                    $"The registered '{provider}' provider does not support requiresNew isolation: " +
                    "it cannot bind already-injected business dependencies to an independent transaction context. " +
                    "Do not request requiresNew for this provider.");
            }

            if (manager.ScopeFactory is null)
            {
                // Manual construction without DI scope support (unit tests / custom hosts).
                // The custom factory is expected to return an independent instance per call.
                var fallback = manager.Factory.Create(provider);
                var manualNode = new UnitOfWorkChainNode();
                manualNode.AttachTo(parentFrame.Scope.Node);
                return UnitOfWorkScope.CreateOwner(
                    manager,
                    fallback,
                    provider,
                    options,
                    manualNode,
                    UnitOfWorkScopePreflight.ResolveTenantSnapshot(manager, provider, manager.ServiceProvider),
                    parentFrame: parentFrame,
                    executionParent,
                    ownedScope: null,
                    ambientToken: null,
                    ownsUnitOfWorkDirectly: manager.OwnsResolvedUnitOfWorks);
            }

            var childScope = manager.ScopeFactory.CreateScope();
            IDisposable? ambientToken = null;
            try
            {
                var tenantSnapshot = UnitOfWorkScopePreflight.ResolveTenantSnapshot(manager, provider, childScope.ServiceProvider);

                // Push the isolated ambient context BEFORE constructing the unit of
                // work: construction must capture the child scope's own resources,
                // never be redirected to a parent context via ambient routing.
                if (binding!.AmbientContextFactory is not null)
                {
                    var ambientContext = binding.AmbientContextFactory(childScope.ServiceProvider)
                        ?? throw new InvalidOperationException(
                            $"The '{provider}' ambient context factory returned null; requiresNew isolation " +
                            "requires a resolvable resource context.");
                    var childNode = childScope.ServiceProvider.GetRequiredService<UnitOfWorkChainNode>();
                    childNode.AttachTo(parentFrame.Scope.Node);
                    ambientToken = UnitOfWorkAmbientContext.Push(ambientContext, childNode, tenantSnapshot);
                }

                var childFactory = childScope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();
                var unitOfWork = childFactory.Create(provider);

                return UnitOfWorkScope.CreateOwner(
                    manager,
                    unitOfWork,
                    provider,
                    options,
                    childScope.ServiceProvider.GetRequiredService<UnitOfWorkChainNode>(),
                    tenantSnapshot,
                    parentFrame: parentFrame,
                    executionParent,
                    ownedScope: childScope,
                    ambientToken: ambientToken,
                    ownsUnitOfWorkDirectly: false);
            }
            catch
            {
                ambientToken?.Dispose();
                childScope.Dispose();
                throw;
            }
        }
    }
}
