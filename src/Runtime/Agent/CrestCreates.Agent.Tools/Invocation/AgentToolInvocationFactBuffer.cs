namespace CrestCreates.Agent.Tools;

internal sealed class AgentToolInvocationFactBuffer : IAgentToolInvocationFactBuffer
{
    private readonly object _sync = new();
    private readonly List<AgentToolAuditFact> _facts = new();
    private int _maximum = 64;
    private bool _sealed;

    public void AddTrustedFacts(IReadOnlyList<AgentToolAuditFact> facts, int requestedMaximum)
    {
        ArgumentNullException.ThrowIfNull(facts);
        lock (_sync)
        {
            if (_sealed)
                throw new InvalidOperationException("Invocation audit facts are already sealed.");
            if (requestedMaximum < 0)
                throw new ArgumentOutOfRangeException(nameof(requestedMaximum));
            _maximum = Math.Min(_maximum, requestedMaximum);
            foreach (var fact in facts)
            {
                if (fact is null || string.IsNullOrWhiteSpace(fact.Code)
                    || fact.Code.Length > 96 || fact.Value?.Length > 256)
                    throw new ArgumentException("Audit fact shape is invalid.", nameof(facts));
                _facts.Add(fact);
                if (_facts.Count > _maximum)
                    throw new InvalidOperationException("Invocation audit fact limit exceeded.");
            }
        }
    }

    public AgentToolInvocationFactSnapshot Seal()
    {
        lock (_sync)
        {
            if (_sealed)
                throw new InvalidOperationException("Invocation audit facts are already sealed.");
            _sealed = true;
            return new AgentToolInvocationFactSnapshot(_facts.ToArray(), _maximum);
        }
    }
}

internal sealed class AgentToolInvocationFactBufferFactory : IAgentToolInvocationFactBufferFactory
{
    public IAgentToolInvocationFactBuffer Create() => new AgentToolInvocationFactBuffer();
}
