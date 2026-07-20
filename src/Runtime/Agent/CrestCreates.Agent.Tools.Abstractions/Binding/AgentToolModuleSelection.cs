namespace CrestCreates.Agent.Tools;

/// <summary>Explicit per-host module opt-in for generated Agent Tool contributions.</summary>
public interface IAgentToolModuleSelection
{
    string ModuleId { get; }
}
