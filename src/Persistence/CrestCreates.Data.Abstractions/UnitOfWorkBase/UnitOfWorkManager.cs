using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// 工作单元管理器：环境栈、嵌套复用与 requiresNew 隔离的唯一权威。
    /// </summary>
    /// <remarks>
    /// <para>Provider 选择规则：调用方显式 provider → 应用显式默认（AddUnitOfWork(defaultProvider)）→
    /// 恰好一个绑定即默认 → 多绑定无默认时确定性失败。</para>
    /// <para>requiresNew（存在环境时）：通过受管子 DI scope 获取独立工作单元/DbContext/连接；期间把绑定的
    /// 当前上下文推入 <see cref="UnitOfWorkAmbientContext"/>，使已注入的业务依赖（仓储/DbContext 适配器）
    /// 跟随当前 UoW；子 scope 结束恢复父 Current 与环境上下文。声明不支持 requiresNew 的 Provider 给出确定性诊断。</para>
    /// <para>Dispose owner：绑定注册路径下工作单元由 DI 作用域持有，manager 只释放自己创建的受管子作用域
    /// （容器随作用域释放其中的 UoW 一次）；手动构造路径（自定义工厂/单测）下 manager 直接释放其创建的工作单元。
    /// 复用的内层 scope 不释放、不提交外层。</para>
    /// </remarks>
    public class UnitOfWorkManager : IUnitOfWorkManager
    {
        private readonly IUnitOfWorkFactory _factory;
        private readonly UnitOfWorkProviderBindingRegistry? _bindings;
        private readonly IServiceScopeFactory? _scopeFactory;
        private readonly OrmProvider? _explicitDefault;
        private readonly bool _ownsResolvedUnitOfWorks;
        private readonly AsyncLocal<AmbientUnitOfWorkScope?> _currentScope = new();

        /// <summary>
        /// DI 主链构造函数
        /// </summary>
        /// <param name="factory">当前作用域的工作单元工厂</param>
        /// <param name="bindings">装配完成的 Provider 绑定索引</param>
        /// <param name="scopeFactory">用于 requiresNew 子作用域的 scope 工厂</param>
        /// <param name="explicitDefault">应用显式声明的默认 Provider（可为 null）</param>
        public UnitOfWorkManager(
            IUnitOfWorkFactory factory,
            UnitOfWorkProviderBindingRegistry bindings,
            IServiceScopeFactory scopeFactory,
            OrmProvider? explicitDefault = null)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _explicitDefault = explicitDefault;
            _ownsResolvedUnitOfWorks = false;
        }

        /// <summary>
        /// 手动构造（自定义工厂 / 单元测试）：不使用绑定索引与子作用域，
        /// requiresNew 语义取决于自定义工厂是否每次返回独立实例。
        /// </summary>
        /// <param name="factory">工作单元工厂</param>
        /// <param name="defaultProvider">默认 ORM 提供者</param>
        public UnitOfWorkManager(IUnitOfWorkFactory factory, OrmProvider defaultProvider = OrmProvider.EfCore)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _explicitDefault = defaultProvider;
            _ownsResolvedUnitOfWorks = true;
        }

        /// <summary>
        /// 获取当前工作单元
        /// </summary>
        /// <exception cref="InvalidOperationException">当前没有活动的工作单元</exception>
        public IUnitOfWork? CurrentOrNull => _currentScope.Value?.UnitOfWork;

        public IUnitOfWork Current
        {
            get
            {
                var current = CurrentOrNull;
                if (current == null)
                {
                    throw new InvalidOperationException("No active unit of work. Call Begin() first.");
                }
                return current;
            }
        }

        public IUnitOfWorkScope BeginScope(
            bool isTransactional = true,
            bool requiresNew = false,
            OrmProvider? provider = null)
        {
            var current = CurrentOrNull;
            if (current != null && !requiresNew)
            {
                return new UnitOfWorkScope(
                    this, current, _currentScope.Value,
                    isOwner: false, isTransactional,
                    ownedScope: null, ambientToken: null, ownsUnitOfWorkDirectly: false);
            }

            var parentScope = _currentScope.Value;

            if (current != null)
            {
                return BeginIsolatedScope(parentScope!, isTransactional, provider);
            }

            // No ambient unit of work: resolve from the caller's scope so that
            // services resolved in the same scope (repositories, DbContext)
            // participate in this unit of work.
            var unitOfWork = _factory.Create(ResolveProvider(provider));
            _currentScope.Value = new AmbientUnitOfWorkScope(unitOfWork, parentScope);
            return new UnitOfWorkScope(
                this, unitOfWork, parentScope,
                isOwner: true, isTransactional,
                ownedScope: null, ambientToken: null,
                ownsUnitOfWorkDirectly: _ownsResolvedUnitOfWorks);
        }

        /// <summary>
        /// 开始新的工作单元
        /// </summary>
        /// <param name="provider">ORM 提供者，null 使用解析规则确定的默认提供者</param>
        /// <returns>新的工作单元实例</returns>
        public IUnitOfWork Begin(OrmProvider? provider = null)
        {
            return new ScopedUnitOfWorkProxy(BeginScope(requiresNew: true, provider: provider));
        }

        /// <summary>
        /// 同步执行操作
        /// </summary>
        /// <typeparam name="TResult">返回值类型</typeparam>
        /// <param name="action">要执行的操作</param>
        /// <param name="provider">ORM 提供者</param>
        /// <returns>操作结果</returns>
        public TResult Execute<TResult>(Func<IUnitOfWork, TResult> action, OrmProvider? provider = null)
        {
            using (var scope = BeginScope(requiresNew: true, provider: provider))
            {
                try
                {
                    scope.UnitOfWork.BeginTransactionAsync().GetAwaiter().GetResult();
                    var result = action(scope.UnitOfWork);
                    scope.UnitOfWork.CommitTransactionAsync().GetAwaiter().GetResult();
                    return result;
                }
                catch
                {
                    TryRollback(scope.UnitOfWork);
                    throw;
                }
            }
        }

        /// <summary>
        /// 异步执行操作
        /// </summary>
        /// <typeparam name="TResult">返回值类型</typeparam>
        /// <param name="action">要执行的异步操作</param>
        /// <param name="provider">ORM 提供者</param>
        /// <returns>操作结果的异步任务</returns>
        public async Task<TResult> ExecuteAsync<TResult>(
            Func<IUnitOfWork, Task<TResult>> action,
            OrmProvider? provider = null)
        {
            using (var scope = BeginScope(requiresNew: true, provider: provider))
            {
                try
                {
                    await scope.UnitOfWork.BeginTransactionAsync();
                    var result = await action(scope.UnitOfWork);
                    await scope.UnitOfWork.CommitTransactionAsync();
                    return result;
                }
                catch
                {
                    await TryRollbackAsync(scope.UnitOfWork);
                    throw;
                }
            }
        }

        private IUnitOfWorkScope BeginIsolatedScope(
            AmbientUnitOfWorkScope parentScope,
            bool isTransactional,
            OrmProvider? provider)
        {
            var resolvedProvider = ResolveProvider(provider);
            var binding = _bindings?.GetRequired(resolvedProvider);

            if (binding is not null && !binding.SupportsRequiresNew)
            {
                throw new NotSupportedException(
                    $"The registered '{resolvedProvider}' provider does not support requiresNew isolation: " +
                    "it cannot bind already-injected business dependencies to an independent transaction context. " +
                    "Do not request requiresNew for this provider.");
            }

            if (_scopeFactory is null)
            {
                // Manual construction without DI scope support (unit tests / custom hosts).
                // The custom factory is expected to return an independent instance per call.
                var fallback = _factory.Create(resolvedProvider);
                _currentScope.Value = new AmbientUnitOfWorkScope(fallback, parentScope);
                return new UnitOfWorkScope(
                    this, fallback, parentScope,
                    isOwner: true, isTransactional,
                    ownedScope: null, ambientToken: null,
                    ownsUnitOfWorkDirectly: _ownsResolvedUnitOfWorks);
            }

            var childScope = _scopeFactory.CreateScope();
            IDisposable? ambientToken = null;
            try
            {
                // Push the isolated ambient context BEFORE constructing the unit of
                // work: construction must capture the child scope's own resources,
                // never be redirected to a parent context via ambient routing.
                if (binding!.AmbientContextFactory is not null)
                {
                    ambientToken = UnitOfWorkAmbientContext.Push(
                        binding.AmbientContextFactory(childScope.ServiceProvider));
                }

                var childFactory = childScope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();
                var unitOfWork = childFactory.Create(resolvedProvider);

                _currentScope.Value = new AmbientUnitOfWorkScope(unitOfWork, parentScope);
                return new UnitOfWorkScope(
                    this, unitOfWork, parentScope,
                    isOwner: true, isTransactional,
                    ownedScope: childScope, ambientToken: ambientToken,
                    ownsUnitOfWorkDirectly: false);
            }
            catch
            {
                ambientToken?.Dispose();
                childScope.Dispose();
                throw;
            }
        }

        private OrmProvider ResolveProvider(OrmProvider? requested)
        {
            if (_bindings is not null)
            {
                return _bindings.ResolveProvider(requested, _explicitDefault);
            }

            return requested ?? _explicitDefault ?? OrmProvider.EfCore;
        }

        private static void TryRollback(IUnitOfWork unitOfWork)
        {
            try
            {
                unitOfWork.RollbackTransactionAsync().GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                // Cleanup failures must not replace the original business exception.
            }
        }

        private static async Task TryRollbackAsync(IUnitOfWork unitOfWork)
        {
            try
            {
                await unitOfWork.RollbackTransactionAsync();
            }
            catch (Exception)
            {
                // Cleanup failures must not replace the original business exception.
            }
        }

        private void EndScope(UnitOfWorkScope scope)
        {
            if (!scope.IsOwner)
            {
                return;
            }

            if (!ReferenceEquals(CurrentOrNull, scope.UnitOfWork))
            {
                throw new InvalidOperationException("The unit of work scope was disposed out of order.");
            }

            _currentScope.Value = scope.ParentScope;

            // Restore the ambient context first so code observing it after the
            // scope never sees a context that is about to be disposed.
            scope.AmbientToken?.Dispose();
            try
            {
                if (scope.OwnsUnitOfWorkDirectly)
                {
                    scope.UnitOfWork.Dispose();
                }
                else if (scope.UnitOfWork is IUnitOfWorkTransactionAbortable abortable)
                {
                    // DI owns the object; the manager only terminates a pending
                    // transaction so an abandoned scope cannot leave an open
                    // transaction behind in the same request scope.
                    TryAbortPendingTransaction(abortable);
                }
            }
            finally
            {
                // DI-tracked unit of work instances (binding path) are released
                // exactly once by the scope that created them, never by both.
                scope.OwnedScope?.Dispose();
            }
        }

        private static void TryAbortPendingTransaction(IUnitOfWorkTransactionAbortable abortable)
        {
            try
            {
                abortable.AbortPendingTransaction();
            }
            catch (Exception)
            {
                // Scope unwinding must not be masked by cleanup failures.
            }
        }

        private sealed class AmbientUnitOfWorkScope
        {
            public AmbientUnitOfWorkScope(IUnitOfWork unitOfWork, AmbientUnitOfWorkScope? parent)
            {
                UnitOfWork = unitOfWork;
                Parent = parent;
            }

            public IUnitOfWork UnitOfWork { get; }

            public AmbientUnitOfWorkScope? Parent { get; }
        }

        private sealed class UnitOfWorkScope : IUnitOfWorkScope
        {
            private readonly UnitOfWorkManager _manager;
            private bool _disposed;

            public UnitOfWorkScope(
                UnitOfWorkManager manager,
                IUnitOfWork unitOfWork,
                AmbientUnitOfWorkScope? parentScope,
                bool isOwner,
                bool isTransactional,
                IServiceScope? ownedScope,
                IDisposable? ambientToken,
                bool ownsUnitOfWorkDirectly)
            {
                _manager = manager;
                UnitOfWork = unitOfWork;
                ParentScope = parentScope;
                IsOwner = isOwner;
                IsTransactional = isTransactional;
                OwnedScope = ownedScope;
                AmbientToken = ambientToken;
                OwnsUnitOfWorkDirectly = ownsUnitOfWorkDirectly;
            }

            public IUnitOfWork UnitOfWork { get; }

            public AmbientUnitOfWorkScope? ParentScope { get; }

            public bool IsOwner { get; }

            public bool IsTransactional { get; }

            public IServiceScope? OwnedScope { get; }

            public IDisposable? AmbientToken { get; }

            public bool OwnsUnitOfWorkDirectly { get; }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _manager.EndScope(this);
                _disposed = true;
            }
        }

        private sealed class ScopedUnitOfWorkProxy : IUnitOfWork
        {
            private readonly IUnitOfWorkScope _scope;

            public ScopedUnitOfWorkProxy(IUnitOfWorkScope scope)
            {
                _scope = scope;
            }

            public Task BeginTransactionAsync()
            {
                return _scope.UnitOfWork.BeginTransactionAsync();
            }

            public Task CommitTransactionAsync()
            {
                return _scope.UnitOfWork.CommitTransactionAsync();
            }

            public Task RollbackTransactionAsync()
            {
                return _scope.UnitOfWork.RollbackTransactionAsync();
            }

            public Task<int> SaveChangesAsync()
            {
                return _scope.UnitOfWork.SaveChangesAsync();
            }

            public void Dispose()
            {
                _scope.Dispose();
            }
        }
    }
}
