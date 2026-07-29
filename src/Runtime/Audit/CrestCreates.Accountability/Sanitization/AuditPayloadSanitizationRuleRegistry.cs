using CrestCreates.Accountability.Abstractions.Sanitization;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Semantics;
using CrestCreates.Accountability.Abstractions.Validation;

namespace CrestCreates.Accountability.Sanitization;

public sealed class AuditPayloadSanitizationRuleRegistry
{
    private readonly IReadOnlyDictionary<string, IAuditPayloadSanitizationRule> _rules;

    public AuditPayloadSanitizationRuleRegistry(IEnumerable<IAuditPayloadSanitizationRule> rules)
    {
        var map = new Dictionary<string, IAuditPayloadSanitizationRule>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            ArgumentNullException.ThrowIfNull(rule);
            if (!AuditSemanticNames.IsStableKind(rule.Kind, AuditContractLimits.MaxSemanticKindLength)
                || rule.RuleVersion <= 0)
                throw new InvalidOperationException($"Invalid accountability payload sanitization rule '{rule.Kind}'.");
            if (!map.TryAdd(rule.Kind, rule))
                throw new InvalidOperationException($"Duplicate accountability payload sanitization rule Kind '{rule.Kind}'.");
        }
        _rules = map;
    }

    public AuditPayload Sanitize(AuditPayload payload)
    {
        if (!_rules.TryGetValue(payload.Kind, out var rule))
            throw new AuditSanitizationException("AUDIT_UNKNOWN_SANITIZATION_RULE", "Payload.Kind");
        AuditPayload sanitized;
        try
        {
            sanitized = rule.Sanitize(payload);
        }
        catch (AuditSanitizationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AuditSanitizationException("AUDIT_SANITIZATION_RULE_FAILED", "Payload", exception);
        }
        if (sanitized is null)
            throw new AuditSanitizationException("AUDIT_SANITIZED_OUTPUT_INVALID", "Payload");
        if (!string.Equals(payload.Kind, sanitized.Kind, StringComparison.Ordinal)
            || payload.Version != sanitized.Version)
            throw new AuditSanitizationException("AUDIT_SANITIZER_REWROTE_PROTECTED_FACT", "Payload");
        return sanitized;
    }

    public IReadOnlyCollection<IAuditPayloadSanitizationRule> Rules => _rules.Values.ToArray();

    public string GetAppliedRuleId(string kind)
    {
        if (!_rules.TryGetValue(kind, out var rule))
            throw new AuditSanitizationException("AUDIT_UNKNOWN_SANITIZATION_RULE", "Payload.Kind");
        return $"payload:{kind}:v{rule.RuleVersion}";
    }
}
