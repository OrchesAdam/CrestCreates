using System.Collections.Frozen;
using System.Text.Json.Serialization.Metadata;

namespace CrestCreates.Mcp.Abstractions;

/// <summary>
/// Collects JsonTypeInfo entries from IMcpToolJsonContextContributor implementations.
/// Frozen after all contributors have been called.
/// Duplicate binding keys cause startup failure.
/// </summary>
public sealed class McpJsonContextBuilder
{
    private readonly Dictionary<string, JsonTypeInfo> _entries = new();
    private readonly Dictionary<string, string> _bindingToContributor = new();
    private readonly Dictionary<Type, string> _bindingRootOwnership = new();
    private bool _frozen;

    public void AddBinding(string bindingKey, JsonTypeInfo typeInfo, string contributorId)
    {
        if (_frozen)
            throw new InvalidOperationException("Builder is frozen — no more bindings can be added.");

        if (_entries.TryGetValue(bindingKey, out _))
            throw new InvalidOperationException(
                $"Duplicate binding key '{bindingKey}' from contributor '{contributorId}'. " +
                $"Already registered by contributor '{_bindingToContributor[bindingKey]}'.");

        _entries[bindingKey] = typeInfo;
        _bindingToContributor[bindingKey] = contributorId;
    }

    /// <summary>
    /// Records ownership of a binding root type. A type may only be claimed by one contributor.
    /// </summary>
    public void AddBindingRootOwnership(Type rootType, string contributorId)
    {
        if (_frozen)
            throw new InvalidOperationException("Builder is frozen — no more ownership entries can be added.");

        if (_bindingRootOwnership.TryGetValue(rootType, out var existingOwner))
            throw new InvalidOperationException(
                $"Duplicate binding root type '{rootType.Name}' claimed by contributor '{contributorId}'. " +
                $"Already owned by contributor '{existingOwner}'.");

        _bindingRootOwnership[rootType] = contributorId;
    }

    public McpJsonContextBuildResult Build()
    {
        _frozen = true;
        return new McpJsonContextBuildResult(
            _entries.ToFrozenDictionary(),
            _bindingRootOwnership.ToFrozenDictionary());
    }

    public bool IsFrozen => _frozen;
}

/// <summary>
/// Result of building the MCP JSON context — frozen binding map and binding root ownership.
/// </summary>
public sealed class McpJsonContextBuildResult
{
    public McpJsonContextBuildResult(
        FrozenDictionary<string, JsonTypeInfo> bindings,
        FrozenDictionary<Type, string> bindingRootOwnership)
    {
        Bindings = bindings;
        BindingRootOwnership = bindingRootOwnership;
    }

    /// <summary>
    /// Binding key → JsonTypeInfo entries contributed by all MCP JSON context contributors.
    /// </summary>
    public FrozenDictionary<string, JsonTypeInfo> Bindings { get; }

    /// <summary>
    /// CLR type → contributor ID, recording which contributor owns each binding root type.
    /// </summary>
    public FrozenDictionary<Type, string> BindingRootOwnership { get; }
}
