using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.ControlPlane;

/// <summary>
/// Decision returned by the descriptor kind visibility policy evaluator.
/// </summary>
internal enum AgentDescriptorKindDecision
{
    Visible,
    Denied,
    Invalid
}

/// <summary>
/// Typed policy evaluator for descriptor kind visibility.
///
/// <para>Evaluation rules:</para>
/// <list type="number">
///   <item>If the <see cref="DescriptorKind"/> value is not a defined enum member,
///         returns <see cref="AgentDescriptorKindDecision.Invalid"/></item>
///   <item>If the kind's canonical name is in the deny set, returns
///         <see cref="AgentDescriptorKindDecision.Denied"/> (deny wins over allow)</item>
///   <item>In open-world mode (DevelopmentAllowAll), all remaining valid kinds are visible</item>
///   <item>In closed-world modes (ExplicitPolicy, DenyAll), only kinds in the allow set are visible;
///         an empty allow set means no descriptor is visible</item>
/// </list>
///
/// <para>Production/locked-down callers must populate AllowedDescriptorKinds;
/// empty means no descriptor visibility. Legacy policy conversion forwards denies
/// and remains closed-world.</para>
/// </summary>
internal sealed class AgentDescriptorKindPolicyEvaluator
{
    private readonly bool _openWorld;
    private readonly IReadOnlySet<string> _allowed;
    private readonly IReadOnlySet<string> _denied;

    /// <summary>
    /// Whether any descriptor kind restrictions are active.
    /// True when in closed-world mode or when deny rules exist.
    /// </summary>
    public bool HasRestrictions => !_openWorld || _denied.Count != 0;

    public AgentDescriptorKindPolicyEvaluator(AgentToolAuthorizationOptions options)
    {
        _openWorld = options.Mode == AgentToolAuthorizationMode.DevelopmentAllowAll;
        _allowed = options.AllowedDescriptorKinds.ToHashSet(StringComparer.Ordinal);
        _denied = options.DeniedDescriptorKinds.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Evaluates whether the given descriptor kind is visible, denied, or invalid.
    /// </summary>
    public AgentDescriptorKindDecision Evaluate(DescriptorKind kind)
    {
        if (!IsValidDescriptorKind(kind)) return AgentDescriptorKindDecision.Invalid;

        var canonical = kind.ToString();
        if (_denied.Contains(canonical)) return AgentDescriptorKindDecision.Denied;

        return _openWorld || _allowed.Contains(canonical)
            ? AgentDescriptorKindDecision.Visible
            : AgentDescriptorKindDecision.Denied;
    }

    /// <summary>
    /// Trim/AoT-safe validation that a <see cref="DescriptorKind"/> value
    /// is a defined enum member. Replaces <c>Enum.IsDefined</c> which
    /// relies on runtime reflection and is not trim-safe.
    /// </summary>
    internal static bool IsValidDescriptorKind(DescriptorKind kind)
    {
        return kind is DescriptorKind.Schema
            or DescriptorKind.Capability
            or DescriptorKind.Event
            or DescriptorKind.Workflow
            or DescriptorKind.Form
            or DescriptorKind.HumanTask
            or DescriptorKind.DynamicApiEndpoint;
    }
}
