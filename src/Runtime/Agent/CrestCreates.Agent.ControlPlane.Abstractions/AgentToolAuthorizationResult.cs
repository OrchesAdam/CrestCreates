namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record AgentToolAuthorizationResult
{
    public required bool IsAllowed { get; init; }
    public required IReadOnlyList<AgentToolDiagnostic> DenialDiagnostics { get; init; }

    public static AgentToolAuthorizationResult Allowed()
        => new() { IsAllowed = true, DenialDiagnostics = Array.Empty<AgentToolDiagnostic>() };

    public static AgentToolAuthorizationResult Denied(params AgentToolDiagnostic[] diagnostics)
        => new() { IsAllowed = false, DenialDiagnostics = diagnostics };
}
