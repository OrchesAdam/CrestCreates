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
            var registration = services.FirstOrDefault(item => item.ServiceType == typeof(ModuleConfigurationRegistration))
                ?.ImplementationInstance as ModuleConfigurationRegistration;
            if (registration is not null && ReferenceEquals(registration.State.Owner, services)) return registration.State;
            var state = new ModuleConfigurationState(services, registration);
            state.RegisterStore(services);
            services.TryAddSingleton<ModuleInitializationState>();
            state.Publish(services);
            return state;
        }
    }

    private sealed record ModuleConfigurationRegistration(
        ModuleConfigurationState State,
        KeyValuePair<Type, ExceptionDispatchInfo?>[] Configured,
        IReadOnlyList<ModulePhaseDiagnostic> Diagnostics,
        bool HealthCheckRegistered,
        ServiceDescriptor? StoreDescriptor,
        bool IsConfiguring);

    private sealed class ModuleConfigurationState
    {
        private readonly object _sync = new();
        private readonly Dictionary<Type, ExceptionDispatchInfo?> _configured = new();
        private readonly ModuleDiagnosticsStore _diagnostics = new();
        private bool _healthCheckRegistered;
        private ServiceDescriptor? _storeDescriptor;
        private int _configurationDepth;

        public IServiceCollection Owner { get; }

        public ModuleConfigurationState(IServiceCollection owner, ModuleConfigurationRegistration? registration)
        {
            Owner = owner;
            if (registration is null) return;
            if (registration.IsConfiguring) throw new InvalidOperationException("Module configuration cannot be copied before its current callback completes.");
            _healthCheckRegistered = registration.HealthCheckRegistered;
            _storeDescriptor = registration.StoreDescriptor;
            // Copied descriptors contain these registrations; only their published facts may be inherited.
            foreach (var entry in registration.Configured) _configured.Add(entry.Key, entry.Value);
            foreach (var diagnostic in registration.Diagnostics) _diagnostics.Record(diagnostic);
        }

        public void RegisterHealthCheck(IServiceCollection services)
        {
            lock (_sync)
            {
                if (_healthCheckRegistered) return;
                services.AddHealthChecks().AddCheck<ModuleHealthCheck>("modules", tags: new[] { "modules" });
                _healthCheckRegistered = true;
                Publish(services);
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
                Publish(services);
                var timer = ModulePhaseTimer.StartNew(moduleType.FullName!, "ConfigureServices");
                try
                {
                    configure();
                    _diagnostics.Record(timer.Stop(ModulePhaseStatus.Success));
                }
                catch (Exception error)
                {
                    _configured[moduleType] = ExceptionDispatchInfo.Capture(error);
                    _diagnostics.Record(timer.StopFailed(error));
                    throw;
                }
                finally
                {
                    _configurationDepth--;
                    RegisterStore(services);
                    Publish(services);
                }
            }
        }

        public void Publish(IServiceCollection services)
        {
            lock (_sync)
            {
                var registration = new ModuleConfigurationRegistration(this, _configured.ToArray(), _diagnostics.GetAll(),
                    _healthCheckRegistered, _storeDescriptor, _configurationDepth != 0);
                services.Replace(ServiceDescriptor.Singleton(registration));
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
