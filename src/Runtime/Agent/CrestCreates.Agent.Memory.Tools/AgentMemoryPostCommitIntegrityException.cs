namespace CrestCreates.Agent.Memory.Tools;

/// <summary>
/// Indicates that a confirmed curation commit returned a graph different from
/// the immutable output that was preflighted before mutation. The Agent Tool
/// Invoker treats this terminal condition as indeterminate rather than as a
/// structured zero-write Conflict.
/// </summary>
internal sealed class AgentMemoryPostCommitIntegrityException : InvalidOperationException
{
    public AgentMemoryPostCommitIntegrityException(string message)
        : base(message)
    {
    }
}
