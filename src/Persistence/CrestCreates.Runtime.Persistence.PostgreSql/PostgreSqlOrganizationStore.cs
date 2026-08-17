using System.Text.Json;
using CrestCreates.Organization.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.PostgreSql;
using Npgsql;

#pragma warning disable IL2026, IL3050 // TODO: Replace with generated JsonTypeInfo for NativeAOT (Slice 5)

namespace CrestCreates.Runtime.Persistence.PostgreSql;

internal sealed class PostgreSqlOrganizationStore : IOrganizationStore
{
    private readonly PostgreSqlRuntimePersistenceOptions _options;
    private readonly NpgsqlDataSource _dataSource;
    private readonly PostgreSqlRuntimeTransactionCoordinator _coordinator;

    public PostgreSqlOrganizationStore(
        PostgreSqlRuntimePersistenceOptions options,
        NpgsqlDataSource dataSource,
        PostgreSqlRuntimeTransactionCoordinator coordinator)
    {
        _options = options;
        _dataSource = dataSource;
        _coordinator = coordinator;
    }

    public async Task SaveOrganizationUnitAsync(OrganizationUnit organizationUnit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OrganizationStoreSemantics.ValidateSaveOrganizationUnit(organizationUnit);
        var snapshot = organizationUnit.Snapshot();
        var json = JsonSerializer.Serialize(snapshot);
        var table = PostgreSqlControlPlaneReferenceDataStoreSupport.Table(_options, "organization_units");
        var (scope, tenant) = ScopeTenant(snapshot.TenantId);

        await _coordinator.ExecuteTopLevelAsync(async ct =>
        {
            var session = _coordinator.RequireSession();
            var sql = $"""
                insert into {table}
                    (tenant_scope_kind, tenant_id, organization_unit_id, parent_id, sort_order, is_active,
                     created_at_utc_ticks, created_at, state_contract_version, state_json, updated_at)
                values
                    (@scope, @tenant, @orgUnitId, @parentId, @sortOrder, @isActive,
                     @createdAtUtcTicks, @createdAt, @stateContractVersion, @stateJson::jsonb, clock_timestamp())
                on conflict (tenant_scope_kind, tenant_id, organization_unit_id) do update set
                    parent_id = excluded.parent_id,
                    sort_order = excluded.sort_order,
                    is_active = excluded.is_active,
                    created_at_utc_ticks = excluded.created_at_utc_ticks,
                    created_at = excluded.created_at,
                    state_json = excluded.state_json,
                    updated_at = clock_timestamp()
                """;
            await using var cmd = PostgreSqlControlPlaneReferenceDataStoreSupport.CreateWriteCommand(session, _options, sql);
            cmd.Parameters.AddWithValue("scope", scope);
            cmd.Parameters.AddWithValue("tenant", tenant);
            cmd.Parameters.AddWithValue("orgUnitId", snapshot.Id);
            cmd.Parameters.AddWithValue("parentId", (object?)snapshot.ParentId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("sortOrder", snapshot.SortOrder);
            cmd.Parameters.AddWithValue("isActive", snapshot.IsActive);
            cmd.Parameters.AddWithValue("createdAtUtcTicks", snapshot.CreatedAt.UtcTicks);
            cmd.Parameters.AddWithValue("createdAt", snapshot.CreatedAt.UtcDateTime);
            cmd.Parameters.AddWithValue("stateContractVersion", PostgreSqlControlPlaneReferenceDataStoreSupport.StateContractVersion);
            cmd.Parameters.AddWithValue("stateJson", json);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OrganizationUnit?> GetOrganizationUnitByIdAsync(string organizationUnitId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OrganizationStoreSemantics.ValidatePointReadId(organizationUnitId, nameof(organizationUnitId));
        var (scope, tenant) = ScopeTenant(tenantId);
        var table = PostgreSqlControlPlaneReferenceDataStoreSupport.Table(_options, "organization_units");
        var sql = $"""
            select state_json from {table}
            where tenant_scope_kind=@scope and tenant_id=@tenant and organization_unit_id=@id
            """;
        return await ReadEntityAsync<OrganizationUnit>(sql, cancellationToken, ("scope", scope), ("tenant", tenant), ("id", organizationUnitId)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OrganizationUnit>> GetOrganizationUnitsAsync(string? tenantId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OrganizationStoreSemantics.ValidateQueryTenantId(tenantId);
        var table = PostgreSqlControlPlaneReferenceDataStoreSupport.Table(_options, "organization_units");
        string sql;
        if (tenantId is not null)
        {
            var (scope, tenant) = ScopeTenant(tenantId);
            sql = $"""
                select state_json from {table}
                where tenant_scope_kind=@scope and tenant_id=@tenant
                order by sort_order, tenant_scope_kind, tenant_id, organization_unit_id
                """;
            return await ReadListAsync<OrganizationUnit>(sql, cancellationToken, ("scope", scope), ("tenant", tenant)).ConfigureAwait(false);
        }
        else
        {
            sql = $"""
                select state_json from {table}
                order by sort_order, tenant_scope_kind, tenant_id, organization_unit_id
                """;
            return await ReadListAsync<OrganizationUnit>(sql, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task SavePositionAsync(Position position, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OrganizationStoreSemantics.ValidateSavePosition(position);
        var snapshot = position.Snapshot();
        var json = JsonSerializer.Serialize(snapshot);
        var table = PostgreSqlControlPlaneReferenceDataStoreSupport.Table(_options, "organization_positions");
        var (scope, tenant) = ScopeTenant(snapshot.TenantId);

        await _coordinator.ExecuteTopLevelAsync(async ct =>
        {
            var session = _coordinator.RequireSession();
            var sql = $"""
                insert into {table}
                    (tenant_scope_kind, tenant_id, position_id, is_active,
                     created_at_utc_ticks, created_at, state_contract_version, state_json, updated_at)
                values
                    (@scope, @tenant, @positionId, @isActive,
                     @createdAtUtcTicks, @createdAt, @stateContractVersion, @stateJson::jsonb, clock_timestamp())
                on conflict (tenant_scope_kind, tenant_id, position_id) do update set
                    is_active = excluded.is_active,
                    created_at_utc_ticks = excluded.created_at_utc_ticks,
                    created_at = excluded.created_at,
                    state_json = excluded.state_json,
                    updated_at = clock_timestamp()
                """;
            await using var cmd = PostgreSqlControlPlaneReferenceDataStoreSupport.CreateWriteCommand(session, _options, sql);
            cmd.Parameters.AddWithValue("scope", scope);
            cmd.Parameters.AddWithValue("tenant", tenant);
            cmd.Parameters.AddWithValue("positionId", snapshot.Id);
            cmd.Parameters.AddWithValue("isActive", snapshot.IsActive);
            cmd.Parameters.AddWithValue("createdAtUtcTicks", snapshot.CreatedAt.UtcTicks);
            cmd.Parameters.AddWithValue("createdAt", snapshot.CreatedAt.UtcDateTime);
            cmd.Parameters.AddWithValue("stateContractVersion", PostgreSqlControlPlaneReferenceDataStoreSupport.StateContractVersion);
            cmd.Parameters.AddWithValue("stateJson", json);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Position?> GetPositionByIdAsync(string positionId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OrganizationStoreSemantics.ValidatePointReadId(positionId, nameof(positionId));
        var (scope, tenant) = ScopeTenant(tenantId);
        var table = PostgreSqlControlPlaneReferenceDataStoreSupport.Table(_options, "organization_positions");
        var sql = $"""
            select state_json from {table}
            where tenant_scope_kind=@scope and tenant_id=@tenant and position_id=@id
            """;
        return await ReadEntityAsync<Position>(sql, cancellationToken, ("scope", scope), ("tenant", tenant), ("id", positionId)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Position>> GetPositionsAsync(string? tenantId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OrganizationStoreSemantics.ValidateQueryTenantId(tenantId);
        var table = PostgreSqlControlPlaneReferenceDataStoreSupport.Table(_options, "organization_positions");
        string sql;
        if (tenantId is not null)
        {
            var (scope, tenant) = ScopeTenant(tenantId);
            sql = $"""
                select state_json from {table}
                where tenant_scope_kind=@scope and tenant_id=@tenant
                order by tenant_scope_kind, tenant_id, position_id
                """;
            return await ReadListAsync<Position>(sql, cancellationToken, ("scope", scope), ("tenant", tenant)).ConfigureAwait(false);
        }
        else
        {
            sql = $"""
                select state_json from {table}
                order by tenant_scope_kind, tenant_id, position_id
                """;
            return await ReadListAsync<Position>(sql, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task SaveMembershipAsync(UserOrganizationMembership membership, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OrganizationStoreSemantics.ValidateSaveMembership(membership);
        var snapshot = membership.Snapshot();
        var json = JsonSerializer.Serialize(snapshot);
        var table = PostgreSqlControlPlaneReferenceDataStoreSupport.Table(_options, "organization_memberships");
        var (scope, tenant) = ScopeTenant(snapshot.TenantId);

        await _coordinator.ExecuteTopLevelAsync(async ct =>
        {
            var session = _coordinator.RequireSession();
            var sql = $"""
                insert into {table}
                    (tenant_scope_kind, tenant_id, membership_id, user_id, organization_unit_id, position_id,
                     is_primary, is_active, created_at_utc_ticks, created_at, state_contract_version, state_json, updated_at)
                values
                    (@scope, @tenant, @membershipId, @userId, @orgUnitId, @positionId,
                     @isPrimary, @isActive, @createdAtUtcTicks, @createdAt, @stateContractVersion, @stateJson::jsonb, clock_timestamp())
                on conflict (tenant_scope_kind, tenant_id, membership_id) do update set
                    user_id = excluded.user_id,
                    organization_unit_id = excluded.organization_unit_id,
                    position_id = excluded.position_id,
                    is_primary = excluded.is_primary,
                    is_active = excluded.is_active,
                    created_at_utc_ticks = excluded.created_at_utc_ticks,
                    created_at = excluded.created_at,
                    state_json = excluded.state_json,
                    updated_at = clock_timestamp()
                """;
            await using var cmd = PostgreSqlControlPlaneReferenceDataStoreSupport.CreateWriteCommand(session, _options, sql);
            cmd.Parameters.AddWithValue("scope", scope);
            cmd.Parameters.AddWithValue("tenant", tenant);
            cmd.Parameters.AddWithValue("membershipId", snapshot.Id);
            cmd.Parameters.AddWithValue("userId", snapshot.UserId);
            cmd.Parameters.AddWithValue("orgUnitId", snapshot.OrganizationUnitId);
            cmd.Parameters.AddWithValue("positionId", (object?)snapshot.PositionId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("isPrimary", snapshot.IsPrimary);
            cmd.Parameters.AddWithValue("isActive", snapshot.IsActive);
            cmd.Parameters.AddWithValue("createdAtUtcTicks", snapshot.CreatedAt.UtcTicks);
            cmd.Parameters.AddWithValue("createdAt", snapshot.CreatedAt.UtcDateTime);
            cmd.Parameters.AddWithValue("stateContractVersion", PostgreSqlControlPlaneReferenceDataStoreSupport.StateContractVersion);
            cmd.Parameters.AddWithValue("stateJson", json);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<UserOrganizationMembership>> GetMembershipsByUserAsync(string userId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OrganizationStoreSemantics.ValidateUserId(userId, nameof(userId));
        var table = PostgreSqlControlPlaneReferenceDataStoreSupport.Table(_options, "organization_memberships");
        string sql;
        if (tenantId is not null)
        {
            var (scope, tenant) = ScopeTenant(tenantId);
            sql = $"""
                select state_json from {table}
                where user_id=@userId and tenant_scope_kind=@scope and tenant_id=@tenant
                order by created_at_utc_ticks, tenant_scope_kind, tenant_id, membership_id
                """;
            return await ReadListAsync<UserOrganizationMembership>(sql, cancellationToken, ("userId", userId), ("scope", scope), ("tenant", tenant)).ConfigureAwait(false);
        }
        else
        {
            sql = $"""
                select state_json from {table}
                where user_id=@userId
                order by created_at_utc_ticks, tenant_scope_kind, tenant_id, membership_id
                """;
            return await ReadListAsync<UserOrganizationMembership>(sql, cancellationToken, ("userId", userId)).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<UserOrganizationMembership>> GetMembershipsByOrganizationUnitAsync(string organizationUnitId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OrganizationStoreSemantics.ValidateOrganizationUnitId(organizationUnitId, nameof(organizationUnitId));
        var table = PostgreSqlControlPlaneReferenceDataStoreSupport.Table(_options, "organization_memberships");
        string sql;
        if (tenantId is not null)
        {
            var (scope, tenant) = ScopeTenant(tenantId);
            sql = $"""
                select state_json from {table}
                where organization_unit_id=@orgUnitId and tenant_scope_kind=@scope and tenant_id=@tenant
                order by created_at_utc_ticks, tenant_scope_kind, tenant_id, membership_id
                """;
            return await ReadListAsync<UserOrganizationMembership>(sql, cancellationToken, ("orgUnitId", organizationUnitId), ("scope", scope), ("tenant", tenant)).ConfigureAwait(false);
        }
        else
        {
            sql = $"""
                select state_json from {table}
                where organization_unit_id=@orgUnitId
                order by created_at_utc_ticks, tenant_scope_kind, tenant_id, membership_id
                """;
            return await ReadListAsync<UserOrganizationMembership>(sql, cancellationToken, ("orgUnitId", organizationUnitId)).ConfigureAwait(false);
        }
    }

    public async Task SaveRoleAssignmentAsync(UserOrganizationRoleAssignment assignment, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OrganizationStoreSemantics.ValidateSaveRoleAssignment(assignment);
        var snapshot = assignment.Snapshot();
        var json = JsonSerializer.Serialize(snapshot);
        var table = PostgreSqlControlPlaneReferenceDataStoreSupport.Table(_options, "organization_role_assignments");
        var (scope, tenant) = ScopeTenant(snapshot.TenantId);

        await _coordinator.ExecuteTopLevelAsync(async ct =>
        {
            var session = _coordinator.RequireSession();
            var sql = $"""
                insert into {table}
                    (tenant_scope_kind, tenant_id, assignment_id, user_id, role_id, organization_unit_id,
                     is_active, created_at_utc_ticks, created_at, state_contract_version, state_json, updated_at)
                values
                    (@scope, @tenant, @assignmentId, @userId, @roleId, @orgUnitId,
                     @isActive, @createdAtUtcTicks, @createdAt, @stateContractVersion, @stateJson::jsonb, clock_timestamp())
                on conflict (tenant_scope_kind, tenant_id, assignment_id) do update set
                    user_id = excluded.user_id,
                    role_id = excluded.role_id,
                    organization_unit_id = excluded.organization_unit_id,
                    is_active = excluded.is_active,
                    created_at_utc_ticks = excluded.created_at_utc_ticks,
                    created_at = excluded.created_at,
                    state_json = excluded.state_json,
                    updated_at = clock_timestamp()
                """;
            await using var cmd = PostgreSqlControlPlaneReferenceDataStoreSupport.CreateWriteCommand(session, _options, sql);
            cmd.Parameters.AddWithValue("scope", scope);
            cmd.Parameters.AddWithValue("tenant", tenant);
            cmd.Parameters.AddWithValue("assignmentId", snapshot.Id);
            cmd.Parameters.AddWithValue("userId", snapshot.UserId);
            cmd.Parameters.AddWithValue("roleId", snapshot.RoleId);
            cmd.Parameters.AddWithValue("orgUnitId", (object?)snapshot.OrganizationUnitId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("isActive", snapshot.IsActive);
            cmd.Parameters.AddWithValue("createdAtUtcTicks", snapshot.CreatedAt.UtcTicks);
            cmd.Parameters.AddWithValue("createdAt", snapshot.CreatedAt.UtcDateTime);
            cmd.Parameters.AddWithValue("stateContractVersion", PostgreSqlControlPlaneReferenceDataStoreSupport.StateContractVersion);
            cmd.Parameters.AddWithValue("stateJson", json);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<UserOrganizationRoleAssignment>> GetRoleAssignmentsByUserAsync(string userId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OrganizationStoreSemantics.ValidateUserId(userId, nameof(userId));
        var table = PostgreSqlControlPlaneReferenceDataStoreSupport.Table(_options, "organization_role_assignments");
        string sql;
        if (tenantId is not null)
        {
            var (scope, tenant) = ScopeTenant(tenantId);
            sql = $"""
                select state_json from {table}
                where user_id=@userId and tenant_scope_kind=@scope and tenant_id=@tenant
                order by created_at_utc_ticks, tenant_scope_kind, tenant_id, assignment_id
                """;
            return await ReadListAsync<UserOrganizationRoleAssignment>(sql, cancellationToken, ("userId", userId), ("scope", scope), ("tenant", tenant)).ConfigureAwait(false);
        }
        else
        {
            sql = $"""
                select state_json from {table}
                where user_id=@userId
                order by created_at_utc_ticks, tenant_scope_kind, tenant_id, assignment_id
                """;
            return await ReadListAsync<UserOrganizationRoleAssignment>(sql, cancellationToken, ("userId", userId)).ConfigureAwait(false);
        }
    }

    private static (string Scope, string Tenant) ScopeTenant(string? tenantId)
        => tenantId is null ? ("global", "") : ("tenant", tenantId);

    private async Task<T?> ReadEntityAsync<T>(string sql, CancellationToken ct, params (string name, object value)[] parameters) where T : class
    {
        return await PostgreSqlControlPlaneReferenceDataStoreSupport.ExecuteReadAsync(_dataSource, async (connection, innerCt) =>
        {
            await using var cmd = PostgreSqlControlPlaneReferenceDataStoreSupport.CreateReadCommand(connection, _options, sql);
            foreach (var (name, value) in parameters)
                cmd.Parameters.AddWithValue(name, value);
            await using var reader = await cmd.ExecuteReaderAsync(innerCt).ConfigureAwait(false);
            if (!await reader.ReadAsync(innerCt).ConfigureAwait(false))
                return null;
            var jsonStr = reader.GetString(0);
            return JsonSerializer.Deserialize<T>(jsonStr)
                ?? throw PostgreSqlControlPlaneReferenceDataStoreSupport.PersistedInvariant($"{typeof(T).Name} JSON deserialization returned null.");
        }, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<T>> ReadListAsync<T>(string sql, CancellationToken ct, params (string name, object value)[] parameters) where T : class
    {
        return await PostgreSqlControlPlaneReferenceDataStoreSupport.ExecuteReadAsync(_dataSource, async (connection, innerCt) =>
        {
            await using var cmd = PostgreSqlControlPlaneReferenceDataStoreSupport.CreateReadCommand(connection, _options, sql);
            foreach (var (name, value) in parameters)
                cmd.Parameters.AddWithValue(name, value);
            await using var reader = await cmd.ExecuteReaderAsync(innerCt).ConfigureAwait(false);
            var results = new List<T>();
            while (await reader.ReadAsync(innerCt).ConfigureAwait(false))
            {
                var jsonStr = reader.GetString(0);
                var entity = JsonSerializer.Deserialize<T>(jsonStr)
                    ?? throw PostgreSqlControlPlaneReferenceDataStoreSupport.PersistedInvariant($"{typeof(T).Name} JSON deserialization returned null.");
                results.Add(entity);
            }
            return (IReadOnlyList<T>)results;
        }, ct).ConfigureAwait(false);
    }
}
