using CrestCreates.Organization.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.PostgreSql;
using Npgsql;
using NpgsqlTypes;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

internal sealed class PostgreSqlDataPermissionScopeRuleStore : IDataPermissionScopeRuleStore
{
    private readonly PostgreSqlRuntimePersistenceOptions _options;
    private readonly NpgsqlDataSource _dataSource;
    private readonly PostgreSqlRuntimeTransactionCoordinator _coordinator;

    public PostgreSqlDataPermissionScopeRuleStore(
        PostgreSqlRuntimePersistenceOptions options,
        NpgsqlDataSource dataSource,
        PostgreSqlRuntimeTransactionCoordinator coordinator)
    {
        _options = options;
        _dataSource = dataSource;
        _coordinator = coordinator;
    }

    public async Task SaveRuleAsync(DataPermissionScopeRule rule, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DataPermissionRuleSemantics.ValidateSaveRule(rule);
        var key = DataPermissionRuleKey.FromRule(rule);
        var table = PostgreSqlControlPlaneReferenceDataStoreSupport.Table(_options, "data_permission_scope_rules");

        await _coordinator.ExecuteTopLevelAsync(async ct =>
        {
            var session = _coordinator.RequireSession();
            var sql = $"""
                insert into {table}
                    (tenant_scope_kind, tenant_id, resource, action_match_kind, action_value,
                     permission_match_kind, permission_value, scope_kind, updated_at)
                values
                    (@scope, @tenant, @resource, @actionMatchKind, @actionValue,
                     @permissionMatchKind, @permissionValue, @scopeKind, clock_timestamp())
                on conflict (tenant_scope_kind, tenant_id, resource,
                    action_match_kind, action_value, permission_match_kind, permission_value)
                do update set scope_kind = excluded.scope_kind, updated_at = clock_timestamp()
                """;
            await using var cmd = PostgreSqlControlPlaneReferenceDataStoreSupport.CreateWriteCommand(session, _options, sql);
            cmd.Parameters.AddWithValue("scope", key.TenantScope);
            cmd.Parameters.AddWithValue("tenant", key.TenantId);
            cmd.Parameters.AddWithValue("resource", key.Resource);
            cmd.Parameters.AddWithValue("actionMatchKind", (int)key.ActionMatch.Kind);
            cmd.Parameters.AddWithValue("actionValue", key.ActionMatch.Value);
            cmd.Parameters.AddWithValue("permissionMatchKind", (int)key.PermissionMatch.Kind);
            cmd.Parameters.AddWithValue("permissionValue", key.PermissionMatch.Value);
            cmd.Parameters.AddWithValue("scopeKind", (int)rule.ScopeKind);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DataPermissionScopeKind?> GetScopeKindAsync(
        string resource, string? action, string? permission, string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DataPermissionMatch.ValidateNotSentinel(action, nameof(action));
        DataPermissionMatch.ValidateNotSentinel(permission, nameof(permission));
        DataPermissionMatch.ValidateNotSentinel(tenantId, nameof(tenantId));

        var candidates = DataPermissionRuleSemantics.GenerateCandidates(resource, action, permission, tenantId);
        var table = PostgreSqlControlPlaneReferenceDataStoreSupport.Table(_options, "data_permission_scope_rules");

        var valuesClauses = new List<string>();
        for (int i = 0; i < candidates.Count; i++)
        {
            valuesClauses.Add($"(@p{i}_scope, @p{i}_tenant, @resource, @p{i}_ak, @p{i}_av, @p{i}_pk, @p{i}_pv)");
        }

        var sql = $"""
            with candidates(priority, tenant_scope_kind, tenant_id, resource,
                            action_match_kind, action_value,
                            permission_match_kind, permission_value) as (
                values {string.Join(", ", valuesClauses)}
            )
            select r.scope_kind
            from candidates c
            join {table} r
              on r.tenant_scope_kind=c.tenant_scope_kind
             and r.tenant_id=c.tenant_id
             and r.resource=c.resource
             and r.action_match_kind=c.action_match_kind
             and r.action_value=c.action_value
             and r.permission_match_kind=c.permission_match_kind
             and r.permission_value=c.permission_value
            order by c.priority
            limit 1
            """;

        return await PostgreSqlControlPlaneReferenceDataStoreSupport.ExecuteReadAsync<DataPermissionScopeKind?>(_dataSource, async (connection, innerCt) =>
        {
            await using var cmd = PostgreSqlControlPlaneReferenceDataStoreSupport.CreateReadCommand(connection, _options, sql);
            cmd.Parameters.AddWithValue("resource", resource);
            for (int i = 0; i < candidates.Count; i++)
            {
                cmd.Parameters.AddWithValue($"p{i}_scope", candidates[i].TenantScope);
                cmd.Parameters.AddWithValue($"p{i}_tenant", candidates[i].TenantId);
                cmd.Parameters.AddWithValue($"p{i}_ak", (int)candidates[i].ActionMatch.Kind);
                cmd.Parameters.AddWithValue($"p{i}_av", candidates[i].ActionMatch.Value);
                cmd.Parameters.AddWithValue($"p{i}_pk", (int)candidates[i].PermissionMatch.Kind);
                cmd.Parameters.AddWithValue($"p{i}_pv", candidates[i].PermissionMatch.Value);
            }
            await using var reader = await cmd.ExecuteReaderAsync(innerCt).ConfigureAwait(false);
            if (!await reader.ReadAsync(innerCt).ConfigureAwait(false))
                return null;
            var scopeKindInt = reader.GetInt32(0);
            if (!Enum.IsDefined(typeof(DataPermissionScopeKind), scopeKindInt))
                throw PostgreSqlControlPlaneReferenceDataStoreSupport.PersistedInvariant(
                    $"Invalid DataPermissionScopeKind value {scopeKindInt} in persisted rule.");
            return (DataPermissionScopeKind)scopeKindInt;
        }, cancellationToken).ConfigureAwait(false);
    }
}
