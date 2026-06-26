using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.ControlPlane;

/// <summary>
/// Immutable per-invocation visibility scope created after coarse authorization.
/// Captures the tenant and the effective kind policy snapshot.
/// Performs policy decisions only — does not query stores, use reflection, or own business execution.
/// </summary>
internal sealed class AgentDescriptorVisibilityScope
{
    private readonly AgentDescriptorKindPolicyEvaluator _evaluator;

    public string TenantId { get; }

    /// <summary>
    /// Deterministic fingerprint of the scope's visibility policy.
    /// Used to verify that a cached package preview was built under the same scope.
    /// Format: "Mode:AllowedKinds:DeniedKinds" where kinds are sorted and comma-separated.
    /// </summary>
    public string ScopeFingerprint { get; }

    /// <summary>
    /// Whether any descriptor kind restrictions are active for this invocation.
    /// </summary>
    public bool IsRestricted => _evaluator.HasRestrictions;

    public AgentDescriptorVisibilityScope(string tenantId, AgentDescriptorKindPolicyEvaluator evaluator, string scopeFingerprint)
    {
        TenantId = tenantId;
        _evaluator = evaluator;
        ScopeFingerprint = scopeFingerprint;
    }

    /// <summary>
    /// Evaluates whether the given descriptor kind is explicitly visible, denied, or invalid.
    /// </summary>
    public AgentDescriptorKindDecision EvaluateExplicit(DescriptorKind kind) => _evaluator.Evaluate(kind);

    /// <summary>
    /// Returns <c>true</c> if the given descriptor kind is visible under this scope's policy.
    /// </summary>
    public bool IsVisible(DescriptorKind kind) => _evaluator.Evaluate(kind) == AgentDescriptorKindDecision.Visible;

    /// <summary>
    /// Filters a source collection to include only items whose descriptor kind is visible.
    /// Preserves the original order.
    /// </summary>
    public IReadOnlyList<T> Filter<T>(IEnumerable<T> source, Func<T, DescriptorKind> selector) =>
        source.Where(item => IsVisible(selector(item))).ToList().AsReadOnly();

    /// <summary>
    /// Computes a deterministic fingerprint from authorization options.
    /// Used to verify scope identity when reusing cached package previews.
    /// </summary>
    public static string ComputeFingerprint(AgentToolAuthorizationOptions options)
    {
        var allowed = string.Join(",", options.AllowedDescriptorKinds.Order(StringComparer.Ordinal));
        var denied = string.Join(",", options.DeniedDescriptorKinds.Order(StringComparer.Ordinal));
        return $"{options.Mode}:{allowed}:{denied}";
    }
}
