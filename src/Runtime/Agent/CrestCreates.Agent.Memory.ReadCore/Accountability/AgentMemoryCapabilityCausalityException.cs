namespace CrestCreates.Agent.Memory.ReadCore.Accountability;

/// <summary>
/// Thrown when a first-party Agent Tool or MCP Memory operation cannot be
/// composed into a trusted causal envelope before Memory domain execution.
/// This is a fail-closed contract violation, distinct from
/// <see cref="AgentMemoryReadCoreException"/> which signals business
/// unavailability after an operation is admitted.
/// </summary>
public sealed class AgentMemoryCapabilityCausalityException : Exception
{
    public string Code { get; }

    public AgentMemoryCapabilityCausalityException(string code, string message)
        : base(message)
    {
        Code = code;
    }
}
