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
            var descriptor = services.FirstOrDefault(item => item.ServiceType == typeof(ModuleConfigurationState));
            var existing = descriptor?.ImplementationInstance as ModuleConfigurationState;
            if (existing is not null && ReferenceEquals(existing.Owner, services)) return existing;
            var state = existing?.Clone(services) ?? new ModuleConfigurationState(services);
            if (descriptor is null) services.AddSingleton(state);
            else services[services.IndexOf(descriptor)] = ServiceDescriptor.Singleton(state);
            state.RegisterStore(services);
            services.TryAddSingleton<ModuleInitializationState>();
            return state;
        }
    }

    private sealed class ModuleConfigurationState(IServiceCollection owner)
    {
        private readonly object _sync = new();
        private readonly Dictionary<Type, ExceptionDispatchInfo?> _configured = new();
        private readonly ModuleDiagnosticsStore _diagnostics = new();
        private bool _healthCheckRegistered;
        private ServiceDescriptor? _storeDescriptor;
        private int _configurationDepth;

        public IServiceCollection Owner { get; } = owner;

        public ModuleConfigurationState Clone(IServiceCollection owner)
        {
            lock (_sync)
            {
                if (_configurationDepth != 0) throw new InvalidOperationException("Module configuration cannot be copied before its current callback completes.");
                var clone = new ModuleConfigurationState(owner)
                {
                    _healthCheckRegistered = _healthCheckRegistered,
                    _storeDescriptor = _storeDescriptor
                };
                // Replaying registrations already present in the copy would duplicate configuration side effects.
                foreach (var entry in _configured) clone._configured.Add(entry.Key, entry.Value);
                foreach (var diagnostic in _diagnostics.GetAll()) clone._diagnostics.Record(diagnostic);
                return clone;
            }
        }

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
                _configurationDepth++;
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
                finally
                {
                    _configurationDepth--;
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
