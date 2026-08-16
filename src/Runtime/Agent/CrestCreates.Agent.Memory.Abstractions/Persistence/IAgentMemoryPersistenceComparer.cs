namespace CrestCreates.Agent.Memory.Abstractions.Persistence;

/// <summary>
/// Exact persisted-snapshot equality for create-or-exact-replay Memory
/// semantics. Compares every persisted property including collection sequence
/// and nested snapshot values. State-hash equality is never used as the replay
/// equality test.
/// </summary>
public interface IAgentMemoryPersistenceComparer
{
    bool Equals(AgentMemoryItem left, AgentMemoryItem right);
}
