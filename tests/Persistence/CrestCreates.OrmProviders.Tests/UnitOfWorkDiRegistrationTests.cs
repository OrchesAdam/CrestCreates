using System;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.Domain.UnitOfWork;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.OrmProviders.Tests;

/// <summary>
/// 唯一装配路径的真实 DI 验证：默认 Provider 选择规则、重复/冲突诊断、
/// requiresNew 子作用域隔离与父状态恢复、依赖失败不污染 Current。
/// </summary>
public class UnitOfWorkDiRegistrationTests
{
    [Fact]
    public void Single_binding_supplies_default_provider_when_unspecified()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopeMarker>();
        services.AddUnitOfWork();
        services.AddUnitOfWorkProvider(OrmProvider.FreeSql, CreateProbeBinding(OrmProvider.FreeSql));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

        using var unitOfWorkScope = manager.BeginScope();

        ((ProbeUnitOfWork)unitOfWorkScope.UnitOfWork).Provider.Should().Be(OrmProvider.FreeSql);
    }

    [Fact]
    public void Explicit_default_declaration_selects_provider_among_multiple_bindings()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopeMarker>();
        services.AddUnitOfWork(OrmProvider.SqlSugar);
        services.AddUnitOfWorkProvider(OrmProvider.EfCore, CreateProbeBinding(OrmProvider.EfCore));
        services.AddUnitOfWorkProvider(OrmProvider.SqlSugar, CreateProbeBinding(OrmProvider.SqlSugar));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

        using var unitOfWorkScope = manager.BeginScope();
        ((ProbeUnitOfWork)unitOfWorkScope.UnitOfWork).Provider.Should().Be(OrmProvider.SqlSugar);
    }

    [Fact]
    public void Explicit_provider_argument_overrides_declared_default()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopeMarker>();
        services.AddUnitOfWork(OrmProvider.SqlSugar);
        services.AddUnitOfWorkProvider(OrmProvider.EfCore, CreateProbeBinding(OrmProvider.EfCore));
        services.AddUnitOfWorkProvider(OrmProvider.SqlSugar, CreateProbeBinding(OrmProvider.SqlSugar));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

        using var unitOfWorkScope = manager.BeginScope(provider: OrmProvider.EfCore);
        ((ProbeUnitOfWork)unitOfWorkScope.UnitOfWork).Provider.Should().Be(OrmProvider.EfCore);
    }

    [Fact]
    public void Multiple_bindings_without_default_fail_deterministically()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopeMarker>();
        services.AddUnitOfWork();
        services.AddUnitOfWorkProvider(OrmProvider.EfCore, CreateProbeBinding(OrmProvider.EfCore));
        services.AddUnitOfWorkProvider(OrmProvider.SqlSugar, CreateProbeBinding(OrmProvider.SqlSugar));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

        var act = () => manager.BeginScope();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No default ORM provider*EfCore*SqlSugar*");
        manager.CurrentOrNull.Should().BeNull();
    }

    [Fact]
    public void Missing_binding_fails_deterministically_with_registered_providers()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopeMarker>();
        services.AddUnitOfWork();
        services.AddUnitOfWorkProvider(OrmProvider.EfCore, CreateProbeBinding(OrmProvider.EfCore));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();

        var act = () => factory.Create(OrmProvider.SqlSugar);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'SqlSugar'*Registered providers: EfCore*");
    }

    [Fact]
    public void Duplicate_binding_fails_deterministically_instead_of_last_wins()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopeMarker>();
        services.AddUnitOfWork();
        services.AddUnitOfWorkProvider(OrmProvider.EfCore, CreateProbeBinding(OrmProvider.EfCore));
        services.AddUnitOfWorkProvider(OrmProvider.EfCore, CreateProbeBinding(OrmProvider.EfCore));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Duplicate unit-of-work binding*EfCore*");
    }

    [Fact]
    public void Conflicting_default_declarations_fail_at_registration()
    {
        var services = new ServiceCollection();
        services.AddUnitOfWork(OrmProvider.EfCore);

        var act = () => services.AddUnitOfWork(OrmProvider.SqlSugar);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Conflicting default ORM providers*");
    }

    [Fact]
    public void Repeated_registration_with_same_default_is_idempotent()
    {
        var services = new ServiceCollection();
        services.AddUnitOfWork(OrmProvider.EfCore);
        services.AddUnitOfWork(OrmProvider.EfCore);

        services.Count(descriptor => descriptor.ServiceType == typeof(IUnitOfWorkManager)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(IUnitOfWorkFactory)).Should().Be(1);
    }

    [Fact]
    public void RequiresNew_uses_child_scope_and_restores_parent_state()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopeMarker>();
        services.AddUnitOfWork();
        services.AddUnitOfWorkProvider(OrmProvider.EfCore, CreateProbeBinding(OrmProvider.EfCore));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

        ProbeUnitOfWork outerUnitOfWork;
        ProbeUnitOfWork innerUnitOfWork;
        using (var outerScope = manager.BeginScope())
        {
            outerUnitOfWork = (ProbeUnitOfWork)outerScope.UnitOfWork;
            using (var innerScope = manager.BeginScope(requiresNew: true))
            {
                innerScope.IsOwner.Should().BeTrue();
                innerUnitOfWork = (ProbeUnitOfWork)innerScope.UnitOfWork;
                innerUnitOfWork.Should().NotBeSameAs(outerUnitOfWork,
                    "requiresNew must not hand back the same scoped instance");
                innerUnitOfWork.ScopeId.Should().NotBe(outerUnitOfWork.ScopeId,
                    "requiresNew must isolate through a child DI scope with its own DbContext/connection");
                manager.Current.Should().BeSameAs(innerUnitOfWork);
            }

            manager.Current.Should().BeSameAs(outerUnitOfWork,
                "disposing the inner scope must restore the parent unit of work");
        }

        manager.CurrentOrNull.Should().BeNull();
        innerUnitOfWork.DisposeCount.Should().Be(1);
        outerUnitOfWork.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void RequiresNew_when_binding_declares_unsupported_gives_deterministic_diagnostic()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopeMarker>();
        services.AddUnitOfWork();
        services.AddUnitOfWorkProvider(
            OrmProvider.SqlSugar, CreateProbeBinding(OrmProvider.SqlSugar), supportsRequiresNew: false);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

        using var outerScope = manager.BeginScope();

        var act = () => manager.BeginScope(requiresNew: true);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*SqlSugar*requiresNew*");
        manager.Current.Should().BeSameAs(outerScope.UnitOfWork);
    }

    [Fact]
    public void Failed_dependency_creation_does_not_pollute_current()
    {
        var failNextCreation = false;
        var services = new ServiceCollection();
        services.AddScoped<ScopeMarker>();
        services.AddUnitOfWork();
        services.AddUnitOfWorkProvider(OrmProvider.EfCore, sp =>
        {
            if (failNextCreation)
            {
                throw new InvalidOperationException("probe dependency failure");
            }

            return new ProbeUnitOfWork(sp.GetRequiredService<ScopeMarker>().Id, OrmProvider.EfCore);
        });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

        using var outerScope = manager.BeginScope();
        failNextCreation = true;

        var act = () => manager.BeginScope(requiresNew: true);

        act.Should().Throw<InvalidOperationException>().WithMessage("probe dependency failure");
        manager.Current.Should().BeSameAs(outerScope.UnitOfWork,
            "a failed dependency construction must leave the ambient state untouched");
    }

    private static Func<IServiceProvider, IUnitOfWork> CreateProbeBinding(OrmProvider provider)
        => sp => new ProbeUnitOfWork(sp.GetRequiredService<ScopeMarker>().Id, provider);

    private sealed class ScopeMarker
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    private sealed class ProbeUnitOfWork : IUnitOfWork
    {
        private bool _disposed;

        public ProbeUnitOfWork(Guid scopeId, OrmProvider provider)
        {
            ScopeId = scopeId;
            Provider = provider;
        }

        public Guid ScopeId { get; }

        public OrmProvider Provider { get; }

        public int DisposeCount { get; private set; }

        public Task BeginTransactionAsync() => Task.CompletedTask;

        public Task CommitTransactionAsync() => Task.CompletedTask;

        public Task RollbackTransactionAsync() => Task.CompletedTask;

        public Task<int> SaveChangesAsync() => Task.FromResult(0);

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
