using System.Collections.Concurrent;
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization;

public sealed class InMemoryDataPermissionScopeRuleStore : IDataPermissionScopeRuleStore
{
    private readonly ConcurrentDictionary<DataPermissionRuleKey, DataPermissionScopeKind> _rules = new();

    public Task<DataPermissionScopeKind?> GetScopeKindAsync(
        string resource,
        string? action,
        string? permission,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DataPermissionRuleSemantics.ValidateLookup(resource, action, permission, tenantId);

        var candidates = DataPermissionRuleSemantics.GenerateCandidates(resource, action, permission, tenantId);

        foreach (var candidate in candidates)
        {
            if (_rules.TryGetValue(candidate, out var kind))
                return Task.FromResult<DataPermissionScopeKind?>(kind);
        }

        return Task.FromResult<DataPermissionScopeKind?>(null);
    }

    public Task SaveRuleAsync(
        DataPermissionScopeRule rule,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DataPermissionRuleSemantics.ValidateSaveRule(rule);
        var key = DataPermissionRuleKey.FromRule(rule);
        _rules[key] = rule.ScopeKind;
        return Task.CompletedTask;
    }
}
