using System;
using System.Linq;
using CrestCreates.ModuleDiagnostics.Modules;
using CrestCreates.ModuleDiagnostics.Stores;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace CrestCreates.ModuleDiagnostics.Tests.Stores;

public class ModuleDiagnosticsStoreTests
{
    private readonly ModuleDiagnosticsStore _store = new();

    private ModulePhaseDiagnostic CreateSuccess(string moduleName, string phase)
    {
        return new ModulePhaseDiagnostic(moduleName, phase, ModulePhaseStatus.Success, TimeSpan.FromMilliseconds(1), null);
    }

    private ModulePhaseDiagnostic CreateFailure(string moduleName, string phase, string error)
    {
        return new ModulePhaseDiagnostic(moduleName, phase, ModulePhaseStatus.Failed, TimeSpan.FromMilliseconds(1), error);
    }

    [Fact]
    public void Record_ShouldStoreDiagnostic()
    {
        var diagnostic = CreateSuccess("TestModule", "Init");

        _store.Record(diagnostic);

        _store.GetAll().Should().ContainSingle()
            .Which.Should().Be(diagnostic);
    }

    [Fact]
    public void Record_MultiplePhases_ShouldIncreaseTotalCount()
    {
        _store.Record(CreateSuccess("M1", "PreInit"));
        _store.Record(CreateSuccess("M1", "Init"));
        _store.Record(CreateSuccess("M2", "PreInit"));

        _store.TotalCount.Should().Be(3);
    }

    [Fact]
    public void GetByModule_ShouldReturnAllPhasesForModule()
    {
        _store.Record(CreateSuccess("M1", "PreInit"));
        _store.Record(CreateSuccess("M1", "Init"));
        _store.Record(CreateSuccess("M2", "PreInit"));

        var m1Phases = _store.GetByModule("M1");

        m1Phases.Should().HaveCount(2);
        m1Phases.Select(p => p.Phase).Should().Contain(new[] { "PreInit", "Init" });
    }

    [Fact]
    public void GetByModule_UnknownModule_ShouldReturnEmpty()
    {
        _store.GetByModule("NonExistent").Should().BeEmpty();
    }

    [Fact]
    public void GetFailed_ShouldReturnOnlyFailures()
    {
        _store.Record(CreateSuccess("M1", "PreInit"));
        _store.Record(CreateFailure("M1", "Init", "error 1"));
        _store.Record(CreateSuccess("M2", "PreInit"));
        _store.Record(CreateFailure("M2", "ConfigureServices", "error 2"));

        var failed = _store.GetFailed();

        failed.Should().HaveCount(2);
        failed.All(f => f.Status == ModulePhaseStatus.Failed).Should().BeTrue();
    }

    [Fact]
    public void Record_FailedPhase_ShouldSetHasFailures()
    {
        _store.HasFailures.Should().BeFalse();

        _store.Record(CreateFailure("M1", "Init", "error"));

        _store.HasFailures.Should().BeTrue();
    }

    [Fact]
    public void Record_OnlySuccess_ShouldKeepHasFailuresFalse()
    {
        _store.Record(CreateSuccess("M1", "PreInit"));
        _store.Record(CreateSuccess("M2", "Init"));

        _store.HasFailures.Should().BeFalse();
    }

    [Fact]
    public void GetByModule_WithPartialFailure_ShouldReturnAllPhases()
    {
        _store.Record(CreateSuccess("M1", "PreInit"));
        _store.Record(CreateFailure("M1", "Init", "failed"));

        var m1Phases = _store.GetByModule("M1");

        m1Phases.Should().HaveCount(2);
        m1Phases.Select(p => p.Status).Should().Contain(new[] { ModulePhaseStatus.Success, ModulePhaseStatus.Failed });
    }

    [Fact]
    public void GetAll_AfterMixedResults_ShouldContainBothSuccessAndFailure()
    {
        _store.Record(CreateSuccess("M1", "PreInit"));
        _store.Record(CreateFailure("M2", "Init", "failed"));

        var all = _store.GetAll();

        all.Should().HaveCount(2);
        all.Select(r => r.Status).Should().Contain(new[] { ModulePhaseStatus.Success, ModulePhaseStatus.Failed });
    }

    [Fact]
    public void Registration_DoesNotShareDiagnosticsAcrossProviders()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        CrestCreates.ModuleDiagnostics.Modules.ModuleDiagnosticsServiceCollectionExtensions.AddModuleDiagnostics(services);
        using var first = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        using var second = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var firstStore = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IModuleDiagnosticsStore>(first);
        var secondStore = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IModuleDiagnosticsStore>(second);
        firstStore.Record(CreateFailure("OnlyFirst", "Init", "fixture"));

        firstStore.Should().NotBeSameAs(secondStore);
        secondStore.GetAll().Should().BeEmpty();
        secondStore.HasFailures.Should().BeFalse();
    }

    [Fact]
    public void BuiltProvider_LazyDiagnosticsDoNotReadLaterConfiguration()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        CrestCreates.ModuleDiagnostics.Modules.ModuleDiagnosticsServiceCollectionExtensions.ConfigureModuleServices<ConfigurationOne>(services, _ => { });
        using var first = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        CrestCreates.ModuleDiagnostics.Modules.ModuleDiagnosticsServiceCollectionExtensions.ConfigureModuleServices<ConfigurationTwo>(services, _ => { });
        using var second = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var firstStore = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IModuleDiagnosticsStore>(first);
        var secondStore = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IModuleDiagnosticsStore>(second);

        firstStore.GetAll().Select(record => record.ModuleName).Should().Equal(typeof(ConfigurationOne).FullName);
        secondStore.GetAll().Select(record => record.ModuleName).Should().Equal(typeof(ConfigurationOne).FullName, typeof(ConfigurationTwo).FullName);
    }

    private sealed class ConfigurationOne : CrestCreates.Modularity.ModuleBase;
    private sealed class ConfigurationTwo : CrestCreates.Modularity.ModuleBase;
    private sealed class ConfigurationThree : CrestCreates.Modularity.ModuleBase;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopiedCollection_UsesPublishedHealthSnapshot(bool configureSourceFirst)
    {
        var first = new ServiceCollection();
        first.ConfigureModuleServices<ConfigurationOne>(_ => { });
        IServiceCollection second = new ServiceCollection();
        foreach (var descriptor in first) second.Add(descriptor);
        if (configureSourceFirst)
        {
            first.AddModuleDiagnostics();
            second.AddModuleDiagnostics();
        }
        else
        {
            second.AddModuleDiagnostics();
            first.AddModuleDiagnostics();
        }
        using var firstProvider = first.BuildServiceProvider();
        using var secondProvider = second.BuildServiceProvider();

        firstProvider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .Count(registration => registration.Name == "modules").Should().Be(1);
        secondProvider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .Count(registration => registration.Name == "modules").Should().Be(1);
    }

    [Fact]
    public void CopiedCollection_DoesNotInheritFailurePublishedAfterCopy()
    {
        var first = new ServiceCollection();
        first.AddModuleDiagnostics();
        IServiceCollection second = new ServiceCollection();
        foreach (var descriptor in first) second.Add(descriptor);
        var calls = 0;
        Action configureFirst = () => first.ConfigureModuleServices<ConfigurationOne>(_ =>
        {
            calls++;
            throw new InvalidOperationException("source-only failure");
        });
        configureFirst.Should().Throw<InvalidOperationException>();

        second.ConfigureModuleServices<ConfigurationOne>(_ => calls++);

        using var firstProvider = first.BuildServiceProvider();
        using var secondProvider = second.BuildServiceProvider();
        calls.Should().Be(2);
        firstProvider.GetRequiredService<IModuleDiagnosticsStore>().GetFailed().Should().ContainSingle();
        secondProvider.GetRequiredService<IModuleDiagnosticsStore>().GetFailed().Should().BeEmpty();
        secondProvider.GetRequiredService<IModuleDiagnosticsStore>().TotalCount.Should().Be(1);
    }

    [Fact]
    public void CopiedCollection_KeepsCopiedStoreAnchorAfterSourcePublishesLaterFacts()
    {
        var first = new ServiceCollection();
        first.ConfigureModuleServices<ConfigurationOne>(_ => { });
        IServiceCollection second = new ServiceCollection();
        foreach (var descriptor in first) second.Add(descriptor);
        first.ConfigureModuleServices<ConfigurationTwo>(_ => { });

        second.ConfigureModuleServices<ConfigurationThree>(_ => { });

        using var firstProvider = first.BuildServiceProvider();
        using var secondProvider = second.BuildServiceProvider();
        firstProvider.GetRequiredService<IModuleDiagnosticsStore>().GetAll().Select(record => record.ModuleName)
            .Should().BeEquivalentTo(typeof(ConfigurationOne).FullName, typeof(ConfigurationTwo).FullName);
        secondProvider.GetRequiredService<IModuleDiagnosticsStore>().GetAll().Select(record => record.ModuleName)
            .Should().BeEquivalentTo(typeof(ConfigurationOne).FullName, typeof(ConfigurationThree).FullName);
    }

    [Fact]
    public void CopiedCollection_ReadySnapshotDoesNotBecomeIncompleteWhenSourceStartsLater()
    {
        var first = new ServiceCollection();
        first.AddModuleDiagnostics();
        IServiceCollection second = new ServiceCollection();
        foreach (var descriptor in first) second.Add(descriptor);
        var calls = 0;

        first.ConfigureModuleServices<ConfigurationOne>(_ =>
        {
            calls++;
            second.ConfigureModuleServices<ConfigurationOne>(_ => calls++);
        });

        using var firstProvider = first.BuildServiceProvider();
        using var secondProvider = second.BuildServiceProvider();
        calls.Should().Be(2);
        firstProvider.GetRequiredService<IModuleDiagnosticsStore>().TotalCount.Should().Be(1);
        secondProvider.GetRequiredService<IModuleDiagnosticsStore>().TotalCount.Should().Be(1);
    }

    [Fact]
    public void CopiedCollection_IncompleteSnapshotStaysRejectedAfterSourceCompletes()
    {
        var first = new ServiceCollection();
        IServiceCollection second = new ServiceCollection();
        first.ConfigureModuleServices<ConfigurationOne>(services =>
        {
            foreach (var descriptor in services) second.Add(descriptor);
            services.AddSingleton("only-source");
        });

        Action configureSecond = () => second.AddModuleDiagnostics();

        configureSecond.Should().Throw<InvalidOperationException>().WithMessage("*current callback completes*");
        using var provider = first.BuildServiceProvider();
        provider.GetRequiredService<string>().Should().Be("only-source");
        configureSecond.Should().Throw<InvalidOperationException>().WithMessage("*current callback completes*");
    }

    [Fact]
    public void CopiedCollection_PreservesConfigurationFactsWithoutSharingState()
    {
        var first = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        var calls = 0;
        Action configure = () => CrestCreates.ModuleDiagnostics.Modules.ModuleDiagnosticsServiceCollectionExtensions.ConfigureModuleServices<ConfigurationOne>(first, _ => calls++);
        configure();
        Microsoft.Extensions.DependencyInjection.IServiceCollection second = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        foreach (var descriptor in first) second.Add(descriptor);
        CrestCreates.ModuleDiagnostics.Modules.ModuleDiagnosticsServiceCollectionExtensions.ConfigureModuleServices<ConfigurationOne>(second, _ => calls++);
        CrestCreates.ModuleDiagnostics.Modules.ModuleDiagnosticsServiceCollectionExtensions.ConfigureModuleServices<ConfigurationTwo>(second, _ => calls++);
        using var firstProvider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(first);
        using var secondProvider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(second);

        CrestCreates.ModuleDiagnostics.Modules.ModuleDiagnosticsServiceCollectionExtensions.ConfigureModuleServices<ConfigurationTwo>(first, _ => calls++);
        using var updatedFirst = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(first);
        calls.Should().Be(3);
        Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IModuleDiagnosticsStore>(updatedFirst).TotalCount.Should().Be(2);
        Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IModuleDiagnosticsStore>(firstProvider).GetAll()
            .Select(record => record.ModuleName).Should().Equal(typeof(ConfigurationOne).FullName);
        Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IModuleDiagnosticsStore>(secondProvider).GetAll()
            .Select(record => record.ModuleName).Should().Equal(typeof(ConfigurationOne).FullName, typeof(ConfigurationTwo).FullName);
    }

    [Fact]
    public async System.Threading.Tasks.Task CopiedCollection_DoesNotSharePendingInitialization()
    {
        var first = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        CrestCreates.ModuleDiagnostics.Modules.ModuleDiagnosticsServiceCollectionExtensions.AddModuleDiagnostics(first);
        using var firstProvider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(first);
        var gate = new System.Threading.Tasks.TaskCompletionSource(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var firstState = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<CrestCreates.ModuleDiagnostics.Modules.ModuleInitializationState>(firstProvider);
        var pending = firstState.RunInitializationAsync(typeof(ConfigurationOne), () => { calls++; return gate.Task; });
        Microsoft.Extensions.DependencyInjection.IServiceCollection second = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        foreach (var descriptor in first) second.Add(descriptor);
        CrestCreates.ModuleDiagnostics.Modules.ModuleDiagnosticsServiceCollectionExtensions.AddModuleDiagnostics(second);
        using var secondProvider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(second);
        var secondState = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<CrestCreates.ModuleDiagnostics.Modules.ModuleInitializationState>(secondProvider);
        var completed = secondState.RunInitializationAsync(typeof(ConfigurationOne), () => { calls++; return System.Threading.Tasks.Task.CompletedTask; });
        await completed.WaitAsync(TimeSpan.FromSeconds(2));
        gate.SetResult();
        await pending;

        completed.Should().NotBeSameAs(pending);
        secondState.Should().NotBeSameAs(firstState);
        calls.Should().Be(2);
    }

    [Fact]
    public void CopiedCollection_PreservesExplicitStoreFactory()
    {
        var first = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        var calls = 0;
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<IModuleDiagnosticsStore>(first, provider =>
        {
            provider.Should().NotBeNull();
            calls++;
            return new ModuleDiagnosticsStore();
        });
        CrestCreates.ModuleDiagnostics.Modules.ModuleDiagnosticsServiceCollectionExtensions.ConfigureModuleServices<ConfigurationOne>(first, _ => { });
        Microsoft.Extensions.DependencyInjection.IServiceCollection second = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        foreach (var descriptor in first) second.Add(descriptor);
        CrestCreates.ModuleDiagnostics.Modules.ModuleDiagnosticsServiceCollectionExtensions.ConfigureModuleServices<ConfigurationTwo>(second, _ => { });
        calls.Should().Be(0);
        using var firstProvider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(first);
        using var secondProvider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(second);
        var firstStore = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IModuleDiagnosticsStore>(firstProvider);
        var secondStore = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IModuleDiagnosticsStore>(secondProvider);

        calls.Should().Be(2);
        firstStore.Should().NotBeSameAs(secondStore);
    }

    [Fact]
    public void CopiedCollection_DoesNotInvokeInitializationFactoryDuringConfiguration()
    {
        var first = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        var calls = 0;
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<CrestCreates.ModuleDiagnostics.Modules.ModuleInitializationState>(first, provider =>
        {
            provider.Should().NotBeNull();
            calls++;
            return new CrestCreates.ModuleDiagnostics.Modules.ModuleInitializationState();
        });
        CrestCreates.ModuleDiagnostics.Modules.ModuleDiagnosticsServiceCollectionExtensions.AddModuleDiagnostics(first);
        Microsoft.Extensions.DependencyInjection.IServiceCollection second = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        foreach (var descriptor in first) second.Add(descriptor);
        CrestCreates.ModuleDiagnostics.Modules.ModuleDiagnosticsServiceCollectionExtensions.AddModuleDiagnostics(second);
        calls.Should().Be(0);
        using var firstProvider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(first);
        using var secondProvider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(second);
        var firstState = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<CrestCreates.ModuleDiagnostics.Modules.ModuleInitializationState>(firstProvider);
        var secondState = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<CrestCreates.ModuleDiagnostics.Modules.ModuleInitializationState>(secondProvider);

        calls.Should().Be(2);
        firstState.Should().NotBeSameAs(secondState);
    }

    [Fact]
    public void CopiedCollection_PreservesFailureWithoutSuppressingOtherCollectionsConfiguration()
    {
        var first = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        var calls = 0;
        Action firstFailure = () => CrestCreates.ModuleDiagnostics.Modules.ModuleDiagnosticsServiceCollectionExtensions.ConfigureModuleServices<ConfigurationOne>(first, _ =>
        {
            calls++;
            throw new InvalidOperationException("copied configuration failure");
        });
        firstFailure.Should().Throw<InvalidOperationException>();
        Microsoft.Extensions.DependencyInjection.IServiceCollection second = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        foreach (var descriptor in first) second.Add(descriptor);
        Action copiedFailure = () => CrestCreates.ModuleDiagnostics.Modules.ModuleDiagnosticsServiceCollectionExtensions.ConfigureModuleServices<ConfigurationOne>(second, _ => calls++);
        copiedFailure.Should().Throw<InvalidOperationException>().WithMessage("copied configuration failure");
        CrestCreates.ModuleDiagnostics.Modules.ModuleDiagnosticsServiceCollectionExtensions.ConfigureModuleServices<ConfigurationTwo>(second, _ => calls++);
        CrestCreates.ModuleDiagnostics.Modules.ModuleDiagnosticsServiceCollectionExtensions.ConfigureModuleServices<ConfigurationTwo>(first, _ => calls++);
        using var firstProvider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(first);
        using var secondProvider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(second);
        var firstStore = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IModuleDiagnosticsStore>(firstProvider);
        var secondStore = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IModuleDiagnosticsStore>(secondProvider);

        calls.Should().Be(3);
        firstStore.GetFailed().Should().ContainSingle();
        secondStore.GetFailed().Should().ContainSingle();
        firstStore.TotalCount.Should().Be(2);
        secondStore.TotalCount.Should().Be(2);
    }

    [Fact]
    public void CopiedCollection_RejectsIncompleteConfigurationSnapshot()
    {
        var first = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Action configure = () => CrestCreates.ModuleDiagnostics.Modules.ModuleDiagnosticsServiceCollectionExtensions.ConfigureModuleServices<ConfigurationOne>(first, _ =>
        {
            Microsoft.Extensions.DependencyInjection.IServiceCollection second = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            foreach (var descriptor in first) second.Add(descriptor);
            CrestCreates.ModuleDiagnostics.Modules.ModuleDiagnosticsServiceCollectionExtensions.AddModuleDiagnostics(second);
        });

        configure.Should().Throw<InvalidOperationException>().WithMessage("*current callback completes*");
    }

    [Fact]
    public void CopiedCollection_HealthRegistrationRemainsIdempotent()
    {
        var first = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        CrestCreates.ModuleDiagnostics.Modules.ModuleDiagnosticsServiceCollectionExtensions.AddModuleDiagnostics(first);
        Microsoft.Extensions.DependencyInjection.IServiceCollection second = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        foreach (var descriptor in first) second.Add(descriptor);
        CrestCreates.ModuleDiagnostics.Modules.ModuleDiagnosticsServiceCollectionExtensions.AddModuleDiagnostics(second);
        CrestCreates.ModuleDiagnostics.Modules.ModuleDiagnosticsServiceCollectionExtensions.AddModuleDiagnostics(second);
        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(second);
        var options = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckServiceOptions>>(provider);

        options.Value.Registrations.Count(registration => registration.Name == "modules").Should().Be(1);
    }

    [Fact]
    public void FailedConfiguration_IsNotReplayedAndKeepsOneFailure()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        var calls = 0;
        Action configure = () => CrestCreates.ModuleDiagnostics.Modules.ModuleDiagnosticsServiceCollectionExtensions.ConfigureModuleServices<ConfigurationOne>(services, _ =>
        {
            calls++;
            throw new InvalidOperationException("fixture configuration failure");
        });
        configure.Should().Throw<InvalidOperationException>();
        configure.Should().Throw<InvalidOperationException>();
        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var store = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IModuleDiagnosticsStore>(provider);

        calls.Should().Be(1);
        store.GetFailed().Should().ContainSingle();
        store.TotalCount.Should().Be(1);
    }

    [Fact]
    public async System.Threading.Tasks.Task CancelledInitialization_IsNotReplayed()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        CrestCreates.ModuleDiagnostics.Modules.ModuleDiagnosticsServiceCollectionExtensions.AddModuleDiagnostics(services);
        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var state = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<CrestCreates.ModuleDiagnostics.Modules.ModuleInitializationState>(provider);
        var calls = 0;
        Func<System.Threading.Tasks.Task> initialize = () =>
        {
            calls++;
            return System.Threading.Tasks.Task.FromCanceled(new System.Threading.CancellationToken(true));
        };
        var first = state.RunInitializationAsync(typeof(ConfigurationOne), initialize);
        var second = state.RunInitializationAsync(typeof(ConfigurationOne), initialize);
        Func<System.Threading.Tasks.Task> wait = () => first;
        await wait.Should().ThrowAsync<OperationCanceledException>();

        second.Should().BeSameAs(first);
        first.IsCanceled.Should().BeTrue();
        calls.Should().Be(1);
    }

    [Fact]
    public void RepeatedRegistration_AddsOneHealthCheck()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        CrestCreates.ModuleDiagnostics.Modules.ModuleDiagnosticsServiceCollectionExtensions.AddModuleDiagnostics(services);
        CrestCreates.ModuleDiagnostics.Modules.ModuleDiagnosticsServiceCollectionExtensions.AddModuleDiagnostics(services);
        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var options = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckServiceOptions>>(provider);

        options.Value.Registrations.Count(registration => registration.Name == "modules").Should().Be(1);
    }

    [Fact]
    public void Record_ConcurrentWritersAndSnapshots_PreserveEveryValue()
    {
        const int count = 20000;
        System.Threading.Tasks.Parallel.For(0, count, index =>
        {
            _store.Record(CreateSuccess("Shared", index.ToString()));
            if (index % 100 == 0) _store.GetAll();
        });

        _store.TotalCount.Should().Be(count);
        var snapshot = _store.GetByModule("Shared");
        snapshot.Should().HaveCount(count);
        snapshot.Select(record => record.Phase).Distinct().Should().HaveCount(count);
        _store.GetAll().Should().HaveCount(count);
    }

    [Fact]
    public void EmptyStore_TotalCountShouldBeZero()
    {
        _store.TotalCount.Should().Be(0);
        _store.HasFailures.Should().BeFalse();
        _store.GetAll().Should().BeEmpty();
        _store.GetFailed().Should().BeEmpty();
    }
}