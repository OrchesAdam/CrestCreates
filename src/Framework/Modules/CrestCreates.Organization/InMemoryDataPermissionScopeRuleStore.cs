using System.Collections.Concurrent;
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization;

public sealed class InMemoryDataPermissionScopeRuleStore : IDataPermissionScopeRuleStore
{
    private readonly ConcurrentDictionary<string, DataPermissionScopeKind> _rules = new();

    public Task<DataPermissionScopeKind?> GetScopeKindAsync(
        string resource,
        string? action,
        string? permission,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        // Match priority: all tenant rules before all global rules.
        // Within each group: exact > wildcard-permission > wildcard-action.
        var keys = new[]
        {
            $"{resource}::{action ?? "*"}::{permission ?? "*"}::{tenantId ?? "*"}",  // tenant exact
            $"{resource}::{action ?? "*"}::*::{tenantId ?? "*"}",                     // tenant wildcard perm
            $"{resource}::*::*::{tenantId ?? "*"}",                                    // tenant wildcard action
            $"{resource}::{action ?? "*"}::{permission ?? "*"}::*",                   // global exact
            $"{resource}::{action ?? "*"}::*::*",                                      // global wildcard perm
            $"{resource}::*::*::*",                                                    // global wildcard action
        };

        foreach (var key in keys)
        {
            if (_rules.TryGetValue(key, out var kind))
                return Task.FromResult<DataPermissionScopeKind?>(kind);
        }

        return Task.FromResult<DataPermissionScopeKind?>(null);
    }

    public Task SaveRuleAsync(
        DataPermissionScopeRule rule,
        CancellationToken cancellationToken = default)
    {
        var key = $"{rule.Resource}::{rule.Action ?? "*"}::{rule.Permission ?? "*"}::{rule.TenantId ?? "*"}";
        _rules[key] = rule.ScopeKind;
        return Task.CompletedTask;
    }
}
