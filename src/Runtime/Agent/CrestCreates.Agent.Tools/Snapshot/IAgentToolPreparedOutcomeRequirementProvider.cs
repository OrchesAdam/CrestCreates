namespace CrestCreates.Agent.Tools;

/// <summary>
/// Declares tool families whose handlers must publish a write-before-mutation
/// prepared outcome set. Providers are registered explicitly by the owning
/// module; the runtime does not infer this contract from tool names.
/// </summary>
public interface IAgentToolPreparedOutcomeRequirementProvider
{
    AgentToolPreparedOutcomeContract? Create(string toolName);
}
