using System.ComponentModel;

namespace CrestCreates.DynamicApi;

/// <summary>
/// Static registry of body types that require source-generated JSON serialization metadata.
/// Populated at startup by source-generated [ModuleInitializer] code.
/// Startup validation iterates registered types and checks each has a
/// non-null JsonTypeInfo in the application's JsonSerializerOptions.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class CapabilityEndpointJsonContractRegistry
{
    private static readonly List<Type> _bodyTypes = new();
    private static readonly object _lock = new();

    /// <summary>
    /// Registers a body type that requires source-generated JSON metadata.
    /// Called by generated [ModuleInitializer] code during assembly load.
    /// </summary>
    public static void RegisterBodyType(Type bodyType)
    {
        lock (_lock)
        {
            if (!_bodyTypes.Contains(bodyType))
                _bodyTypes.Add(bodyType);
        }
    }

    /// <summary>
    /// Returns all registered body types. Used by startup validation.
    /// </summary>
    public static IReadOnlyList<Type> GetRegisteredBodyTypes()
    {
        lock (_lock)
            return _bodyTypes.ToList();
    }

    /// <summary>
    /// Clears all registered body types. Internal for test isolation.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static void Reset()
    {
        lock (_lock)
            _bodyTypes.Clear();
    }
}
