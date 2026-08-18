using System.Text.Json.Serialization.Metadata;
using CrestCreates.Organization.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.PostgreSql;
using Npgsql;

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
        await PostgreSqlRuntimeTestHooks.NotifyAfterReferenceSnapshotCapturedAsync(cancellationToken).ConfigureAwait(false);
        var json = PostgreSqlRuntimeStoreSupport.Serialize(
            snapshot,
            PostgreSqlControlPlaneReferenceDataJsonSerializerContext.Default.OrganizationUnit);
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
            cmd.Parameters.AddWithValue(
                "createdAt",
                PostgreSqlControlPlaneReferenceDataStoreSupport.ReadableTimestamp(snapshot.CreatedAt));
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
            select organization_unit_id, tenant_scope_kind, tenant_id, parent_id,
                   sort_order, is_active, created_at_utc_ticks, created_at,
                   state_contract_version, state_json
            from {table}
            where tenant_scope_kind=@scope and tenant_id=@tenant and organization_unit_id=@id
            """;
        return await ReadEntityAsync(
            sql,
            PostgreSqlControlPlaneReferenceDataJsonSerializerContext.Default.OrganizationUnit,
            ValidateOrganizationUnit,
            9,
            cancellationToken,
            ("scope", scope), ("tenant", tenant), ("id", organizationUnitId)).ConfigureAwait(false);
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
                select organization_unit_id, tenant_scope_kind, tenant_id, parent_id,
                       sort_order, is_active, created_at_utc_ticks, created_at,
                       state_contract_version, state_json
                from {table}
                where tenant_scope_kind=@scope and tenant_id=@tenant
                order by sort_order, tenant_scope_kind, tenant_id, organization_unit_id
                """;
            return await ReadListAsync(
                sql,
                PostgreSqlControlPlaneReferenceDataJsonSerializerContext.Default.OrganizationUnit,
                ValidateOrganizationUnit,
                9,
                OrganizationStoreSemantics.OrganizationUnitComparer,
                cancellationToken,
                ("scope", scope), ("tenant", tenant)).ConfigureAwait(false);
        }
        else
        {
            sql = $"""
                select organization_unit_id, tenant_scope_kind, tenant_id, parent_id,
                       sort_order, is_active, created_at_utc_ticks, created_at,
                       state_contract_version, state_json
                from {table}
                order by sort_order, tenant_scope_kind, tenant_id, organization_unit_id
                """;
            return await ReadListAsync(
                sql,
                PostgreSqlControlPlaneReferenceDataJsonSerializerContext.Default.OrganizationUnit,
                ValidateOrganizationUnit,
                9,
                OrganizationStoreSemantics.OrganizationUnitComparer,
                cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task SavePositionAsync(Position position, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OrganizationStoreSemantics.ValidateSavePosition(position);
        var snapshot = position.Snapshot();
        await PostgreSqlRuntimeTestHooks.NotifyAfterReferenceSnapshotCapturedAsync(cancellationToken).ConfigureAwait(false);
        var json = PostgreSqlRuntimeStoreSupport.Serialize(
            snapshot,
            PostgreSqlControlPlaneReferenceDataJsonSerializerContext.Default.Position);
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
            cmd.Parameters.AddWithValue(
                "createdAt",
                PostgreSqlControlPlaneReferenceDataStoreSupport.ReadableTimestamp(snapshot.CreatedAt));
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
            select position_id, tenant_scope_kind, tenant_id, is_active,
                   created_at_utc_ticks, created_at, state_contract_version, state_json
            from {table}
            where tenant_scope_kind=@scope and tenant_id=@tenant and position_id=@id
            """;
        return await ReadEntityAsync(
            sql,
            PostgreSqlControlPlaneReferenceDataJsonSerializerContext.Default.Position,
            ValidatePosition,
            7,
            cancellationToken,
            ("scope", scope), ("tenant", tenant), ("id", positionId)).ConfigureAwait(false);
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
                select position_id, tenant_scope_kind, tenant_id, is_active,
                       created_at_utc_ticks, created_at, state_contract_version, state_json
                from {table}
                where tenant_scope_kind=@scope and tenant_id=@tenant
                order by tenant_scope_kind, tenant_id, position_id
                """;
            return await ReadListAsync(
                sql,
                PostgreSqlControlPlaneReferenceDataJsonSerializerContext.Default.Position,
                ValidatePosition,
                7,
                OrganizationStoreSemantics.PositionComparer,
                cancellationToken,
                ("scope", scope), ("tenant", tenant)).ConfigureAwait(false);
        }
        else
        {
            sql = $"""
                select position_id, tenant_scope_kind, tenant_id, is_active,
                       created_at_utc_ticks, created_at, state_contract_version, state_json
                from {table}
                order by tenant_scope_kind, tenant_id, position_id
                """;
            return await ReadListAsync(
                sql,
                PostgreSqlControlPlaneReferenceDataJsonSerializerContext.Default.Position,
                ValidatePosition,
                7,
                OrganizationStoreSemantics.PositionComparer,
                cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task SaveMembershipAsync(UserOrganizationMembership membership, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OrganizationStoreSemantics.ValidateSaveMembership(membership);
        var snapshot = membership.Snapshot();
        await PostgreSqlRuntimeTestHooks.NotifyAfterReferenceSnapshotCapturedAsync(cancellationToken).ConfigureAwait(false);
        var json = PostgreSqlRuntimeStoreSupport.Serialize(
            snapshot,
            PostgreSqlControlPlaneReferenceDataJsonSerializerContext.Default.UserOrganizationMembership);
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
            cmd.Parameters.AddWithValue(
                "createdAt",
                PostgreSqlControlPlaneReferenceDataStoreSupport.ReadableTimestamp(snapshot.CreatedAt));
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
                select membership_id, tenant_scope_kind, tenant_id, user_id,
                       organization_unit_id, position_id, is_primary, is_active,
                       created_at_utc_ticks, created_at, state_contract_version, state_json
                from {table}
                where user_id=@userId and tenant_scope_kind=@scope and tenant_id=@tenant
                order by created_at_utc_ticks, tenant_scope_kind, tenant_id, membership_id
                """;
            return await ReadListAsync(
                sql,
                PostgreSqlControlPlaneReferenceDataJsonSerializerContext.Default.UserOrganizationMembership,
                ValidateMembership,
                11,
                OrganizationStoreSemantics.MembershipByUserComparer,
                cancellationToken,
                ("userId", userId), ("scope", scope), ("tenant", tenant)).ConfigureAwait(false);
        }
        else
        {
            sql = $"""
                select membership_id, tenant_scope_kind, tenant_id, user_id,
                       organization_unit_id, position_id, is_primary, is_active,
                       created_at_utc_ticks, created_at, state_contract_version, state_json
                from {table}
                where user_id=@userId
                order by created_at_utc_ticks, tenant_scope_kind, tenant_id, membership_id
                """;
            return await ReadListAsync(
                sql,
                PostgreSqlControlPlaneReferenceDataJsonSerializerContext.Default.UserOrganizationMembership,
                ValidateMembership,
                11,
                OrganizationStoreSemantics.MembershipByUserComparer,
                cancellationToken,
                ("userId", userId)).ConfigureAwait(false);
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
                select membership_id, tenant_scope_kind, tenant_id, user_id,
                       organization_unit_id, position_id, is_primary, is_active,
                       created_at_utc_ticks, created_at, state_contract_version, state_json
                from {table}
                where organization_unit_id=@orgUnitId and tenant_scope_kind=@scope and tenant_id=@tenant
                order by created_at_utc_ticks, tenant_scope_kind, tenant_id, membership_id
                """;
            return await ReadListAsync(
                sql,
                PostgreSqlControlPlaneReferenceDataJsonSerializerContext.Default.UserOrganizationMembership,
                ValidateMembership,
                11,
                OrganizationStoreSemantics.MembershipByUnitComparer,
                cancellationToken,
                ("orgUnitId", organizationUnitId), ("scope", scope), ("tenant", tenant)).ConfigureAwait(false);
        }
        else
        {
            sql = $"""
                select membership_id, tenant_scope_kind, tenant_id, user_id,
                       organization_unit_id, position_id, is_primary, is_active,
                       created_at_utc_ticks, created_at, state_contract_version, state_json
                from {table}
                where organization_unit_id=@orgUnitId
                order by created_at_utc_ticks, tenant_scope_kind, tenant_id, membership_id
                """;
            return await ReadListAsync(
                sql,
                PostgreSqlControlPlaneReferenceDataJsonSerializerContext.Default.UserOrganizationMembership,
                ValidateMembership,
                11,
                OrganizationStoreSemantics.MembershipByUnitComparer,
                cancellationToken,
                ("orgUnitId", organizationUnitId)).ConfigureAwait(false);
        }
    }

    public async Task SaveRoleAssignmentAsync(UserOrganizationRoleAssignment assignment, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OrganizationStoreSemantics.ValidateSaveRoleAssignment(assignment);
        var snapshot = assignment.Snapshot();
        await PostgreSqlRuntimeTestHooks.NotifyAfterReferenceSnapshotCapturedAsync(cancellationToken).ConfigureAwait(false);
        var json = PostgreSqlRuntimeStoreSupport.Serialize(
            snapshot,
            PostgreSqlControlPlaneReferenceDataJsonSerializerContext.Default.UserOrganizationRoleAssignment);
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
            cmd.Parameters.AddWithValue(
                "createdAt",
                PostgreSqlControlPlaneReferenceDataStoreSupport.ReadableTimestamp(snapshot.CreatedAt));
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
                select assignment_id, tenant_scope_kind, tenant_id, user_id,
                       role_id, organization_unit_id, is_active, created_at_utc_ticks,
                       created_at, state_contract_version, state_json
                from {table}
                where user_id=@userId and tenant_scope_kind=@scope and tenant_id=@tenant
                order by created_at_utc_ticks, tenant_scope_kind, tenant_id, assignment_id
                """;
            return await ReadListAsync(
                sql,
                PostgreSqlControlPlaneReferenceDataJsonSerializerContext.Default.UserOrganizationRoleAssignment,
                ValidateRoleAssignment,
                10,
                OrganizationStoreSemantics.RoleAssignmentComparer,
                cancellationToken,
                ("userId", userId), ("scope", scope), ("tenant", tenant)).ConfigureAwait(false);
        }
        else
        {
            sql = $"""
                select assignment_id, tenant_scope_kind, tenant_id, user_id,
                       role_id, organization_unit_id, is_active, created_at_utc_ticks,
                       created_at, state_contract_version, state_json
                from {table}
                where user_id=@userId
                order by created_at_utc_ticks, tenant_scope_kind, tenant_id, assignment_id
                """;
            return await ReadListAsync(
                sql,
                PostgreSqlControlPlaneReferenceDataJsonSerializerContext.Default.UserOrganizationRoleAssignment,
                ValidateRoleAssignment,
                10,
                OrganizationStoreSemantics.RoleAssignmentComparer,
                cancellationToken,
                ("userId", userId)).ConfigureAwait(false);
        }
    }

    private static (string Scope, string Tenant) ScopeTenant(string? tenantId)
        => tenantId is null ? ("global", "") : ("tenant", tenantId);

    private async Task<T?> ReadEntityAsync<T>(
        string sql,
        JsonTypeInfo<T> typeInfo,
        Action<NpgsqlDataReader, T> validate,
        int jsonOrdinal,
        CancellationToken ct,
        params (string name, object value)[] parameters) where T : class
    {
        return await PostgreSqlControlPlaneReferenceDataStoreSupport.ExecuteReadAsync(_dataSource, async (connection, innerCt) =>
        {
            await using var cmd = PostgreSqlControlPlaneReferenceDataStoreSupport.CreateReadCommand(connection, _options, sql);
            foreach (var (name, value) in parameters)
                cmd.Parameters.AddWithValue(name, value);
            await using var reader = await cmd.ExecuteReaderAsync(innerCt).ConfigureAwait(false);
            if (!await reader.ReadAsync(innerCt).ConfigureAwait(false))
                return null;
            var entity = PostgreSqlRuntimeStoreSupport.Deserialize(reader.GetString(jsonOrdinal), typeInfo);
            validate(reader, entity);
            return entity;
        }, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<T>> ReadListAsync<T>(
        string sql,
        JsonTypeInfo<T> typeInfo,
        Action<NpgsqlDataReader, T> validate,
        int jsonOrdinal,
        IComparer<T> comparer,
        CancellationToken ct,
        params (string name, object value)[] parameters) where T : class
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
                var entity = PostgreSqlRuntimeStoreSupport.Deserialize(reader.GetString(jsonOrdinal), typeInfo);
                validate(reader, entity);
                results.Add(entity);
            }
            results.Sort(comparer);
            return results.AsReadOnly();
        }, ct).ConfigureAwait(false);
    }

    private static void ValidateOrganizationUnit(NpgsqlDataReader reader, OrganizationUnit value)
    {
        var (scope, tenant) = ScopeTenant(value.TenantId);
        if (!string.Equals(reader.GetString(0), value.Id, StringComparison.Ordinal)
            || !string.Equals(reader.GetString(1), scope, StringComparison.Ordinal)
            || !string.Equals(reader.GetString(2), tenant, StringComparison.Ordinal)
            || !OptionalStringMatches(reader, 3, value.ParentId)
            || reader.GetInt32(4) != value.SortOrder
            || reader.GetBoolean(5) != value.IsActive
            || reader.GetInt64(6) != value.CreatedAt.UtcTicks
            || reader.GetInt32(8) != PostgreSqlControlPlaneReferenceDataStoreSupport.StateContractVersion)
        {
            throw PostgreSqlControlPlaneReferenceDataStoreSupport.PersistedInvariant(
                "OrganizationUnit structured columns disagree with the JSON snapshot.");
        }
        ValidateReadableTimestamp(reader, 7, value.CreatedAt, "OrganizationUnit");
    }

    private static void ValidatePosition(NpgsqlDataReader reader, Position value)
    {
        var (scope, tenant) = ScopeTenant(value.TenantId);
        if (!string.Equals(reader.GetString(0), value.Id, StringComparison.Ordinal)
            || !string.Equals(reader.GetString(1), scope, StringComparison.Ordinal)
            || !string.Equals(reader.GetString(2), tenant, StringComparison.Ordinal)
            || reader.GetBoolean(3) != value.IsActive
            || reader.GetInt64(4) != value.CreatedAt.UtcTicks
            || reader.GetInt32(6) != PostgreSqlControlPlaneReferenceDataStoreSupport.StateContractVersion)
        {
            throw PostgreSqlControlPlaneReferenceDataStoreSupport.PersistedInvariant(
                "Position structured columns disagree with the JSON snapshot.");
        }
        ValidateReadableTimestamp(reader, 5, value.CreatedAt, "Position");
    }

    private static void ValidateMembership(NpgsqlDataReader reader, UserOrganizationMembership value)
    {
        var (scope, tenant) = ScopeTenant(value.TenantId);
        if (!string.Equals(reader.GetString(0), value.Id, StringComparison.Ordinal)
            || !string.Equals(reader.GetString(1), scope, StringComparison.Ordinal)
            || !string.Equals(reader.GetString(2), tenant, StringComparison.Ordinal)
            || !string.Equals(reader.GetString(3), value.UserId, StringComparison.Ordinal)
            || !string.Equals(reader.GetString(4), value.OrganizationUnitId, StringComparison.Ordinal)
            || !OptionalStringMatches(reader, 5, value.PositionId)
            || reader.GetBoolean(6) != value.IsPrimary
            || reader.GetBoolean(7) != value.IsActive
            || reader.GetInt64(8) != value.CreatedAt.UtcTicks
            || reader.GetInt32(10) != PostgreSqlControlPlaneReferenceDataStoreSupport.StateContractVersion)
        {
            throw PostgreSqlControlPlaneReferenceDataStoreSupport.PersistedInvariant(
                "Membership structured columns disagree with the JSON snapshot.");
        }
        ValidateReadableTimestamp(reader, 9, value.CreatedAt, "Membership");
    }

    private static void ValidateRoleAssignment(NpgsqlDataReader reader, UserOrganizationRoleAssignment value)
    {
        var (scope, tenant) = ScopeTenant(value.TenantId);
        if (!string.Equals(reader.GetString(0), value.Id, StringComparison.Ordinal)
            || !string.Equals(reader.GetString(1), scope, StringComparison.Ordinal)
            || !string.Equals(reader.GetString(2), tenant, StringComparison.Ordinal)
            || !string.Equals(reader.GetString(3), value.UserId, StringComparison.Ordinal)
            || !string.Equals(reader.GetString(4), value.RoleId, StringComparison.Ordinal)
            || !OptionalStringMatches(reader, 5, value.OrganizationUnitId)
            || reader.GetBoolean(6) != value.IsActive
            || reader.GetInt64(7) != value.CreatedAt.UtcTicks
            || reader.GetInt32(9) != PostgreSqlControlPlaneReferenceDataStoreSupport.StateContractVersion)
        {
            throw PostgreSqlControlPlaneReferenceDataStoreSupport.PersistedInvariant(
                "Role assignment structured columns disagree with the JSON snapshot.");
        }
        ValidateReadableTimestamp(reader, 8, value.CreatedAt, "Role assignment");
    }

    private static bool OptionalStringMatches(NpgsqlDataReader reader, int ordinal, string? expected)
        => reader.IsDBNull(ordinal)
            ? expected is null
            : expected is not null && string.Equals(reader.GetString(ordinal), expected, StringComparison.Ordinal);

    private static void ValidateReadableTimestamp(
        NpgsqlDataReader reader,
        int ordinal,
        DateTimeOffset expected,
        string entityName)
    {
        var expectedTicks = expected.UtcTicks - expected.UtcTicks % TimeSpan.TicksPerMicrosecond;
        if (reader.GetDateTime(ordinal).Ticks != expectedTicks)
        {
            throw PostgreSqlControlPlaneReferenceDataStoreSupport.PersistedInvariant(
                $"{entityName} readable timestamp disagrees with the JSON snapshot.");
        }
    }
}
