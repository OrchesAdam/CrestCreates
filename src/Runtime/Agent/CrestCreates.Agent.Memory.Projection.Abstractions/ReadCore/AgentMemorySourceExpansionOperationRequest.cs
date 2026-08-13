using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Tools;

namespace CrestCreates.Agent.Memory.Projection.Abstractions;

/// <summary>
/// Orchestration carrier for a Source Expansion ReadCore operation.
/// Principal, InvocationContext, Origin, and Scope must agree on Tenant and
/// caller identities. <see cref="Origin"/> remains the logical
/// security-artifact origin; <see cref="Identity"/> owns only this Memory
/// execution/fact, so equality between their OperationIds is neither required
/// nor expected.
/// </summary>
public sealed record AgentMemorySourceExpansionOperationRequest
{
    public required AgentMemoryAccessPrincipal Principal { get; init; }

    public required AgentMemoryArtifactOrigin Origin { get; init; }

    public required AgentMemoryOperationIdentity Identity { get; init; }

    public required AgentMemoryInvocationContext InvocationContext { get; init; }

    public required AgentMemoryAccessScope Scope { get; init; }

    public required ExpandAgentMemorySourceInput Input { get; init; }
}
