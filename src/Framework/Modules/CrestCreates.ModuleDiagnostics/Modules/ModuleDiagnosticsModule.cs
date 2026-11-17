using System.Runtime.ExceptionServices;
using CrestCreates.ModuleDiagnostics.Stores;
using CrestCreates.ModuleDiagnostics.HealthChecks;
using CrestCreates.ModuleDiagnostics.Timing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.ModuleDiagnostics.Modules;

public static class ModuleDiagnosticsServiceCollectionExtensions
{
    public static IServiceCollection AddModuleDiagnostics(this IServiceCollection services)
    {
        GetConfiguration(services).RegisterHealthCheck(services);
        return services;
    }

    public static void ConfigureModuleServices<TModule>(this IServiceCollection services, Action<IServiceCollection> configure)
    {
        GetConfiguration(services).Configure(typeof(TModule), services, () => configure(services));
    }

    private static ModuleConfigurationState GetConfiguration(IServiceCollection services)
    {
        lock (services)
        {
            var state = services.FirstOrDefault(descriptor => descriptor.ServiceType == typeof(ModuleConfigurationState))?.ImplementationInstance as ModuleConfigurationState;
            if (state is not null) return state;
            state = new ModuleConfigurationState();
            services.AddSingleton(state);
            state.RegisterStore(services);
            services.TryAddSingleton<ModuleInitializationState>();
            return state;
        }
    }

    private sealed class ModuleConfigurationState
    {
        private readonly object _sync = new();
        private readonly Dictionary<Type, ExceptionDispatchInfo?> _configured = new();
        private readonly ModuleDiagnosticsStore _diagnostics = new();
        private bool _healthCheckRegistered;
        private ServiceDescriptor? _storeDescriptor;

        public void RegisterHealthCheck(IServiceCollection services)
        {
            lock (_sync)
            {
                if (_healthCheckRegistered) return;
                services.AddHealthChecks().AddCheck<ModuleHealthCheck>("modules", tags: new[] { "modules" });
                _healthCheckRegistered = true;
            }
        }

        public void Configure(Type moduleType, IServiceCollection services, Action configure)
        {
            lock (_sync)
            {
                if (_configured.TryGetValue(moduleType, out var failure))
                {
                    failure?.Throw();
                    return;
                }
                _configured.Add(moduleType, null);
                var timer = ModulePhaseTimer.StartNew(moduleType.FullName!, "ConfigureServices");
                try
                {
                    configure();
                    _diagnostics.Record(timer.Stop(ModulePhaseStatus.Success));
                    RegisterStore(services);
                }
                catch (Exception error)
                {
                    _configured[moduleType] = ExceptionDispatchInfo.Capture(error);
                    _diagnostics.Record(timer.StopFailed(error));
                    RegisterStore(services);
                    throw;
                }
            }
        }

        public void RegisterStore(IServiceCollection services)
        {
            var snapshot = _diagnostics.GetAll();
            var descriptor = ServiceDescriptor.Singleton<IModuleDiagnosticsStore>(_ => CreateStore(snapshot));
            if (_storeDescriptor is not null)
            {
                var index = services.IndexOf(_storeDescriptor);
                if (index < 0) return;
                services[index] = descriptor;
            }
            else
            {
                if (services.Any(item => item.ServiceType == typeof(IModuleDiagnosticsStore))) return;
                services.Add(descriptor);
            }
            _storeDescriptor = descriptor;
        }

        private static IModuleDiagnosticsStore CreateStore(IReadOnlyList<ModulePhaseDiagnostic> snapshot)
        {
            var store = new ModuleDiagnosticsStore();
            foreach (var diagnostic in snapshot) store.Record(diagnostic);
            return store;
        }
    }
}
