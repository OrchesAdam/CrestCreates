namespace CrestCreates.Agent.ControlPlane.Abstractions;

public interface IAgentToolAuthorizationService
{
    Task<AgentToolAuthorizationResult> AuthorizeAsync(
        AgentToolInvocationContext context,
        AgentToolPermissionRequirement permission,
        string expectedToolName,
        CancellationToken ct = default);

    /// <summary>
    /// Checks whether a resolved descriptor kind is denied by policy.
    /// Returns <c>true</c> if the kind is in <see cref="AgentToolAuthorizationOptions.DeniedDescriptorKinds"/>.
    /// If <paramref name="descriptorKind"/> is <c>null</c> and any descriptor kinds are denied,
    /// returns <c>true</c> (fail-closed: unknown kind is treated as potentially denied).
    /// </summary>
    bool IsDescriptorKindDenied(string? descriptorKind);
}
