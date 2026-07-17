namespace CrestCreates.Agent.Tools;

public sealed class AgentCapabilityIdempotencyKeyBuilder
{
    public string Build(AgentToolInvocationFingerprint fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        if (string.IsNullOrWhiteSpace(fingerprint.Value))
            throw new ArgumentException("Invocation fingerprint is required.", nameof(fingerprint));

        return "agent:v1:" + fingerprint.Value;
    }
}
