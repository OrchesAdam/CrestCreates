namespace CrestCreates.Agent.ControlPlane.Abstractions;

public interface IAgentToolManifestProvider
{
    IReadOnlyList<AgentToolDescriptor> GetAllTools();
    AgentToolDescriptor? GetToolByName(string name);
}
