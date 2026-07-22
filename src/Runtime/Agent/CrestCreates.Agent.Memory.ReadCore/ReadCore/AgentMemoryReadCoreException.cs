namespace CrestCreates.Agent.Memory.ReadCore;

/// <summary>
/// Exception thrown by ReadCore when a validation or resolution step fails.
/// Carries a machine-readable code and human-readable message.
/// </summary>
public sealed class AgentMemoryReadCoreException : Exception
{
    public string Code { get; }

    public AgentMemoryReadCoreException(string code, string message)
        : base(message)
    {
        Code = code;
    }
}
