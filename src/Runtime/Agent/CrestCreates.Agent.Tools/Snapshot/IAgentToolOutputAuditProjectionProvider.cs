namespace CrestCreates.Agent.Tools;

/// <summary>
/// Module-owned, typed output projection. The provider is resolved while the
/// immutable runtime snapshot is built; invocation never inspects JSON field
/// names to manufacture audit facts.
/// </summary>
public interface IAgentToolOutputAuditProjectionProvider
{
    Func<object?, IReadOnlyList<AgentToolAuditFact>>? Create(string toolName, Type outputType);
}

/// <summary>
/// Provides the typed wire outcome discriminator for a binding root.  The
/// invoker uses this instead of probing serialized JSON property names.
/// </summary>
public interface IAgentToolOutputOutcomeCodeProvider
{
    Func<object?, string?>? CreateOutcomeCode(string toolName, Type outputType);
}
