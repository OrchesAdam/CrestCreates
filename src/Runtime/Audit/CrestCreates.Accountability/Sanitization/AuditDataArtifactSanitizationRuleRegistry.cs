using CrestCreates.Accountability.Abstractions.Sanitization;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Semantics;
using CrestCreates.Accountability.Abstractions.Validation;

namespace CrestCreates.Accountability.Sanitization;

public sealed class AuditDataArtifactSanitizationRuleRegistry
{
    private readonly IReadOnlyDictionary<string, IAuditDataArtifactSanitizationRule> _rules;

    public AuditDataArtifactSanitizationRuleRegistry(IEnumerable<IAuditDataArtifactSanitizationRule> rules)
    {
        var map = new Dictionary<string, IAuditDataArtifactSanitizationRule>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            ArgumentNullException.ThrowIfNull(rule);
            if (!AuditSemanticNames.IsStableKind(rule.Kind, AuditContractLimits.MaxSemanticKindLength)
                || rule.RuleVersion <= 0)
                throw new InvalidOperationException($"Invalid accountability artifact sanitization rule '{rule.Kind}'.");
            if (!map.TryAdd(rule.Kind, rule))
                throw new InvalidOperationException($"Duplicate accountability artifact sanitization rule Kind '{rule.Kind}'.");
        }
        _rules = map;
    }

    public AuditDataArtifact Sanitize(AuditDataArtifact artifact)
    {
        if (!_rules.TryGetValue(artifact.Kind, out var rule))
            throw new AuditSanitizationException("AUDIT_UNKNOWN_SANITIZATION_RULE", "DataSnapshot.Artifacts.Kind");
        AuditDataArtifact sanitized;
        try
        {
            sanitized = rule.Sanitize(artifact);
        }
        catch (AuditSanitizationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AuditSanitizationException("AUDIT_SANITIZATION_RULE_FAILED", "DataSnapshot.Artifacts", exception);
        }
        if (sanitized is null)
            throw new AuditSanitizationException("AUDIT_SANITIZED_OUTPUT_INVALID", "DataSnapshot.Artifacts");
        if (!string.Equals(artifact.Kind, sanitized.Kind, StringComparison.Ordinal))
            throw new AuditSanitizationException("AUDIT_SANITIZER_REWROTE_PROTECTED_FACT", "DataSnapshot.Artifacts.Kind");
        return sanitized;
    }

    public IReadOnlyCollection<IAuditDataArtifactSanitizationRule> Rules => _rules.Values.ToArray();

    public string GetAppliedRuleId(string kind)
    {
        if (!_rules.TryGetValue(kind, out var rule))
            throw new AuditSanitizationException("AUDIT_UNKNOWN_SANITIZATION_RULE", "DataSnapshot.Artifacts.Kind");
        return $"artifact:{kind}:v{rule.RuleVersion}";
    }
}
