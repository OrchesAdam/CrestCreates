using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.UnitOfWork;

namespace CrestCreates.OrmProviders.Abstract
{
    /// <summary>
    /// 工作单元管理器基类实现
    /// </summary>
    /// <remarks>
    /// 提供工作单元生命周期管理的默认实现
    /// 支持嵌套工作单元和自动事务管理
    /// </remarks>
    public class UnitOfWorkManager : IUnitOfWorkManager
    {
        private readonly IUnitOfWorkFactory _factory;
        private readonly OrmProvider _defaultProvider;
        private readonly AsyncLocal<AmbientUnitOfWorkScope?> _currentScope = new();

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="factory">工作单元工厂</param>
        /// <param name="defaultProvider">默认 ORM 提供者</param>
        public UnitOfWorkManager(IUnitOfWorkFactory factory, OrmProvider defaultProvider = OrmProvider.EfCore)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _defaultProvider = defaultProvider;
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
                return new UnitOfWorkScope(this, current, _currentScope.Value, isOwner: false, isTransactional);
            }

            var parentScope = _currentScope.Value;
            var unitOfWork = _factory.Create(provider ?? _defaultProvider);
            _currentScope.Value = new AmbientUnitOfWorkScope(unitOfWork, parentScope);

            return new UnitOfWorkScope(this, unitOfWork, parentScope, isOwner: true, isTransactional);
        }

        /// <summary>
        /// 开始新的工作单元
        /// </summary>
        /// <param name="provider">ORM 提供者，null 使用默认提供者</param>
        /// <returns>新的工作单元实例</returns>
        /// <exception cref="InvalidOperationException">已存在活动的工作单元</exception>
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
                    scope.UnitOfWork.RollbackTransactionAsync().GetAwaiter().GetResult();
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
                    await scope.UnitOfWork.RollbackTransactionAsync();
                    throw;
                }
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
            scope.UnitOfWork.Dispose();
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
                bool isTransactional)
            {
                _manager = manager;
                UnitOfWork = unitOfWork;
                ParentScope = parentScope;
                IsOwner = isOwner;
                IsTransactional = isTransactional;
            }

            public IUnitOfWork UnitOfWork { get; }

            public AmbientUnitOfWorkScope? ParentScope { get; }

            public bool IsOwner { get; }

            public bool IsTransactional { get; }

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
