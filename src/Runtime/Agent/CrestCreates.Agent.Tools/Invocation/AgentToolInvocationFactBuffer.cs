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
            var validated = new List<AgentToolAuditFact>(facts.Count);
            foreach (var fact in facts)
            {
                if (fact is null || string.IsNullOrWhiteSpace(fact.Code)
                    || fact.Code.Length > 96 || fact.Value?.Length > 256
                    || fact.Kind == AgentToolAuditFactKind.Unknown)
                    throw new ArgumentException("Audit fact shape is invalid.", nameof(facts));
                validated.Add(fact);
            }
            if (validated.Select(item => item.Code).Distinct(StringComparer.Ordinal).Count() != validated.Count
                || _facts.Select(item => item.Code).Intersect(validated.Select(item => item.Code), StringComparer.Ordinal).Any())
                throw new ArgumentException("Audit fact names must be unique within an invocation.", nameof(facts));
            if (_facts.Count + validated.Count > _maximum)
                throw new InvalidOperationException("Invocation audit fact limit exceeded.");
            _facts.AddRange(validated);
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
