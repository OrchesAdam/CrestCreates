using System.Collections.Concurrent;

namespace CrestCreates.Agent.Tools;

public static class AgentToolBindingRegistry
{
    private static readonly ConcurrentDictionary<(string Id, int Version), AgentToolBindingContract> Contracts = new();

    public static void Register(AgentToolBindingContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (string.IsNullOrWhiteSpace(contract.ToolDescriptorId) || contract.ToolDescriptorVersion <= 0)
            throw new ArgumentException("Agent Tool binding identity is invalid.", nameof(contract));

        if (!Contracts.TryAdd((contract.ToolDescriptorId, contract.ToolDescriptorVersion), contract))
            throw new InvalidOperationException("An Agent Tool binding is already registered for this descriptor identity.");
    }

    public static AgentToolBindingContract? Find(string descriptorId, int descriptorVersion)
        => Contracts.TryGetValue((descriptorId, descriptorVersion), out var contract) ? contract : null;

    public static AgentToolBindingContract GetRequired(string descriptorId, int descriptorVersion)
        => Find(descriptorId, descriptorVersion)
            ?? throw new InvalidOperationException("Required Agent Tool binding is not registered.");
}
