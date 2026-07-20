using System.Globalization;

namespace CrestCreates.Agent.Tools;

internal static class AgentToolAuditFactValidator
{
    public static bool Validate(
        IReadOnlyList<AgentToolAuditFact> facts,
        int maximum,
        AgentToolAuditProjectionContract? contract)
        => facts.Count <= maximum
            && (contract is null || facts.Count <= contract.MaximumFacts)
            && facts.All(fact => fact is not null
                && fact.Kind != AgentToolAuditFactKind.Unknown
                && !string.IsNullOrWhiteSpace(fact.Code)
                && fact.Code.Length <= 96
                && fact.Value?.Length <= 256
                && TryValidateDefinition(fact, contract))
            && facts.Select(fact => fact.Code).Distinct(StringComparer.Ordinal).Count() == facts.Count;

    private static bool TryValidateDefinition(AgentToolAuditFact fact, AgentToolAuditProjectionContract? contract)
    {
        if (contract is null)
            return true;
        var definitions = contract.Definitions
            .Where(Matches)
            .ToArray();
        if (definitions.Length != 1 || definitions[0].Kind != fact.Kind)
            return false;
        var definition = definitions[0];
        if (definition.AllowedValues is not null
            && (fact.Value is null || !definition.AllowedValues.Contains(fact.Value)))
            return false;
        return definition.ValueEncoding switch
        {
            AgentToolAuditFactValueEncoding.Text => !string.IsNullOrWhiteSpace(fact.Value),
            AgentToolAuditFactValueEncoding.Integer => long.TryParse(fact.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            AgentToolAuditFactValueEncoding.Boolean => fact.Value is "true" or "false",
            AgentToolAuditFactValueEncoding.Hash => fact.Value is { Length: 64 }
                && fact.Value.All(Uri.IsHexDigit),
            _ => false
        };

        bool Matches(AgentToolAuditFactDefinition definition)
        {
            if (definition.MatchKind == AgentToolAuditFactMatchKind.Exact)
                return string.Equals(fact.Code, definition.CodePrefix, StringComparison.Ordinal)
                    && definition.CodeSuffix.Length == 0;
            if (definition.MatchKind != AgentToolAuditFactMatchKind.Indexed
                || !fact.Code.StartsWith(definition.CodePrefix, StringComparison.Ordinal)
                || !fact.Code.EndsWith(definition.CodeSuffix, StringComparison.Ordinal))
                return false;
            var length = fact.Code.Length - definition.CodePrefix.Length - definition.CodeSuffix.Length;
            if (length <= 0)
                return false;
            var indexText = fact.Code.Substring(definition.CodePrefix.Length, length);
            return int.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                && index >= 0 && index < definition.MaximumIndex;
        }
    }
}
