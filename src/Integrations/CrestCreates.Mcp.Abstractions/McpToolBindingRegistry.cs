using System.Collections.Concurrent;

namespace CrestCreates.Mcp;

public static class McpToolBindingRegistry
{
    private static readonly ConcurrentDictionary<(string Id, int Version), McpToolBindingContract> Contracts = new();

    public static void Register(McpToolBindingContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (string.IsNullOrWhiteSpace(contract.ToolDescriptorId) || contract.ToolDescriptorVersion <= 0)
            throw new ArgumentException("MCP binding identity is invalid.", nameof(contract));

        if (!Contracts.TryAdd((contract.ToolDescriptorId, contract.ToolDescriptorVersion), contract))
            throw new InvalidOperationException("An MCP binding is already registered for this descriptor identity.");
    }

    public static McpToolBindingContract? Find(string descriptorId, int descriptorVersion)
        => Contracts.TryGetValue((descriptorId, descriptorVersion), out var contract) ? contract : null;

    public static McpToolBindingContract GetRequired(string descriptorId, int descriptorVersion)
        => Find(descriptorId, descriptorVersion)
            ?? throw new InvalidOperationException("Required MCP binding is not registered.");
}
