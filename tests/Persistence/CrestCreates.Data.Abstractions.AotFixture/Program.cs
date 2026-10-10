using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.Domain.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Data.Abstractions.AotFixture;

/// <summary>
/// NativeAOT 装配门禁：使用正式注册 API（AddUnitOfWork / AddUnitOfWorkProvider）
/// 与窄范围静态测试工作单元，验证唯一工厂/管理器的 Provider 选择、生命周期执行
/// 与缺注册/冲突诊断在原生产物中真实工作。
/// 本 fixture 不证明任何 ORM SDK 的 AOT 能力。
/// </summary>
internal static class Program
{
    private static readonly UnitOfWorkOptions RequiresNewOptions = new()
    {
        Propagation = UnitOfWorkPropagation.RequiresNew
    };

    private static int Main() => RunAsync().GetAwaiter().GetResult();

    private static async Task<int> RunAsync()
    {
        try
        {
            await VerifySingleBindingSelectionAndLifecycleAsync();
            await VerifyRequiresNewIsolationAsync();
            VerifyMissingBindingDiagnostic();
            VerifyDuplicateBindingDiagnostic();
            VerifyMissingDefaultDiagnostic();
            await VerifyExecuteRollbackPreservesOriginalExceptionAsync();

            Console.WriteLine("CRESTCREATES_UNITOFWORK_NATIVE_PIPELINE_OK");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"UnitOfWork AOT fixture failure: {exception}");
            return 1;
        }
    }

    private static async Task VerifySingleBindingSelectionAndLifecycleAsync()
    {
        using var provider = BuildProvider(services =>
            services.AddUnitOfWorkProvider(
                OrmProvider.EfCore,
                static sp => sp.GetRequiredService<StaticUnitOfWork>(),
                supportsRequiresNew: false));

        var scope = provider.CreateScope();
        StaticUnitOfWork unitOfWork;
        try
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

            await using (var unitOfWorkScope = manager.BeginScope())
            {
                unitOfWork = unitOfWorkScope.UnitOfWork as StaticUnitOfWork
                    ?? throw new InvalidOperationException("The single binding must supply the default provider.");
                Check(unitOfWorkScope.IsOwner, "first BeginScope must own the unit of work");
                await unitOfWorkScope.StartAsync();
                await unitOfWorkScope.CompleteAsync();
                Check(unitOfWorkScope.State == UnitOfWorkState.Completed,
                    "a completed scope must record the completed participation state");
                Check(unitOfWorkScope.TransactionOutcome == UnitOfWorkTransactionOutcome.Committed,
                    "a committed scope must record the committed transaction outcome");
                Check(!unitOfWorkScope.IsReleased, "results must be readable before release");
            }

            Check(manager.CurrentOrNull is null, "ambient state must be cleared after dispose");
            Check(unitOfWork.BeginCount == 1, "begin transaction must reach the bound unit of work");
            Check(unitOfWork.CommitCount == 1, "commit transaction must reach the bound unit of work");
            Check(unitOfWork.DisposeCount == 0,
                "the DI container owns the scoped unit of work while the scope is alive");
        }
        finally
        {
            scope.Dispose();
        }

        Check(unitOfWork.DisposeCount == 1,
            "the container must dispose the scoped unit of work exactly once (no double owner)");
    }

    private static async Task VerifyRequiresNewIsolationAsync()
    {
        using var provider = BuildProvider(services =>
            services.AddUnitOfWorkProvider(
                OrmProvider.FreeSql,
                static sp => new StaticUnitOfWork(sp.GetRequiredService<ScopeToken>().Id),
                supportsRequiresNew: true,
                ambientContextFactory: static sp => new FixtureDataBaseContext(sp.GetRequiredService<ScopeToken>().Id)));

        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

        await using (var outerScope = manager.BeginScope())
        {
            await outerScope.StartAsync();
            var outerUnitOfWork = (StaticUnitOfWork)outerScope.UnitOfWork;
            await using (var innerScope = manager.BeginScope(RequiresNewOptions))
            {
                await innerScope.StartAsync();
                var innerUnitOfWork = (StaticUnitOfWork)innerScope.UnitOfWork;
                Check(innerUnitOfWork != outerUnitOfWork, "requiresNew must create a distinct unit of work");
                Check(innerUnitOfWork.ScopeId != outerUnitOfWork.ScopeId,
                    "requiresNew must isolate through a child DI scope");
                Check(!ReferenceEquals(UnitOfWorkAmbientContext.Current, null),
                    "requiresNew must push the provider-declared ambient context");
                Check(manager.Current == innerUnitOfWork, "inner scope must become current while active");
            }

            Check(manager.Current == outerUnitOfWork, "disposing the inner scope must restore the parent");
            Check(UnitOfWorkAmbientContext.Current is null,
                "the ambient context must be restored after the inner scope");
        }

        await Task.CompletedTask;
    }

    private static void VerifyMissingBindingDiagnostic()
    {
        using var provider = BuildProvider(services =>
            services.AddUnitOfWorkProvider(
                OrmProvider.EfCore,
                static sp => sp.GetRequiredService<StaticUnitOfWork>(),
                supportsRequiresNew: false));

        using var scope = provider.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();

        var exception = CaptureInvalidOperation(() => factory.Create(OrmProvider.SqlSugar));
        Check(exception.Contains("'SqlSugar'", StringComparison.Ordinal),
            "missing binding diagnostic must name the requested provider");
        Check(exception.Contains("Registered providers: EfCore", StringComparison.Ordinal),
            "missing binding diagnostic must list registered providers");
    }

    private static void VerifyDuplicateBindingDiagnostic()
    {
        using var provider = BuildProvider(services =>
        {
            services.AddUnitOfWorkProvider(
                OrmProvider.EfCore,
                static sp => sp.GetRequiredService<StaticUnitOfWork>(),
                supportsRequiresNew: false);
            services.AddUnitOfWorkProvider(
                OrmProvider.EfCore,
                static sp => sp.GetRequiredService<StaticUnitOfWork>(),
                supportsRequiresNew: false);
        });

        using var scope = provider.CreateScope();
        var exception = CaptureInvalidOperation(() =>
            scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>());
        Check(exception.Contains("Duplicate unit-of-work binding", StringComparison.Ordinal),
            "duplicate bindings must fail deterministically instead of last-wins");
    }

    private static void VerifyMissingDefaultDiagnostic()
    {
        using var provider = BuildProvider(services =>
        {
            services.AddUnitOfWorkProvider(
                OrmProvider.EfCore,
                static sp => sp.GetRequiredService<StaticUnitOfWork>(),
                supportsRequiresNew: false);
            services.AddUnitOfWorkProvider(
                OrmProvider.SqlSugar,
                static sp => sp.GetRequiredService<StaticUnitOfWork>(),
                supportsRequiresNew: false);
        });

        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

        var exception = CaptureInvalidOperation(() => manager.BeginScope());
        Check(exception.Contains("No default ORM provider", StringComparison.Ordinal),
            "an unspecified provider with multiple bindings must fail deterministically");
    }

    private static async Task VerifyExecuteRollbackPreservesOriginalExceptionAsync()
    {
        using var provider = BuildProvider(services =>
            services.AddUnitOfWorkProvider(
                OrmProvider.EfCore,
                static sp => sp.GetRequiredService<StaticUnitOfWork>(),
                supportsRequiresNew: false));

        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<StaticUnitOfWork>();

        string observed;
        try
        {
            await manager.ExecuteAsync<bool>(_ => throw new InvalidOperationException("fixture-business-failure"));
            throw new InvalidOperationException("ExecuteAsync must surface the business failure.");
        }
        catch (InvalidOperationException exception)
        {
            observed = exception.Message;
        }

        Check(observed == "fixture-business-failure",
            "the original business exception must not be replaced by cleanup failures");
        Check(unitOfWork.RollbackCount == 1, "the failed execution must roll back the unit of work");
        Check(manager.CurrentOrNull is null, "the failed execution must restore ambient state");
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection> configureProviders)
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopeToken>();
        services.AddScoped<StaticUnitOfWork>();
        services.AddUnitOfWork();
        configureProviders(services);
        return services.BuildServiceProvider();
    }

    private static string CaptureInvalidOperation(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }

        throw new InvalidOperationException("Expected an InvalidOperationException but the action succeeded.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"AOT fixture assertion failed: {message}");
        }
    }

    private sealed class ScopeToken
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    /// <summary>最小 IDataBaseContext 探针（仅 ambient 身份用途）。</summary>
    private sealed class FixtureDataBaseContext : CrestCreates.DbContextProvider.Abstract.IDataBaseContext
    {
        public FixtureDataBaseContext(Guid scopeId)
        {
            ScopeId = scopeId;
        }

        public Guid ScopeId { get; }

        public OrmProvider Provider => OrmProvider.EfCore;

        public CrestCreates.DbContextProvider.Abstract.IDataBaseTransaction? CurrentTransaction => null;

        public string? ConnectionString => null;

        public CrestCreates.DbContextProvider.Abstract.IDataBaseSet<TEntity> Set<TEntity>() where TEntity : class
            => throw new NotSupportedException();

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CrestCreates.DbContextProvider.Abstract.IDataBaseTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public CrestCreates.DbContextProvider.Abstract.IQueryableBuilder<TEntity> Queryable<TEntity>() where TEntity : class
            => throw new NotSupportedException();

        public object GetNativeContext() => this;

        public void Dispose()
        {
        }
    }

    private sealed class StaticUnitOfWork : IUnitOfWork
    {
        private bool _disposed;

        public StaticUnitOfWork()
        {
        }

        public StaticUnitOfWork(Guid scopeId)
        {
            ScopeId = scopeId;
        }

        public Guid ScopeId { get; }

        public int BeginCount { get; private set; }

        public int CommitCount { get; private set; }

        public int RollbackCount { get; private set; }

        public int DisposeCount { get; private set; }

        public Task BeginTransactionAsync(UnitOfWorkBeginOptions options, CancellationToken cancellationToken = default)
        {
            BeginCount++;
            return Task.CompletedTask;
        }

        public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            CommitCount++;
            return Task.CompletedTask;
        }

        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
        {
            RollbackCount++;
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DisposeCount++;
        }
    }
}
