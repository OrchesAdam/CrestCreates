using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Aop.Interceptors;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.EFCore.DbContexts;
using CrestCreates.Data.EFCore.Extensions;
using CrestCreates.Data.EFCore.Repositories;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.OrmProviders.Tests;

/// <summary>
/// [UnitOfWorkMo] 正式织入路径验收（切片 1 独立成立，不依赖切片 2、不向测试动作注入 ExecutionToken）：
/// 1) 织入方法体必须看到激活（Current/ambient 对方法体可见）；
/// 2) 调用方 CT 不取消、仅 UoW deadline 到期时，方法把 CT 参数传给正式仓储，
///    等待取消的数据库动作收到取消且事务清理完成（操作层 token 组合）。
/// </summary>
public class AopUnitOfWorkAcceptanceTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"crest-uow-aop-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        foreach (var suffix in new string[] { string.Empty, "-wal", "-shm" })
        {
            var path = _databasePath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task Woven_Method_Body_Sees_Ambient_And_Commit_Reaches_The_Repository()
    {
        var (provider, _, recorder) = BuildProvider(waitForCancellation: false);
        Guid entityId;

        using (var scope = provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<WovenProbeService>();
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

            entityId = await service.InsertProbeAsync("woven-commit");

            recorder.AmbientSeenDuringBody.Should().BeTrue(
                "the woven method body must observe the ambient activation established by the interceptor");
            manager.CurrentOrNull.Should().BeNull(
                "the interceptor must complete and release the scope when the woven method returns");
        }

        using var verifyScope = provider.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<UowAopDbContext>();
        (await verifyContext.Probes.FindAsync(entityId)).Should().NotBeNull(
            "the woven path must commit the repository write through the unified kernel");
    }

    [Fact]
    public async Task Aop_Method_Ct_Parameter_Is_Combined_With_The_Kernel_Deadline()
    {
        var (provider, interceptor, _) = BuildProvider(waitForCancellation: true);

        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var service = scope.ServiceProvider.GetRequiredService<WovenProbeService>();

        using var callerCts = new CancellationTokenSource();
        var act = async () => await manager.ExecuteAsync(
            async kernelToken =>
            {
                // The method receives a plain (non-kernel) caller token and forwards it to the
                // official repository; only the operation-layer composition can deliver the deadline.
                await service.WaitWithCallerTokenAsync(callerCts.Token);
                return true;
            },
            new UnitOfWorkOptions { Timeout = TimeSpan.FromMilliseconds(250) },
            CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
        callerCts.IsCancellationRequested.Should().BeFalse(
            "the caller token must not be the source of the cancellation");
        interceptor.CancellationObserved.Should().BeTrue(
            "the waiting database action must observe the unit-of-work deadline through token composition");
        interceptor.CompletedWithoutCancellation.Should().BeFalse();
        manager.CurrentOrNull.Should().BeNull("cleanup must restore ambient state");

        var dbContext = scope.ServiceProvider.GetRequiredService<UowAopDbContext>();
        dbContext.Database.CurrentTransaction.Should().BeNull(
            "the failed unit of work must terminate its transaction promptly");
    }

    [Fact]
    public async Task Aop_Method_Without_Ct_Parameter_Still_Observes_The_Kernel_Deadline()
    {
        var (provider, interceptor, _) = BuildProvider(waitForCancellation: true);

        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var service = scope.ServiceProvider.GetRequiredService<WovenProbeService>();

        var act = async () => await manager.ExecuteAsync(
            async kernelToken =>
            {
                await service.WaitWithoutCallerTokenAsync();
                return true;
            },
            new UnitOfWorkOptions { Timeout = TimeSpan.FromMilliseconds(250) },
            CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
        interceptor.CancellationObserved.Should().BeTrue(
            "a woven method without a CT parameter must still receive deadline coverage through the official repository");
        interceptor.CompletedWithoutCancellation.Should().BeFalse();
    }

    private (ServiceProvider Provider, WaitingCommandInterceptor Interceptor, ActivationRecorder Recorder) BuildProvider(
        bool waitForCancellation)
    {
        var interceptor = new WaitingCommandInterceptor { WaitForCancellation = waitForCancellation };
        var recorder = new ActivationRecorder();

        var services = new ServiceCollection();
        services.AddDbContext<UowAopDbContext>(options =>
            options.UseSqlite($"Data Source={_databasePath}").AddInterceptors(interceptor));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<UowAopDbContext>());
        services.AddScoped<IEntityFrameworkCoreDbContext>(sp =>
            new EfCoreDbContextAdapter(sp.GetRequiredService<UowAopDbContext>()));
        services.AddScoped<IDataBaseContext>(sp => sp.GetRequiredService<IEntityFrameworkCoreDbContext>());
        services.AddScoped(sp => new EfCoreRepository<UowAopEntity, Guid>(sp.GetRequiredService<IDataBaseContext>()));
        services.AddSingleton<IDomainEventPublisher>(new NoOpDomainEventPublisher());
        services.AddSingleton(recorder);
        services.AddScoped<WovenProbeService>();
        services.AddUnitOfWork();
        services.AddEfCoreUnitOfWork();

        var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<UowAopDbContext>().Database.EnsureCreated();
        }

        return (provider, interceptor, recorder);
    }

    public sealed class ActivationRecorder
    {
        public bool AmbientSeenDuringBody { get; set; }
    }

    /// <summary>被 [UnitOfWorkMo] 织入的探针服务（测试程序集由 Rougamo 织入）。</summary>
    public sealed class WovenProbeService
    {
        private readonly EfCoreRepository<UowAopEntity, Guid> _repository;
        private readonly IUnitOfWorkManager _manager;
        private readonly ActivationRecorder _recorder;

        public WovenProbeService(
            EfCoreRepository<UowAopEntity, Guid> repository,
            IUnitOfWorkManager manager,
            ActivationRecorder recorder,
            IServiceProvider serviceProvider)
        {
            _repository = repository;
            _manager = manager;
            _recorder = recorder;
            ServiceProvider = serviceProvider;
        }

        /// <summary>
        /// 与 CrestAppServiceBase 相同的解析入口：拦截器经该属性取得当前作用域的服务提供者。
        /// </summary>
        public IServiceProvider? ServiceProvider { get; }

        [UnitOfWorkMo]
        public async Task<Guid> InsertProbeAsync(string name)
        {
            _recorder.AmbientSeenDuringBody = _manager.CurrentOrNull is not null;
            var entity = new UowAopEntity(Guid.NewGuid(), name);
            await _repository.InsertAsync(entity);
            return entity.Id;
        }

        [UnitOfWorkMo]
        public Task WaitWithCallerTokenAsync(CancellationToken cancellationToken)
        {
            return _repository.GetListAsync(cancellationToken);
        }

        [UnitOfWorkMo]
        public Task WaitWithoutCallerTokenAsync()
        {
            return _repository.GetListAsync();
        }
    }

    public sealed class UowAopDbContext : DbContext
    {
        public UowAopDbContext(DbContextOptions<UowAopDbContext> options)
            : base(options)
        {
        }

        public DbSet<UowAopEntity> Probes => Set<UowAopEntity>();
    }

    public sealed class UowAopEntity : AggregateRoot<Guid>
    {
        public UowAopEntity()
        {
        }

        public UowAopEntity(Guid id, string name)
        {
            Id = id;
            Name = name;
        }

        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// 等待取消的数据库动作：命令执行时挂起直到有效 token 取消；若在限定时间内未收到取消则放行并标记。
    /// </summary>
    public sealed class WaitingCommandInterceptor : DbCommandInterceptor
    {
        private static readonly TimeSpan BoundedWait = TimeSpan.FromSeconds(5);

        public bool WaitForCancellation { get; set; }

        public bool CancellationObserved { get; private set; }

        public bool CompletedWithoutCancellation { get; private set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (!WaitForCancellation)
            {
                return result;
            }

            try
            {
                await Task.Delay(BoundedWait, cancellationToken);
                CompletedWithoutCancellation = true;
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }

            return result;
        }
    }

    private sealed class NoOpDomainEventPublisher : IDomainEventPublisher
    {
        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent
            => Task.CompletedTask;
    }
}
