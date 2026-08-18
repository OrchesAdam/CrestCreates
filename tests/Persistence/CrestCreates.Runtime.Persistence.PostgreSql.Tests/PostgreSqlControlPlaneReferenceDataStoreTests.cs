using CrestCreates.DescriptorDraft;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Organization.Abstractions;
using CrestCreates.Runtime.Persistence.PostgreSql;
using CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

[Collection(PostgreSqlRuntimeCollection.Name)]
public sealed class PostgreSqlControlPlaneReferenceDataStoreTests
{
    private readonly PostgreSqlRuntimeCollectionFixture _fixture;

    public PostgreSqlControlPlaneReferenceDataStoreTests(PostgreSqlRuntimeCollectionFixture fixture)
        => _fixture = fixture;

    [Fact]
    public async Task Real_feature_stores_round_trip_generated_json_contracts()
    {
        await using var lease = await _fixture.CreateSchemaLeaseAsync();
        var services = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();

        await using var provider = services.BuildServiceProvider();
        var drafts = provider.GetRequiredService<IDescriptorDraftStore>();
        var organizations = provider.GetRequiredService<IOrganizationStore>();

        var draft = new Draft
        {
            TenantId = "tenant-1",
            DraftId = "draft-1",
            DescriptorKind = Metadata.Abstractions.DescriptorKind.Schema,
            DescriptorId = "schema-1",
            Operation = DescriptorDraftOperation.Create,
            AuthorKind = DescriptorDraftAuthorKind.System,
            AuthorId = "system",
            CreatedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(3)),
            Payload = new SchemaDescriptorDraftPayload(new SchemaDescriptor
            {
                Id = "schema-1",
                Name = "Schema",
                Fields = new[]
                {
                    new SchemaFieldDescriptor { Name = "Name", FieldType = "string", IsRequired = true }
                }
            })
        };

        await drafts.SaveAsync(draft);
        (await drafts.GetAsync(draft.TenantId, draft.DraftId)).Should().BeEquivalentTo(draft);

        var organizationUnit = new OrganizationUnit
        {
            Id = "unit-1",
            TenantId = "tenant-1",
            Name = "Unit",
            Code = "U-1",
            ParentId = null,
            SortOrder = 1,
            CreatedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)
        };
        await organizations.SaveOrganizationUnitAsync(organizationUnit);

        (await organizations.GetOrganizationUnitByIdAsync(organizationUnit.Id, organizationUnit.TenantId))
            .Should().BeEquivalentTo(organizationUnit);

        var rules = provider.GetRequiredService<IDataPermissionScopeRuleStore>();
        await rules.SaveRuleAsync(new DataPermissionScopeRule
        {
            Resource = "reference-data",
            Action = "read",
            Permission = "view",
            TenantId = "tenant-1",
            ScopeKind = DataPermissionScopeKind.Self
        });
        (await rules.GetScopeKindAsync("reference-data", "read", "view", "tenant-1"))
            .Should().Be(DataPermissionScopeKind.Self);
    }

    [Fact]
    public async Task Draft_list_applies_all_query_predicates()
    {
        await using var lease = await _fixture.CreateSchemaLeaseAsync();
        var services = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDescriptorDraftStore>();

        await store.SaveAsync(CreateDraft("draft-a", DescriptorDraftOperation.Create, DescriptorDraftStatus.Created,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        await store.SaveAsync(CreateDraft("draft-b", DescriptorDraftOperation.Update, DescriptorDraftStatus.Reviewed,
            new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero)));
        await store.SaveAsync(CreateDraft("draft-c", DescriptorDraftOperation.Update, DescriptorDraftStatus.Created,
            new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero)));

        var result = await store.ListAsync("tenant-1", new DraftQuery
        {
            DescriptorKind = Metadata.Abstractions.DescriptorKind.Schema,
            Operation = DescriptorDraftOperation.Update,
            AuthorKind = DescriptorDraftAuthorKind.System,
            Status = DescriptorDraftStatus.Reviewed,
            CreatedFrom = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
            CreatedTo = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero)
        });

        result.Select(value => value.DraftId).Should().Equal("draft-b");
    }

    [Fact]
    public async Task Draft_read_rejects_structured_column_mismatch()
    {
        await using var lease = await _fixture.CreateSchemaLeaseAsync();
        var services = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDescriptorDraftStore>();
        var draft = CreateDraft("draft-corrupt", DescriptorDraftOperation.Create, DescriptorDraftStatus.Created,
            DateTimeOffset.UnixEpoch);
        await store.SaveAsync(draft);

        await using (var connection = new NpgsqlConnection(lease.Options.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                $"update \"{lease.Options.Schema}\".control_plane_descriptor_drafts set created_at_utc_ticks = created_at_utc_ticks + 1 where tenant_id = @tenant and draft_id = @draft",
                connection);
            command.Parameters.AddWithValue("tenant", draft.TenantId);
            command.Parameters.AddWithValue("draft", draft.DraftId);
            await command.ExecuteNonQueryAsync();
        }

        var act = () => store.GetAsync(draft.TenantId, draft.DraftId);
        (await act.Should().ThrowAsync<RuntimePersistenceContractException>())
            .Which.Code.Should().Be(RuntimePersistenceContractErrorCode.PersistedInvariantViolation);
    }

    [Fact]
    public async Task Organization_read_rejects_structured_column_mismatch()
    {
        await using var lease = await _fixture.CreateSchemaLeaseAsync();
        var services = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IOrganizationStore>();
        var unit = new OrganizationUnit { Id = "unit-corrupt", TenantId = "tenant-1", Name = "Unit" };
        await store.SaveOrganizationUnitAsync(unit);

        await using (var connection = new NpgsqlConnection(lease.Options.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                $"update \"{lease.Options.Schema}\".organization_units set parent_id = 'unexpected-parent' where tenant_scope_kind = 'tenant' and tenant_id = @tenant and organization_unit_id = @id",
                connection);
            command.Parameters.AddWithValue("tenant", unit.TenantId!);
            command.Parameters.AddWithValue("id", unit.Id);
            await command.ExecuteNonQueryAsync();
        }

        var act = () => store.GetOrganizationUnitByIdAsync(unit.Id, unit.TenantId);
        (await act.Should().ThrowAsync<RuntimePersistenceContractException>())
            .Which.Code.Should().Be(RuntimePersistenceContractErrorCode.PersistedInvariantViolation);
    }

    [Fact]
    public async Task Rule_read_rejects_invalid_persisted_scope_kind()
    {
        await using var lease = await _fixture.CreateSchemaLeaseAsync();
        var services = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDataPermissionScopeRuleStore>();
        await store.SaveRuleAsync(new DataPermissionScopeRule
        {
            Resource = "corrupt-rule",
            Action = "read",
            Permission = "view",
            TenantId = "tenant-1",
            ScopeKind = DataPermissionScopeKind.Self
        });

        await using (var connection = new NpgsqlConnection(lease.Options.ConnectionString))
        {
            await connection.OpenAsync();
            await using var drop = new NpgsqlCommand(
                $"alter table \"{lease.Options.Schema}\".data_permission_scope_rules drop constraint ck_data_permission_scope_kind",
                connection);
            await drop.ExecuteNonQueryAsync();
            await using var update = new NpgsqlCommand(
                $"update \"{lease.Options.Schema}\".data_permission_scope_rules set scope_kind = 99 where tenant_scope_kind = 'tenant' and tenant_id = 'tenant-1' and resource = 'corrupt-rule'",
                connection);
            await update.ExecuteNonQueryAsync();
        }

        var act = () => store.GetScopeKindAsync("corrupt-rule", "read", "view", "tenant-1");
        (await act.Should().ThrowAsync<RuntimePersistenceContractException>())
            .Which.Code.Should().Be(RuntimePersistenceContractErrorCode.PersistedInvariantViolation);
    }

    [Fact]
    public async Task Draft_read_rejects_invalid_payload_discriminator()
    {
        await using var lease = await _fixture.CreateSchemaLeaseAsync();
        var services = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDescriptorDraftStore>();
        var draft = CreateDraft("draft-discriminator", DescriptorDraftOperation.Create, DescriptorDraftStatus.Created,
            DateTimeOffset.UnixEpoch);
        await store.SaveAsync(draft);

        await using (var connection = new NpgsqlConnection(lease.Options.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                $"update \"{lease.Options.Schema}\".control_plane_descriptor_drafts set state_json = jsonb_set(state_json, '{{payloadType}}', '999'::jsonb) where tenant_id = @tenant and draft_id = @draft",
                connection);
            command.Parameters.AddWithValue("tenant", draft.TenantId);
            command.Parameters.AddWithValue("draft", draft.DraftId);
            await command.ExecuteNonQueryAsync();
        }

        var act = () => store.GetAsync(draft.TenantId, draft.DraftId);
        (await act.Should().ThrowAsync<RuntimePersistenceContractException>())
            .Which.Code.Should().Be(RuntimePersistenceContractErrorCode.PersistedInvariantViolation);
    }

    [Theory]
    [InlineData("organization_units", "organization_unit_id", "unit-invalid-json")]
    [InlineData("organization_positions", "position_id", "position-invalid-json")]
    [InlineData("organization_memberships", "membership_id", "membership-invalid-json")]
    [InlineData("organization_role_assignments", "assignment_id", "role-invalid-json")]
    public async Task Organization_read_rejects_invalid_persisted_json(
        string table,
        string idColumn,
        string id)
    {
        await using var lease = await _fixture.CreateSchemaLeaseAsync();
        var services = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IOrganizationStore>();
        switch (table)
        {
            case "organization_units":
                await store.SaveOrganizationUnitAsync(new OrganizationUnit { Id = id, TenantId = "tenant-1", Name = id });
                break;
            case "organization_positions":
                await store.SavePositionAsync(new Position { Id = id, TenantId = "tenant-1", Name = id });
                break;
            case "organization_memberships":
                await store.SaveMembershipAsync(new UserOrganizationMembership
                {
                    Id = id, TenantId = "tenant-1", UserId = "user", OrganizationUnitId = "unit"
                });
                break;
            case "organization_role_assignments":
                await store.SaveRoleAssignmentAsync(new UserOrganizationRoleAssignment
                {
                    Id = id, TenantId = "tenant-1", UserId = "user", RoleId = "role"
                });
                break;
        }

        await using (var connection = new NpgsqlConnection(lease.Options.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                $"update \"{lease.Options.Schema}\".{table} set state_json = 'null'::jsonb where tenant_scope_kind = 'tenant' and tenant_id = 'tenant-1' and {idColumn} = @id",
                connection);
            command.Parameters.AddWithValue("id", id);
            await command.ExecuteNonQueryAsync();
        }

        Func<Task> act = table switch
        {
            "organization_units" => async () => await store.GetOrganizationUnitByIdAsync(id, "tenant-1"),
            "organization_positions" => async () => await store.GetPositionByIdAsync(id, "tenant-1"),
            "organization_memberships" => async () => await store.GetMembershipsByUserAsync("user", "tenant-1"),
            "organization_role_assignments" => async () => await store.GetRoleAssignmentsByUserAsync("user", "tenant-1"),
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, null)
        };
        (await act.Should().ThrowAsync<RuntimePersistenceContractException>())
            .Which.Code.Should().Be(RuntimePersistenceContractErrorCode.PersistedInvariantViolation);
    }

    private static Draft CreateDraft(
        string draftId,
        DescriptorDraftOperation operation,
        DescriptorDraftStatus status,
        DateTimeOffset createdAt)
        => new()
        {
            TenantId = "tenant-1",
            DraftId = draftId,
            DescriptorKind = Metadata.Abstractions.DescriptorKind.Schema,
            DescriptorId = "schema-1",
            Operation = operation,
            AuthorKind = DescriptorDraftAuthorKind.System,
            AuthorId = "system",
            CreatedAt = createdAt,
            Status = status,
            Payload = new SchemaDescriptorDraftPayload(new SchemaDescriptor
            {
                Id = "schema-1",
                Name = "Schema"
            })
        };
}
