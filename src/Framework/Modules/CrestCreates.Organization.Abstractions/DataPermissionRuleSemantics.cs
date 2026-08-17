namespace CrestCreates.Organization.Abstractions;

internal enum DataPermissionMatchKind
{
    Exact = 0,
    Wildcard = 1
}

internal readonly record struct DataPermissionMatch(
    DataPermissionMatchKind Kind,
    string Value)
{
    public static DataPermissionMatch FromDomain(string? value) => value is null
        ? new DataPermissionMatch(DataPermissionMatchKind.Wildcard, "")
        : new DataPermissionMatch(DataPermissionMatchKind.Exact, value);

    public static void ValidateNotSentinel(string? value, string paramName)
    {
        if (value == "*")
            throw new ArgumentException("Literal \"*\" is not allowed as a DataPermission value.", paramName);
    }
}

internal readonly record struct DataPermissionRuleKey(
    string TenantScope,
    string TenantId,
    string Resource,
    DataPermissionMatch ActionMatch,
    DataPermissionMatch PermissionMatch)
{
    public static DataPermissionRuleKey FromRule(DataPermissionScopeRule rule)
    {
        var (scope, tenant) = rule.TenantId is null
            ? ("global", "")
            : ("tenant", rule.TenantId);
        return new DataPermissionRuleKey(
            scope,
            tenant,
            rule.Resource,
            DataPermissionMatch.FromDomain(rule.Action),
            DataPermissionMatch.FromDomain(rule.Permission));
    }
}

internal static class DataPermissionRuleSemantics
{
    public static void ValidateSaveRule(DataPermissionScopeRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (string.IsNullOrEmpty(rule.Resource))
            throw new ArgumentException("Rule.Resource must not be null or empty.", nameof(rule));
        DataPermissionMatch.ValidateNotSentinel(rule.Action, nameof(rule));
        DataPermissionMatch.ValidateNotSentinel(rule.Permission, nameof(rule));
        DataPermissionMatch.ValidateNotSentinel(rule.TenantId, nameof(rule));
        if (rule.TenantId is not null && string.IsNullOrWhiteSpace(rule.TenantId))
            throw new ArgumentException("Non-null Rule.TenantId must not be empty or whitespace.", nameof(rule));
    }

    public static IReadOnlyList<DataPermissionRuleKey> GenerateCandidates(
        string resource,
        string? action,
        string? permission,
        string? tenantId)
    {
        var actionMatch = DataPermissionMatch.FromDomain(action);
        var permissionMatch = DataPermissionMatch.FromDomain(permission);

        var candidates = new List<DataPermissionRuleKey>();

        string requestedScope, requestedTenant;
        if (tenantId is not null)
        {
            requestedScope = "tenant";
            requestedTenant = tenantId;
        }
        else
        {
            requestedScope = "global";
            requestedTenant = "";
        }

        candidates.Add(new DataPermissionRuleKey(requestedScope, requestedTenant, resource,
            actionMatch, permissionMatch));

        candidates.Add(new DataPermissionRuleKey(requestedScope, requestedTenant, resource,
            actionMatch, new DataPermissionMatch(DataPermissionMatchKind.Wildcard, "")));

        candidates.Add(new DataPermissionRuleKey(requestedScope, requestedTenant, resource,
            new DataPermissionMatch(DataPermissionMatchKind.Wildcard, ""),
            new DataPermissionMatch(DataPermissionMatchKind.Wildcard, "")));

        if (tenantId is not null)
        {
            var globalActionMatch = actionMatch;
            candidates.Add(new DataPermissionRuleKey("global", "", resource,
                globalActionMatch, permissionMatch));

            candidates.Add(new DataPermissionRuleKey("global", "", resource,
                globalActionMatch, new DataPermissionMatch(DataPermissionMatchKind.Wildcard, "")));

            candidates.Add(new DataPermissionRuleKey("global", "", resource,
                new DataPermissionMatch(DataPermissionMatchKind.Wildcard, ""),
                new DataPermissionMatch(DataPermissionMatchKind.Wildcard, "")));
        }

        return Deduplicate(candidates);
    }

    private static IReadOnlyList<DataPermissionRuleKey> Deduplicate(List<DataPermissionRuleKey> candidates)
    {
        var seen = new HashSet<DataPermissionRuleKey>();
        var result = new List<DataPermissionRuleKey>();
        foreach (var c in candidates)
        {
            if (seen.Add(c))
                result.Add(c);
        }
        return result;
    }
}
