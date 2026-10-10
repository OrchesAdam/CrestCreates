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
        AddProbeBinding(services, OrmProvider.FreeSql);

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
        AddProbeBinding(services, OrmProvider.EfCore);
        AddProbeBinding(services, OrmProvider.SqlSugar);

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
        AddProbeBinding(services, OrmProvider.EfCore);
        AddProbeBinding(services, OrmProvider.SqlSugar);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

        using var unitOfWorkScope = manager.BeginScope(new UnitOfWorkOptions { Provider = OrmProvider.EfCore });
        ((ProbeUnitOfWork)unitOfWorkScope.UnitOfWork).Provider.Should().Be(OrmProvider.EfCore);
    }

    [Fact]
    public void Multiple_bindings_without_default_fail_deterministically()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopeMarker>();
        services.AddUnitOfWork();
        AddProbeBinding(services, OrmProvider.EfCore);
        AddProbeBinding(services, OrmProvider.SqlSugar);

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
        AddProbeBinding(services, OrmProvider.EfCore);

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
        AddProbeBinding(services, OrmProvider.EfCore);
        AddProbeBinding(services, OrmProvider.EfCore);

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
    public void Mixing_binding_and_custom_factory_registration_fails_deterministically()
    {
        var services = new ServiceCollection();
        services.AddUnitOfWork();

        var act = () => services.AddUnitOfWork<ProbeUnitOfWorkFactory>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Conflicting unit-of-work registrations*provider-bindings*custom-factory*");
    }

    [Fact]
    public void Different_custom_factories_fail_deterministically_in_both_orders()
    {
        var services = new ServiceCollection();
        services.AddUnitOfWork<ProbeUnitOfWorkFactory>();

        var act = () => services.AddUnitOfWork<SecondProbeUnitOfWorkFactory>();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Conflicting unit-of-work registrations*ProbeUnitOfWorkFactory*SecondProbeUnitOfWorkFactory*");

        var reversedServices = new ServiceCollection();
        reversedServices.AddUnitOfWork<SecondProbeUnitOfWorkFactory>();

        var reversedAct = () => reversedServices.AddUnitOfWork<ProbeUnitOfWorkFactory>();
        reversedAct.Should().Throw<InvalidOperationException>()
            .WithMessage("*Conflicting unit-of-work registrations*SecondProbeUnitOfWorkFactory*ProbeUnitOfWorkFactory*");
    }

    [Fact]
    public void Same_custom_factory_repeated_is_idempotent()
    {
        var services = new ServiceCollection();
        services.AddUnitOfWork<ProbeUnitOfWorkFactory>();
        services.AddUnitOfWork<ProbeUnitOfWorkFactory>();

        services.Count(descriptor => descriptor.ServiceType == typeof(IUnitOfWorkFactory)).Should().Be(1);
    }

    [Fact]
    public void Same_custom_factory_with_conflicting_defaults_fails()
    {
        var services = new ServiceCollection();
        services.AddUnitOfWork<ProbeUnitOfWorkFactory>(OrmProvider.EfCore);

        var act = () => services.AddUnitOfWork<ProbeUnitOfWorkFactory>(OrmProvider.SqlSugar);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Conflicting default ORM providers*");
    }

    [Fact]
    public void RequiresNew_uses_child_scope_and_restores_parent_state()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopeMarker>();
        services.AddScoped<ProbeUnitOfWork>(sp =>
            new ProbeUnitOfWork(sp.GetRequiredService<ScopeMarker>().Id, OrmProvider.EfCore));
        services.AddUnitOfWork();
        services.AddUnitOfWorkProvider(
            OrmProvider.EfCore,
            static sp => sp.GetRequiredService<ProbeUnitOfWork>(),
            supportsRequiresNew: true,
            ambientContextFactory: static sp => new TestDataBaseContext(sp.GetRequiredService<ScopeMarker>().Id));

        using var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        ProbeUnitOfWork outerUnitOfWork;
        ProbeUnitOfWork innerUnitOfWork;
        try
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

            using (var outerScope = manager.BeginScope())
            {
                outerUnitOfWork = (ProbeUnitOfWork)outerScope.UnitOfWork;
                using (var innerScope = manager.BeginScope(new UnitOfWorkOptions { Propagation = UnitOfWorkPropagation.RequiresNew }))
                {
                    innerScope.IsOwner.Should().BeTrue();
                    innerUnitOfWork = (ProbeUnitOfWork)innerScope.UnitOfWork;
                    innerUnitOfWork.Should().NotBeSameAs(outerUnitOfWork,
                        "requiresNew must not hand back the same scoped instance");
                    innerUnitOfWork.ScopeId.Should().NotBe(outerUnitOfWork.ScopeId,
                        "requiresNew must isolate through a child DI scope with its own DbContext/connection");
                    UnitOfWorkAmbientContext.Current.Should().NotBeNull(
                        "requiresNew must push the provider-declared ambient context for injected dependencies");
                    manager.Current.Should().BeSameAs(innerUnitOfWork);
                }

                manager.Current.Should().BeSameAs(outerUnitOfWork,
                    "disposing the inner scope must restore the parent unit of work");
                UnitOfWorkAmbientContext.Current.Should().BeNull(
                    "disposing the inner scope must restore the previous ambient context");

                innerUnitOfWork.DisposeCount.Should().Be(1,
                    "the child DI scope must dispose its tracked instance exactly once");
                outerUnitOfWork.DisposeCount.Should().Be(0,
                    "the caller scope still owns the outer instance while it is alive");
            }

            manager.CurrentOrNull.Should().BeNull();
        }
        finally
        {
            scope.Dispose();
        }

        outerUnitOfWork.DisposeCount.Should().Be(1,
            "the caller DI scope must dispose its tracked instance exactly once");
    }

    [Fact]
    public void RequiresNew_when_binding_declares_unsupported_gives_deterministic_diagnostic()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopeMarker>();
        services.AddUnitOfWork();
        AddProbeBinding(services, OrmProvider.SqlSugar);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

        using var outerScope = manager.BeginScope();

        var act = () => manager.BeginScope(new UnitOfWorkOptions { Propagation = UnitOfWorkPropagation.RequiresNew });

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
        services.AddUnitOfWorkProvider(
            OrmProvider.EfCore,
            sp =>
            {
                if (failNextCreation)
                {
                    throw new InvalidOperationException("probe dependency failure");
                }

                return new ProbeUnitOfWork(sp.GetRequiredService<ScopeMarker>().Id, OrmProvider.EfCore);
            },
            supportsRequiresNew: true,
            ambientContextFactory: static sp => new TestDataBaseContext(sp.GetRequiredService<ScopeMarker>().Id));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

        using var outerScope = manager.BeginScope();
        failNextCreation = true;

        var act = () => manager.BeginScope(new UnitOfWorkOptions { Propagation = UnitOfWorkPropagation.RequiresNew });

        act.Should().Throw<InvalidOperationException>().WithMessage("probe dependency failure");
        manager.Current.Should().BeSameAs(outerScope.UnitOfWork,
            "a failed dependency construction must leave the ambient state untouched");
    }

    private static void AddProbeBinding(IServiceCollection services, OrmProvider provider)
    {
        // Selection/diagnostic probes: no ambient context, so requiresNew is
        // declared unsupported (mirrors an honest provider capability statement).
        services.AddUnitOfWorkProvider(
            provider,
            sp => new ProbeUnitOfWork(sp.GetRequiredService<ScopeMarker>().Id, provider),
            supportsRequiresNew: false);
    }

    private sealed class ScopeMarker
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    private sealed class ProbeUnitOfWorkFactory : IUnitOfWorkFactory
    {
        public IUnitOfWork Create(OrmProvider provider)
            => new ProbeUnitOfWork(Guid.NewGuid(), provider);
    }

    private sealed class SecondProbeUnitOfWorkFactory : IUnitOfWorkFactory
    {
        public IUnitOfWork Create(OrmProvider provider)
            => new ProbeUnitOfWork(Guid.NewGuid(), provider);
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

        public Task BeginTransactionAsync(UnitOfWorkBeginOptions options, System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task CommitTransactionAsync(System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RollbackTransactionAsync(System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<int> SaveChangesAsync(System.Threading.CancellationToken cancellationToken = default) => Task.FromResult(0);

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
