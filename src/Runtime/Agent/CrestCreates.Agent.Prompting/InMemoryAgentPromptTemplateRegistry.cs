using CrestCreates.Agent.Prompting.Abstractions;

namespace CrestCreates.Agent.Prompting;

public sealed class InMemoryAgentPromptTemplateRegistry : IAgentPromptTemplateRegistry
{
    private readonly AgentPromptTemplateDescriptor[] _descriptors;

    public InMemoryAgentPromptTemplateRegistry(IEnumerable<AgentPromptTemplateDescriptor> descriptors)
    {
        _descriptors = descriptors
            .Select(d => d with { Metadata = new Dictionary<string, string>(d.Metadata) })
            .ToArray();
    }

    public AgentPromptTemplateDescriptor? Find(AgentPromptTemplateId templateId, AgentPromptVersion version)
    {
        var descriptor = Array.Find(_descriptors, d =>
            d.TemplateId == templateId && d.Version == version);
        return descriptor is null ? null : descriptor with
        {
            Metadata = new Dictionary<string, string>(descriptor.Metadata)
        };
    }

    public IReadOnlyList<AgentPromptTemplateDescriptor> List() => _descriptors
        .Select(d => d with { Metadata = new Dictionary<string, string>(d.Metadata) })
        .ToArray();
}
