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

    public FrozenDictionary<string, JsonTypeInfo> Build()
    {
        _frozen = true;
        return _entries.ToFrozenDictionary();
    }

    public bool IsFrozen => _frozen;
}
