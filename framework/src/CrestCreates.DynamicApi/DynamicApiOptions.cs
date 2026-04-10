using System.Reflection;

namespace CrestCreates.DynamicApi;

public sealed class DynamicApiOptions
{
    private readonly HashSet<Assembly> _serviceAssemblies = new();

    public IReadOnlyCollection<Assembly> ServiceAssemblies => _serviceAssemblies;

    public string DefaultRoutePrefix { get; set; } = "api";

    public bool EnableRuntimeReflectionFallback { get; private set; }

    public void AddApplicationServiceAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        _serviceAssemblies.Add(assembly);
    }

    public void AddApplicationServiceAssembly<TMarker>()
    {
        AddApplicationServiceAssembly(typeof(TMarker).Assembly);
    }

    [Obsolete("Dynamic API runtime reflection fallback is no longer the default execution path. Only use this for temporary diagnostics.")]
    public void UseRuntimeReflectionFallback()
    {
        EnableRuntimeReflectionFallback = true;
    }
}
