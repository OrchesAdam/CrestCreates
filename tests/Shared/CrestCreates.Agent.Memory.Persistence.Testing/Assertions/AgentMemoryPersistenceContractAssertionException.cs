namespace CrestCreates.Agent.Memory.Persistence.Testing.Assertions;

public sealed class AgentMemoryPersistenceContractAssertionException : Exception
{
    public AgentMemoryPersistenceContractAssertionException(string message)
        : base(message)
    {
    }
}
