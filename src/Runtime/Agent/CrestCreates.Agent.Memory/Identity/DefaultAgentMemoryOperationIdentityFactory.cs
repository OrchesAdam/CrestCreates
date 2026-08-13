using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;

namespace CrestCreates.Agent.Memory.Identity;

/// <summary>
/// Allocates the stable identity pair of an admitted Memory operation once per
/// capability execution using the host <see cref="TimeProvider"/>. OperationId is
/// an opaque first-party identifier; no tenant, source, or origin material is embedded.
/// </summary>
public sealed class DefaultAgentMemoryOperationIdentityFactory : IAgentMemoryOperationIdentityFactory
{
    private readonly TimeProvider _timeProvider;

    public DefaultAgentMemoryOperationIdentityFactory(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public AgentMemoryOperationIdentity Create() => new()
    {
        OperationId = $"op_{Guid.NewGuid():N}",
        OccurredAt = _timeProvider.GetUtcNow()
    };
}
