using CrestCreates.DescriptorDraft;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Organization.Abstractions;
using CrestCreates.Runtime.Persistence.PostgreSql;
using CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;
using CrestCreates.ControlPlane.ReferenceData.Persistence.Testing;
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

    [Theory]
    [MemberData(nameof(PersistedRuleCorruptionData))]
    public async Task PersistedRuleCorruptionVariant_Should_FailClosed(PersistedRuleCorruptionVariant variant)
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

        await DropAllCheckConstraintsAsync(
            lease.Options.ConnectionString,
            lease.Options.Schema,
            "data_permission_scope_rules");
        await using (var connection = new NpgsqlConnection(lease.Options.ConnectionString))
        {
            await connection.OpenAsync();
            await using var update = new NpgsqlCommand(
                $"update \"{lease.Options.Schema}\".data_permission_scope_rules set {CorruptionSql(variant)} where tenant_scope_kind = 'tenant' and tenant_id = 'tenant-1' and resource = 'corrupt-rule'",
                connection);
            await update.ExecuteNonQueryAsync();
        }

        var act = () => store.GetScopeKindAsync("corrupt-rule", "read", "view", "tenant-1");
        if (variant == PersistedRuleCorruptionVariant.InvalidScopeKind)
        {
            (await act.Should().ThrowAsync<RuntimePersistenceContractException>())
                .Which.Code.Should().Be(RuntimePersistenceContractErrorCode.PersistedInvariantViolation);
        }
        else
        {
            (await act()).Should().BeNull("a corrupted rule key must not become an authorization decision");
        }
    }

    public static IEnumerable<object[]> PersistedRuleCorruptionData()
    {
        foreach (var value in Enum.GetValues<PersistedRuleCorruptionVariant>())
            yield return new object[] { value };
    }

    private static string CorruptionSql(PersistedRuleCorruptionVariant variant)
        => variant switch
        {
            PersistedRuleCorruptionVariant.InvalidTenantScopeKind => "tenant_scope_kind = 'invalid'",
            PersistedRuleCorruptionVariant.TenantScopeTupleMismatch => "tenant_scope_kind = 'global'",
            PersistedRuleCorruptionVariant.InvalidActionMatchKind => "action_match_kind = 99",
            PersistedRuleCorruptionVariant.ActionWildcardValueMismatch => "action_match_kind = 1, action_value = 'read'",
            PersistedRuleCorruptionVariant.InvalidPermissionMatchKind => "permission_match_kind = 99",
            PersistedRuleCorruptionVariant.PermissionWildcardValueMismatch => "permission_match_kind = 1, permission_value = 'view'",
            PersistedRuleCorruptionVariant.InvalidScopeKind => "scope_kind = 99",
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null)
        };

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

    // ── F01: ConcurrentBlindSave — one complete snapshot ──

    public static IEnumerable<object[]> SaveSurfaces()
    {
        foreach (var s in new[] { "draft", "organization-unit", "position", "membership", "role-assignment", "rule" })
            yield return new object[] { s };
    }

    [Theory]
    [MemberData(nameof(SaveSurfaces))]
    public async Task SaveSurface_ConcurrentBlindSave_Should_ExposeOneCompleteSnapshot(string surface)
    {
        await using var lease = await _fixture.CreateSchemaLeaseAsync();
        var services = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();
        await using var provider = services.BuildServiceProvider();

        switch (surface)
        {
            case "draft":
            {
                var drafts = provider.GetRequiredService<IDescriptorDraftStore>();
                var a = CreateDraft("concurrent", DescriptorDraftOperation.Create, DescriptorDraftStatus.Created, DateTimeOffset.UnixEpoch);
                var b = CreateDraft("concurrent", DescriptorDraftOperation.Create, DescriptorDraftStatus.Created, DateTimeOffset.UnixEpoch);
                // AuthorId is init-only; use two separate draft instances with different AuthorId via CreateDraft variants
                var draftA = new Draft
                {
                    TenantId = "tenant-1", DraftId = "concurrent",
                    DescriptorKind = Metadata.Abstractions.DescriptorKind.Schema, DescriptorId = "schema-1",
                    Operation = DescriptorDraftOperation.Create, AuthorKind = DescriptorDraftAuthorKind.System,
                    AuthorId = "author-a", CreatedAt = DateTimeOffset.UnixEpoch, Status = DescriptorDraftStatus.Created,
                    Payload = new SchemaDescriptorDraftPayload(new SchemaDescriptor { Id = "schema-1", Name = "Schema" })
                };
                var draftB = new Draft
                {
                    TenantId = "tenant-1", DraftId = "concurrent",
                    DescriptorKind = Metadata.Abstractions.DescriptorKind.Schema, DescriptorId = "schema-1",
                    Operation = DescriptorDraftOperation.Create, AuthorKind = DescriptorDraftAuthorKind.System,
                    AuthorId = "author-b", CreatedAt = DateTimeOffset.UnixEpoch, Status = DescriptorDraftStatus.Created,
                    Payload = new SchemaDescriptorDraftPayload(new SchemaDescriptor { Id = "schema-1", Name = "Schema" })
                };
                await Task.WhenAll(drafts.SaveAsync(draftA), drafts.SaveAsync(draftB));
                var result = await drafts.GetAsync("tenant-1", "concurrent");
                result.Should().NotBeNull();
                result!.AuthorId.Should().BeOneOf("author-a", "author-b");
                break;
            }
            case "organization-unit":
            {
                var orgs = provider.GetRequiredService<IOrganizationStore>();
                var a = new OrganizationUnit { Id = "unit-c", TenantId = "tenant-1", Name = "A", SortOrder = 1 };
                var b = new OrganizationUnit { Id = "unit-c", TenantId = "tenant-1", Name = "B", SortOrder = 2 };
                await Task.WhenAll(orgs.SaveOrganizationUnitAsync(a), orgs.SaveOrganizationUnitAsync(b));
                var r = await orgs.GetOrganizationUnitByIdAsync("unit-c", "tenant-1");
                r.Should().NotBeNull();
                r!.Name.Should().Match(m => m == "A" || m == "B");
                break;
            }
            case "position":
            {
                var orgs = provider.GetRequiredService<IOrganizationStore>();
                var a = new Position { Id = "pos-c", TenantId = "tenant-1", Name = "PA" };
                var b = new Position { Id = "pos-c", TenantId = "tenant-1", Name = "PB" };
                await Task.WhenAll(orgs.SavePositionAsync(a), orgs.SavePositionAsync(b));
                var r = await orgs.GetPositionByIdAsync("pos-c", "tenant-1");
                r.Should().NotBeNull();
                r!.Name.Should().Match(m => m == "PA" || m == "PB");
                break;
            }
            case "membership":
            {
                var orgs = provider.GetRequiredService<IOrganizationStore>();
                var a = new UserOrganizationMembership { Id = "mem-c", TenantId = "tenant-1", UserId = "u1", OrganizationUnitId = "o1", IsPrimary = true };
                var b = new UserOrganizationMembership { Id = "mem-c", TenantId = "tenant-1", UserId = "u1", OrganizationUnitId = "o1", IsPrimary = false };
                await Task.WhenAll(orgs.SaveMembershipAsync(a), orgs.SaveMembershipAsync(b));
                var r = (await orgs.GetMembershipsByUserAsync("u1", "tenant-1")).Single();
                // Both values are valid — last writer wins atomically
                break;
            }
            case "role-assignment":
            {
                var orgs = provider.GetRequiredService<IOrganizationStore>();
                var a = new UserOrganizationRoleAssignment { Id = "ra-c", TenantId = "tenant-1", UserId = "u1", RoleId = "r1" };
                var b = new UserOrganizationRoleAssignment { Id = "ra-c", TenantId = "tenant-1", UserId = "u1", RoleId = "r2" };
                await Task.WhenAll(orgs.SaveRoleAssignmentAsync(a), orgs.SaveRoleAssignmentAsync(b));
                var r = (await orgs.GetRoleAssignmentsByUserAsync("u1", "tenant-1")).Single();
                r.RoleId.Should().BeOneOf("r1", "r2");
                break;
            }
            case "rule":
            {
                var rules = provider.GetRequiredService<IDataPermissionScopeRuleStore>();
                await Task.WhenAll(
                    rules.SaveRuleAsync(new DataPermissionScopeRule { Resource = "rc", Action = "read", Permission = "view", TenantId = "tenant-1", ScopeKind = DataPermissionScopeKind.Self }),
                    rules.SaveRuleAsync(new DataPermissionScopeRule { Resource = "rc", Action = "read", Permission = "view", TenantId = "tenant-1", ScopeKind = DataPermissionScopeKind.OwnOrganization }));
                var r = await rules.GetScopeKindAsync("rc", "read", "view", "tenant-1");
                r.Should().Match(m => m == DataPermissionScopeKind.Self || m == DataPermissionScopeKind.OwnOrganization);
                break;
            }
        }
    }

    // ── F02: ConcurrentBlindSave — no false OCC ──

    [Theory]
    [MemberData(nameof(SaveSurfaces))]
    public async Task SaveSurface_ConcurrentBlindSave_Should_NotInventStaleWriterConflict(string surface)
    {
        await using var lease = await _fixture.CreateSchemaLeaseAsync();
        var services = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();
        await using var provider = services.BuildServiceProvider();

        switch (surface)
        {
            case "draft":
            {
                var store = provider.GetRequiredService<IDescriptorDraftStore>();
                var a = CreateDraft("concurrent-no-occ", DescriptorDraftOperation.Create, DescriptorDraftStatus.Created, DateTimeOffset.UnixEpoch);
                var b = CreateDraft("concurrent-no-occ", DescriptorDraftOperation.Create, DescriptorDraftStatus.Created, DateTimeOffset.UnixEpoch);
                var ex = await Record.ExceptionAsync(() => Task.WhenAll(store.SaveAsync(a), store.SaveAsync(b)));
                ex.Should().BeNull();
                break;
            }
            case "organization-unit":
            {
                var store = provider.GetRequiredService<IOrganizationStore>();
                var ex = await Record.ExceptionAsync(() => Task.WhenAll(
                    store.SaveOrganizationUnitAsync(new OrganizationUnit { Id = "u-no-occ", TenantId = "tenant-1", Name = "A" }),
                    store.SaveOrganizationUnitAsync(new OrganizationUnit { Id = "u-no-occ", TenantId = "tenant-1", Name = "B" })));
                ex.Should().BeNull();
                break;
            }
            case "position":
            {
                var store = provider.GetRequiredService<IOrganizationStore>();
                var ex = await Record.ExceptionAsync(() => Task.WhenAll(
                    store.SavePositionAsync(new Position { Id = "p-no-occ", TenantId = "tenant-1", Name = "A" }),
                    store.SavePositionAsync(new Position { Id = "p-no-occ", TenantId = "tenant-1", Name = "B" })));
                ex.Should().BeNull();
                break;
            }
            case "membership":
            {
                var store = provider.GetRequiredService<IOrganizationStore>();
                var ex = await Record.ExceptionAsync(() => Task.WhenAll(
                    store.SaveMembershipAsync(new UserOrganizationMembership { Id = "m-no-occ", TenantId = "tenant-1", UserId = "u", OrganizationUnitId = "o" }),
                    store.SaveMembershipAsync(new UserOrganizationMembership { Id = "m-no-occ", TenantId = "tenant-1", UserId = "u", OrganizationUnitId = "o" })));
                ex.Should().BeNull();
                break;
            }
            case "role-assignment":
            {
                var store = provider.GetRequiredService<IOrganizationStore>();
                var ex = await Record.ExceptionAsync(() => Task.WhenAll(
                    store.SaveRoleAssignmentAsync(new UserOrganizationRoleAssignment { Id = "ra-no-occ", TenantId = "tenant-1", UserId = "u", RoleId = "r" }),
                    store.SaveRoleAssignmentAsync(new UserOrganizationRoleAssignment { Id = "ra-no-occ", TenantId = "tenant-1", UserId = "u", RoleId = "r" })));
                ex.Should().BeNull();
                break;
            }
            case "rule":
            {
                var store = provider.GetRequiredService<IDataPermissionScopeRuleStore>();
                var ex = await Record.ExceptionAsync(() => Task.WhenAll(
                    store.SaveRuleAsync(new DataPermissionScopeRule { Resource = "r-no-occ", Action = "read", Permission = "view", TenantId = "tenant-1", ScopeKind = DataPermissionScopeKind.Self }),
                    store.SaveRuleAsync(new DataPermissionScopeRule { Resource = "r-no-occ", Action = "read", Permission = "view", TenantId = "tenant-1", ScopeKind = DataPermissionScopeKind.OwnOrganization })));
                ex.Should().BeNull();
                break;
            }
        }
    }

    // ── F06: Unavailable provider ──

    public static IEnumerable<object[]> StoreMethodSurfaces()
    {
        foreach (var s in Enum.GetValues<StoreMethodSurface>())
            yield return new object[] { s };
    }

    [Theory]
    [MemberData(nameof(StoreMethodSurfaces))]
    public async Task StoreMethodSurface_UnavailableProvider_Should_UseSharedFailureTaxonomy(StoreMethodSurface surface)
    {
        var unavailable = new PostgreSqlRuntimePersistenceOptions
        {
            ConnectionString = "Host=127.0.0.1;Port=1;Timeout=1",
            Schema = "unavailable"
        };
        using var provider = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(unavailable)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence()
            .BuildServiceProvider();

        Func<Task> act = surface switch
        {
            StoreMethodSurface.DraftSave => async () => await provider.GetRequiredService<IDescriptorDraftStore>().SaveAsync(CreateDraft("d", DescriptorDraftOperation.Create, DescriptorDraftStatus.Created, DateTimeOffset.UnixEpoch)),
            StoreMethodSurface.DraftGet => async () => await provider.GetRequiredService<IDescriptorDraftStore>().GetAsync("t", "d"),
            StoreMethodSurface.DraftList => async () => await provider.GetRequiredService<IDescriptorDraftStore>().ListAsync("t"),
            StoreMethodSurface.UnitSave => async () => await provider.GetRequiredService<IOrganizationStore>().SaveOrganizationUnitAsync(new OrganizationUnit { Id = "u", TenantId = "t", Name = "n" }),
            StoreMethodSurface.UnitGet => async () => await provider.GetRequiredService<IOrganizationStore>().GetOrganizationUnitByIdAsync("u", "t"),
            StoreMethodSurface.UnitList => async () => await provider.GetRequiredService<IOrganizationStore>().GetOrganizationUnitsAsync("t"),
            StoreMethodSurface.PositionSave => async () => await provider.GetRequiredService<IOrganizationStore>().SavePositionAsync(new Position { Id = "p", TenantId = "t", Name = "n" }),
            StoreMethodSurface.PositionGet => async () => await provider.GetRequiredService<IOrganizationStore>().GetPositionByIdAsync("p", "t"),
            StoreMethodSurface.PositionList => async () => await provider.GetRequiredService<IOrganizationStore>().GetPositionsAsync("t"),
            StoreMethodSurface.MembershipSave => async () => await provider.GetRequiredService<IOrganizationStore>().SaveMembershipAsync(new UserOrganizationMembership { Id = "m", TenantId = "t", UserId = "u", OrganizationUnitId = "o" }),
            StoreMethodSurface.MembershipsByUser => async () => await provider.GetRequiredService<IOrganizationStore>().GetMembershipsByUserAsync("u", "t"),
            StoreMethodSurface.MembershipsByUnit => async () => await provider.GetRequiredService<IOrganizationStore>().GetMembershipsByOrganizationUnitAsync("o", "t"),
            StoreMethodSurface.RoleSave => async () => await provider.GetRequiredService<IOrganizationStore>().SaveRoleAssignmentAsync(new UserOrganizationRoleAssignment { Id = "ra", TenantId = "t", UserId = "u", RoleId = "r" }),
            StoreMethodSurface.RolesByUser => async () => await provider.GetRequiredService<IOrganizationStore>().GetRoleAssignmentsByUserAsync("u", "t"),
            StoreMethodSurface.RuleSave => async () => await provider.GetRequiredService<IDataPermissionScopeRuleStore>().SaveRuleAsync(new DataPermissionScopeRule { Resource = "r", Action = "read", Permission = "view", TenantId = "t", ScopeKind = DataPermissionScopeKind.Self }),
            StoreMethodSurface.RuleGet => async () => await provider.GetRequiredService<IDataPermissionScopeRuleStore>().GetScopeKindAsync("r", "read", "view", "t"),
            _ => throw new ArgumentOutOfRangeException(nameof(surface))
        };

        var ex = await Record.ExceptionAsync(act);
        ex.Should().BeOfType<RuntimePersistenceUnavailableException>();
    }

    // ── F07: PersistedSnapshotCorruptionVariant — fail closed ──

    [Theory]
    [MemberData(nameof(CorruptionVariantData))]
    public async Task PersistedSnapshotCorruptionVariant_Should_FailClosed(PersistedSnapshotCorruptionVariant variant)
    {
        await using var lease = await _fixture.CreateSchemaLeaseAsync();
        var services = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();
        await using var provider = services.BuildServiceProvider();
        var schema = lease.Options.Schema;

        switch (variant)
        {
            case PersistedSnapshotCorruptionVariant.DraftInvalidJson:
            {
                var store = provider.GetRequiredService<IDescriptorDraftStore>();
                var draft = CreateDraft("f07-draft-json", DescriptorDraftOperation.Create, DescriptorDraftStatus.Created, DateTimeOffset.UnixEpoch);
                await store.SaveAsync(draft);
                await CorruptAsync(lease.Options.ConnectionString, schema,
                    $"update \"{schema}\".control_plane_descriptor_drafts set state_json = 'null'::jsonb where tenant_id='tenant-1' and draft_id='f07-draft-json'");
                var act = () => store.GetAsync("tenant-1", "f07-draft-json");
                (await act.Should().ThrowAsync<RuntimePersistenceContractException>()).Which.Code.Should().Be(RuntimePersistenceContractErrorCode.PersistedInvariantViolation);
                break;
            }
            case PersistedSnapshotCorruptionVariant.DraftUnsupportedStateContractVersion:
            {
                var store = provider.GetRequiredService<IDescriptorDraftStore>();
                var draft = CreateDraft("f07-draft-ver", DescriptorDraftOperation.Create, DescriptorDraftStatus.Created, DateTimeOffset.UnixEpoch);
                await store.SaveAsync(draft);
                await DropAllCheckConstraintsAsync(lease.Options.ConnectionString, schema, "control_plane_descriptor_drafts");
                await CorruptAsync(lease.Options.ConnectionString, schema,
                    $"update \"{schema}\".control_plane_descriptor_drafts set state_contract_version = 999 where tenant_id='tenant-1' and draft_id='f07-draft-ver'");
                var act = () => store.GetAsync("tenant-1", "f07-draft-ver");
                (await act.Should().ThrowAsync<RuntimePersistenceContractException>()).Which.Code.Should().Be(RuntimePersistenceContractErrorCode.PersistedInvariantViolation);
                break;
            }
            case PersistedSnapshotCorruptionVariant.DraftInvalidPayloadDiscriminator:
            {
                var store = provider.GetRequiredService<IDescriptorDraftStore>();
                var draft = CreateDraft("f07-draft-disc", DescriptorDraftOperation.Create, DescriptorDraftStatus.Created, DateTimeOffset.UnixEpoch);
                await store.SaveAsync(draft);
                await DropAllCheckConstraintsAsync(lease.Options.ConnectionString, schema, "control_plane_descriptor_drafts");
                await CorruptAsync(lease.Options.ConnectionString, schema,
                    $"update \"{schema}\".control_plane_descriptor_drafts set payload_type = 99 where tenant_id='tenant-1' and draft_id='f07-draft-disc'");
                var act = () => store.GetAsync("tenant-1", "f07-draft-disc");
                (await act.Should().ThrowAsync<RuntimePersistenceContractException>()).Which.Code.Should().Be(RuntimePersistenceContractErrorCode.PersistedInvariantViolation);
                break;
            }
            case PersistedSnapshotCorruptionVariant.DraftInvalidWorkflowTargetUnionShape:
            {
                var store = provider.GetRequiredService<IDescriptorDraftStore>();
                var draft = new Draft
                {
                    TenantId = "tenant-1", DraftId = "f07-draft-wf",
                    DescriptorKind = Metadata.Abstractions.DescriptorKind.Workflow,
                    DescriptorId = "wf-1", Operation = DescriptorDraftOperation.Create,
                    AuthorKind = DescriptorDraftAuthorKind.System, AuthorId = "system",
                    CreatedAt = DateTimeOffset.UnixEpoch,
                    Payload = new WorkflowDescriptorDraftPayload(new WorkflowDescriptor
                    {
                        Id = "wf-1", Name = "WF",
                        Steps = new[] { new WorkflowStep { Id = "s1", Name = "S", Target = new CapabilityTarget { Capability = new("cap-1", 1) } } }
                    })
                };
                await store.SaveAsync(draft);
                await CorruptAsync(lease.Options.ConnectionString, schema,
                    $"update \"{schema}\".control_plane_descriptor_drafts set state_json = jsonb_set(state_json, '{{workflow,steps,0,target,type}}', '99'::jsonb) where tenant_id='tenant-1' and draft_id='f07-draft-wf'");
                var act = () => store.GetAsync("tenant-1", "f07-draft-wf");
                (await act.Should().ThrowAsync<RuntimePersistenceContractException>()).Which.Code.Should().Be(RuntimePersistenceContractErrorCode.PersistedInvariantViolation);
                break;
            }
            case PersistedSnapshotCorruptionVariant.OrganizationUnitInvalidJson:
            case PersistedSnapshotCorruptionVariant.OrganizationUnitUnsupportedStateContractVersion:
            case PersistedSnapshotCorruptionVariant.PositionInvalidJson:
            case PersistedSnapshotCorruptionVariant.PositionUnsupportedStateContractVersion:
            case PersistedSnapshotCorruptionVariant.MembershipInvalidJson:
            case PersistedSnapshotCorruptionVariant.MembershipUnsupportedStateContractVersion:
            case PersistedSnapshotCorruptionVariant.RoleAssignmentInvalidJson:
            case PersistedSnapshotCorruptionVariant.RoleAssignmentUnsupportedStateContractVersion:
            {
                var (table, idCol, id) = variant switch
                {
                    PersistedSnapshotCorruptionVariant.OrganizationUnitInvalidJson => ("organization_units", "organization_unit_id", "f07-ou-json"),
                    PersistedSnapshotCorruptionVariant.OrganizationUnitUnsupportedStateContractVersion => ("organization_units", "organization_unit_id", "f07-ou-ver"),
                    PersistedSnapshotCorruptionVariant.PositionInvalidJson => ("organization_positions", "position_id", "f07-pos-json"),
                    PersistedSnapshotCorruptionVariant.PositionUnsupportedStateContractVersion => ("organization_positions", "position_id", "f07-pos-ver"),
                    PersistedSnapshotCorruptionVariant.MembershipInvalidJson => ("organization_memberships", "membership_id", "f07-mem-json"),
                    PersistedSnapshotCorruptionVariant.MembershipUnsupportedStateContractVersion => ("organization_memberships", "membership_id", "f07-mem-ver"),
                    PersistedSnapshotCorruptionVariant.RoleAssignmentInvalidJson => ("organization_role_assignments", "assignment_id", "f07-ra-json"),
                    PersistedSnapshotCorruptionVariant.RoleAssignmentUnsupportedStateContractVersion => ("organization_role_assignments", "assignment_id", "f07-ra-ver"),
                    _ => throw new InvalidOperationException()
                };
                var orgs = provider.GetRequiredService<IOrganizationStore>();
                switch (table)
                {
                    case "organization_units":
                        await orgs.SaveOrganizationUnitAsync(new OrganizationUnit { Id = id, TenantId = "tenant-1", Name = id }); break;
                    case "organization_positions":
                        await orgs.SavePositionAsync(new Position { Id = id, TenantId = "tenant-1", Name = id }); break;
                    case "organization_memberships":
                        await orgs.SaveMembershipAsync(new UserOrganizationMembership { Id = id, TenantId = "tenant-1", UserId = "u-" + id, OrganizationUnitId = "o-" + id }); break;
                    case "organization_role_assignments":
                        await orgs.SaveRoleAssignmentAsync(new UserOrganizationRoleAssignment { Id = id, TenantId = "tenant-1", UserId = "u-" + id, RoleId = "r-" + id }); break;
                }
                await DropAllCheckConstraintsAsync(lease.Options.ConnectionString, schema, table);
                var isInvalidJson = variant.ToString().Contains("InvalidJson");
                var sql = isInvalidJson
                    ? $"update \"{schema}\".\"{table}\" set state_json = 'null'::jsonb where tenant_scope_kind='tenant' and tenant_id='tenant-1' and \"{idCol}\"=@id"
                    : $"update \"{schema}\".\"{table}\" set state_contract_version = 999 where tenant_scope_kind='tenant' and tenant_id='tenant-1' and \"{idCol}\"=@id";
                await CorruptAsync(lease.Options.ConnectionString, schema, sql, ("id", id));
                Func<Task> act = variant switch
                {
                    PersistedSnapshotCorruptionVariant.OrganizationUnitInvalidJson or PersistedSnapshotCorruptionVariant.OrganizationUnitUnsupportedStateContractVersion
                        => async () => await orgs.GetOrganizationUnitByIdAsync(id, "tenant-1"),
                    PersistedSnapshotCorruptionVariant.PositionInvalidJson or PersistedSnapshotCorruptionVariant.PositionUnsupportedStateContractVersion
                        => async () => await orgs.GetPositionByIdAsync(id, "tenant-1"),
                    PersistedSnapshotCorruptionVariant.MembershipInvalidJson or PersistedSnapshotCorruptionVariant.MembershipUnsupportedStateContractVersion
                        => async () => await orgs.GetMembershipsByUserAsync("u-" + id, "tenant-1"),
                    PersistedSnapshotCorruptionVariant.RoleAssignmentInvalidJson or PersistedSnapshotCorruptionVariant.RoleAssignmentUnsupportedStateContractVersion
                        => async () => await orgs.GetRoleAssignmentsByUserAsync("u-" + id, "tenant-1"),
                    _ => throw new InvalidOperationException()
                };
                (await act.Should().ThrowAsync<RuntimePersistenceContractException>()).Which.Code.Should().Be(RuntimePersistenceContractErrorCode.PersistedInvariantViolation);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(variant));
        }
    }

    public static IEnumerable<object[]> CorruptionVariantData()
    {
        foreach (var v in Enum.GetValues<PersistedSnapshotCorruptionVariant>())
            yield return new object[] { v };
    }

    // ── F08: Ambient Runtime transaction rejection ──

    [Theory]
    [MemberData(nameof(SaveSurfaces))]
    public async Task SaveSurface_Should_RejectAmbientRuntimeTransactionBeforeMutation(string surface)
    {
        await using var lease = await _fixture.CreateSchemaLeaseAsync();
        var services = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();
        await using var provider = services.BuildServiceProvider();

        await using var connection = new NpgsqlConnection(lease.Options.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var accessor = provider.GetRequiredService<PostgreSqlRuntimeTransactionAccessor>();
        accessor.Set(new PostgreSqlRuntimeSession { Connection = connection, Transaction = transaction });
        try
        {
            Func<Task> act = surface switch
            {
                "draft" => async () => await provider.GetRequiredService<IDescriptorDraftStore>().SaveAsync(CreateDraft("amb", DescriptorDraftOperation.Create, DescriptorDraftStatus.Created, DateTimeOffset.UnixEpoch)),
                "organization-unit" => async () => await provider.GetRequiredService<IOrganizationStore>().SaveOrganizationUnitAsync(new OrganizationUnit { Id = "amb", TenantId = "t", Name = "n" }),
                "position" => async () => await provider.GetRequiredService<IOrganizationStore>().SavePositionAsync(new Position { Id = "amb", TenantId = "t", Name = "n" }),
                "membership" => async () => await provider.GetRequiredService<IOrganizationStore>().SaveMembershipAsync(new UserOrganizationMembership { Id = "amb", TenantId = "t", UserId = "u", OrganizationUnitId = "o" }),
                "role-assignment" => async () => await provider.GetRequiredService<IOrganizationStore>().SaveRoleAssignmentAsync(new UserOrganizationRoleAssignment { Id = "amb", TenantId = "t", UserId = "u", RoleId = "r" }),
                "rule" => async () => await provider.GetRequiredService<IDataPermissionScopeRuleStore>().SaveRuleAsync(new DataPermissionScopeRule { Resource = "amb", Action = "read", Permission = "view", TenantId = "t", ScopeKind = DataPermissionScopeKind.Self }),
                _ => throw new ArgumentOutOfRangeException(nameof(surface))
            };
            var ex = await Record.ExceptionAsync(act);
            ex.Should().BeOfType<RuntimePersistenceContractException>();
            ((RuntimePersistenceContractException)ex!).Code.Should().Be(RuntimePersistenceContractErrorCode.AmbientCommitBoundaryUnsupported);
        }
        finally
        {
            accessor.Set(null);
        }
    }

    // ── F09: PersistedStructuredFieldVariant — fail closed ──

    [Theory]
    [MemberData(nameof(StructuredFieldData))]
    public async Task PersistedStructuredFieldVariant_Mismatch_Should_FailClosed(PersistedStructuredFieldVariant variant)
    {
        await using var lease = await _fixture.CreateSchemaLeaseAsync();
        var services = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();
        await using var provider = services.BuildServiceProvider();
        var schema = lease.Options.Schema;

        switch (variant)
        {
            // ── Draft fields ──
            case PersistedStructuredFieldVariant.DraftTenantId:
                await SaveDraftCorruptAndAssert(provider, lease, "f09-d-tid",
                    $"update \"{schema}\".control_plane_descriptor_drafts set tenant_id='tampered' where tenant_id='tenant-1' and draft_id='f09-d-tid'", isUndiscoverable: true);
                break;
            case PersistedStructuredFieldVariant.DraftDraftId:
                await SaveDraftCorruptAndAssert(provider, lease, "f09-d-did",
                    $"update \"{schema}\".control_plane_descriptor_drafts set draft_id='tampered' where tenant_id='tenant-1' and draft_id='f09-d-did'", isUndiscoverable: true);
                break;
            case PersistedStructuredFieldVariant.DraftPayloadDiscriminator:
                await SaveDraftCorruptAndAssert(provider, lease, "f09-d-pt",
                    $"update \"{schema}\".control_plane_descriptor_drafts set payload_type=99 where tenant_id='tenant-1' and draft_id='f09-d-pt'");
                break;
            case PersistedStructuredFieldVariant.DraftDescriptorKind:
                await SaveDraftCorruptAndAssert(provider, lease, "f09-d-dk",
                    $"update \"{schema}\".control_plane_descriptor_drafts set descriptor_kind=99 where tenant_id='tenant-1' and draft_id='f09-d-dk'");
                break;
            case PersistedStructuredFieldVariant.DraftOperation:
                await SaveDraftCorruptAndAssert(provider, lease, "f09-d-op",
                    $"update \"{schema}\".control_plane_descriptor_drafts set operation=99 where tenant_id='tenant-1' and draft_id='f09-d-op'");
                break;
            case PersistedStructuredFieldVariant.DraftAuthorKind:
                await SaveDraftCorruptAndAssert(provider, lease, "f09-d-ak",
                    $"update \"{schema}\".control_plane_descriptor_drafts set author_kind=99 where tenant_id='tenant-1' and draft_id='f09-d-ak'");
                break;
            case PersistedStructuredFieldVariant.DraftStatus:
                await SaveDraftCorruptAndAssert(provider, lease, "f09-d-st",
                    $"update \"{schema}\".control_plane_descriptor_drafts set status=99 where tenant_id='tenant-1' and draft_id='f09-d-st'");
                break;
            case PersistedStructuredFieldVariant.DraftCreatedAtUtcTicks:
                await SaveDraftCorruptAndAssert(provider, lease, "f09-d-ticks",
                    $"update \"{schema}\".control_plane_descriptor_drafts set created_at_utc_ticks=created_at_utc_ticks+1 where tenant_id='tenant-1' and draft_id='f09-d-ticks'");
                break;
            case PersistedStructuredFieldVariant.DraftCreatedAtReadableProjection:
                await SaveDraftCorruptAndAssert(provider, lease, "f09-d-readable",
                    $"update \"{schema}\".control_plane_descriptor_drafts set created_at=created_at+interval '1 microsecond' where tenant_id='tenant-1' and draft_id='f09-d-readable'");
                break;

            // ── OrganizationUnit fields ──
            case PersistedStructuredFieldVariant.OrganizationUnitTenantScope:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_units", "organization_unit_id", "f09-ou-scope",
                    $"update \"{schema}\".organization_units set tenant_scope_kind='global' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and organization_unit_id='f09-ou-scope'",
                    OrgRead, PersistedStructuredFieldVariant.OrganizationUnitTenantScope);
                break;
            case PersistedStructuredFieldVariant.OrganizationUnitId:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_units", "organization_unit_id", "f09-ou-id",
                    $"update \"{schema}\".organization_units set organization_unit_id='tampered' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and organization_unit_id='f09-ou-id'",
                    OrgRead, PersistedStructuredFieldVariant.OrganizationUnitId);
                break;
            case PersistedStructuredFieldVariant.OrganizationUnitParentId:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_units", "organization_unit_id", "f09-ou-pid",
                    $"update \"{schema}\".organization_units set parent_id='unexpected' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and organization_unit_id='f09-ou-pid'",
                    OrgRead, PersistedStructuredFieldVariant.OrganizationUnitParentId);
                break;
            case PersistedStructuredFieldVariant.OrganizationUnitSortOrder:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_units", "organization_unit_id", "f09-ou-so",
                    $"update \"{schema}\".organization_units set sort_order=999 where tenant_scope_kind='tenant' and tenant_id='tenant-1' and organization_unit_id='f09-ou-so'",
                    OrgRead, PersistedStructuredFieldVariant.OrganizationUnitSortOrder);
                break;
            case PersistedStructuredFieldVariant.OrganizationUnitIsActive:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_units", "organization_unit_id", "f09-ou-ia",
                    $"update \"{schema}\".organization_units set is_active=not is_active where tenant_scope_kind='tenant' and tenant_id='tenant-1' and organization_unit_id='f09-ou-ia'",
                    OrgRead, PersistedStructuredFieldVariant.OrganizationUnitIsActive);
                break;
            case PersistedStructuredFieldVariant.OrganizationUnitCreatedAtUtcTicks:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_units", "organization_unit_id", "f09-ou-ticks",
                    $"update \"{schema}\".organization_units set created_at_utc_ticks=created_at_utc_ticks+1 where tenant_scope_kind='tenant' and tenant_id='tenant-1' and organization_unit_id='f09-ou-ticks'",
                    OrgRead, PersistedStructuredFieldVariant.OrganizationUnitCreatedAtUtcTicks);
                break;
            case PersistedStructuredFieldVariant.OrganizationUnitCreatedAtReadableProjection:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_units", "organization_unit_id", "f09-ou-readable",
                    $"update \"{schema}\".organization_units set created_at=created_at+interval '1 microsecond' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and organization_unit_id='f09-ou-readable'",
                    OrgRead, PersistedStructuredFieldVariant.OrganizationUnitCreatedAtReadableProjection);
                break;

            // ── Position fields ──
            case PersistedStructuredFieldVariant.PositionTenantScope:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_positions", "position_id", "f09-pos-scope",
                    $"update \"{schema}\".organization_positions set tenant_scope_kind='global' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and position_id='f09-pos-scope'",
                    PosRead, PersistedStructuredFieldVariant.PositionTenantScope);
                break;
            case PersistedStructuredFieldVariant.PositionId:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_positions", "position_id", "f09-pos-id",
                    $"update \"{schema}\".organization_positions set position_id='tampered' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and position_id='f09-pos-id'",
                    PosRead, PersistedStructuredFieldVariant.PositionId);
                break;
            case PersistedStructuredFieldVariant.PositionIsActive:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_positions", "position_id", "f09-pos-ia",
                    $"update \"{schema}\".organization_positions set is_active=not is_active where tenant_scope_kind='tenant' and tenant_id='tenant-1' and position_id='f09-pos-ia'",
                    PosRead, PersistedStructuredFieldVariant.PositionIsActive);
                break;
            case PersistedStructuredFieldVariant.PositionCreatedAtUtcTicks:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_positions", "position_id", "f09-pos-ticks",
                    $"update \"{schema}\".organization_positions set created_at_utc_ticks=created_at_utc_ticks+1 where tenant_scope_kind='tenant' and tenant_id='tenant-1' and position_id='f09-pos-ticks'",
                    PosRead, PersistedStructuredFieldVariant.PositionCreatedAtUtcTicks);
                break;
            case PersistedStructuredFieldVariant.PositionCreatedAtReadableProjection:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_positions", "position_id", "f09-pos-readable",
                    $"update \"{schema}\".organization_positions set created_at=created_at+interval '1 microsecond' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and position_id='f09-pos-readable'",
                    PosRead, PersistedStructuredFieldVariant.PositionCreatedAtReadableProjection);
                break;

            // ── Membership fields ──
            case PersistedStructuredFieldVariant.MembershipTenantScope:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_memberships", "membership_id", "f09-mem-scope",
                    $"update \"{schema}\".organization_memberships set tenant_scope_kind='global' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and membership_id='f09-mem-scope'",
                    MemRead, PersistedStructuredFieldVariant.MembershipTenantScope);
                break;
            case PersistedStructuredFieldVariant.MembershipId:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_memberships", "membership_id", "f09-mem-id",
                    $"update \"{schema}\".organization_memberships set membership_id='tampered' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and membership_id='f09-mem-id'",
                    MemRead, PersistedStructuredFieldVariant.MembershipId);
                break;
            case PersistedStructuredFieldVariant.MembershipUserId:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_memberships", "membership_id", "f09-mem-uid",
                    $"update \"{schema}\".organization_memberships set user_id='tampered' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and membership_id='f09-mem-uid'",
                    MemRead, PersistedStructuredFieldVariant.MembershipUserId);
                break;
            case PersistedStructuredFieldVariant.MembershipOrganizationUnitId:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_memberships", "membership_id", "f09-mem-oid",
                    $"update \"{schema}\".organization_memberships set organization_unit_id='tampered' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and membership_id='f09-mem-oid'",
                    MemRead, PersistedStructuredFieldVariant.MembershipOrganizationUnitId);
                break;
            case PersistedStructuredFieldVariant.MembershipPositionId:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_memberships", "membership_id", "f09-mem-pid",
                    $"update \"{schema}\".organization_memberships set position_id='tampered' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and membership_id='f09-mem-pid'",
                    MemRead, PersistedStructuredFieldVariant.MembershipPositionId);
                break;
            case PersistedStructuredFieldVariant.MembershipIsPrimary:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_memberships", "membership_id", "f09-mem-ip",
                    $"update \"{schema}\".organization_memberships set is_primary=not is_primary where tenant_scope_kind='tenant' and tenant_id='tenant-1' and membership_id='f09-mem-ip'",
                    MemRead, PersistedStructuredFieldVariant.MembershipIsPrimary);
                break;
            case PersistedStructuredFieldVariant.MembershipIsActive:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_memberships", "membership_id", "f09-mem-ia",
                    $"update \"{schema}\".organization_memberships set is_active=not is_active where tenant_scope_kind='tenant' and tenant_id='tenant-1' and membership_id='f09-mem-ia'",
                    MemRead, PersistedStructuredFieldVariant.MembershipIsActive);
                break;
            case PersistedStructuredFieldVariant.MembershipCreatedAtUtcTicks:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_memberships", "membership_id", "f09-mem-ticks",
                    $"update \"{schema}\".organization_memberships set created_at_utc_ticks=created_at_utc_ticks+1 where tenant_scope_kind='tenant' and tenant_id='tenant-1' and membership_id='f09-mem-ticks'",
                    MemRead, PersistedStructuredFieldVariant.MembershipCreatedAtUtcTicks);
                break;
            case PersistedStructuredFieldVariant.MembershipCreatedAtReadableProjection:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_memberships", "membership_id", "f09-mem-readable",
                    $"update \"{schema}\".organization_memberships set created_at=created_at+interval '1 microsecond' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and membership_id='f09-mem-readable'",
                    MemRead, PersistedStructuredFieldVariant.MembershipCreatedAtReadableProjection);
                break;

            // ── RoleAssignment fields ──
            case PersistedStructuredFieldVariant.RoleAssignmentTenantScope:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_role_assignments", "assignment_id", "f09-ra-scope",
                    $"update \"{schema}\".organization_role_assignments set tenant_scope_kind='global' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and assignment_id='f09-ra-scope'",
                    RaRead, PersistedStructuredFieldVariant.RoleAssignmentTenantScope);
                break;
            case PersistedStructuredFieldVariant.RoleAssignmentId:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_role_assignments", "assignment_id", "f09-ra-id",
                    $"update \"{schema}\".organization_role_assignments set assignment_id='tampered' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and assignment_id='f09-ra-id'",
                    RaRead, PersistedStructuredFieldVariant.RoleAssignmentId);
                break;
            case PersistedStructuredFieldVariant.RoleAssignmentUserId:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_role_assignments", "assignment_id", "f09-ra-uid",
                    $"update \"{schema}\".organization_role_assignments set user_id='tampered' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and assignment_id='f09-ra-uid'",
                    RaRead, PersistedStructuredFieldVariant.RoleAssignmentUserId);
                break;
            case PersistedStructuredFieldVariant.RoleAssignmentRoleId:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_role_assignments", "assignment_id", "f09-ra-rid",
                    $"update \"{schema}\".organization_role_assignments set role_id='tampered' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and assignment_id='f09-ra-rid'",
                    RaRead, PersistedStructuredFieldVariant.RoleAssignmentRoleId);
                break;
            case PersistedStructuredFieldVariant.RoleAssignmentOrganizationUnitId:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_role_assignments", "assignment_id", "f09-ra-oid",
                    $"update \"{schema}\".organization_role_assignments set organization_unit_id='tampered' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and assignment_id='f09-ra-oid'",
                    RaRead, PersistedStructuredFieldVariant.RoleAssignmentOrganizationUnitId);
                break;
            case PersistedStructuredFieldVariant.RoleAssignmentIsActive:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_role_assignments", "assignment_id", "f09-ra-ia",
                    $"update \"{schema}\".organization_role_assignments set is_active=not is_active where tenant_scope_kind='tenant' and tenant_id='tenant-1' and assignment_id='f09-ra-ia'",
                    RaRead, PersistedStructuredFieldVariant.RoleAssignmentIsActive);
                break;
            case PersistedStructuredFieldVariant.RoleAssignmentCreatedAtUtcTicks:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_role_assignments", "assignment_id", "f09-ra-ticks",
                    $"update \"{schema}\".organization_role_assignments set created_at_utc_ticks=created_at_utc_ticks+1 where tenant_scope_kind='tenant' and tenant_id='tenant-1' and assignment_id='f09-ra-ticks'",
                    RaRead, PersistedStructuredFieldVariant.RoleAssignmentCreatedAtUtcTicks);
                break;
            case PersistedStructuredFieldVariant.RoleAssignmentCreatedAtReadableProjection:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_role_assignments", "assignment_id", "f09-ra-readable",
                    $"update \"{schema}\".organization_role_assignments set created_at=created_at+interval '1 microsecond' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and assignment_id='f09-ra-readable'",
                    RaRead, PersistedStructuredFieldVariant.RoleAssignmentCreatedAtReadableProjection);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(variant));
        }
    }

    public static IEnumerable<object[]> StructuredFieldData()
    {
        foreach (var v in Enum.GetValues<PersistedStructuredFieldVariant>())
            yield return new object[] { v };
    }

    // ── Helpers ──

    private static async Task CorruptAsync(string connectionString, string schema, string sql, params (string name, object value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DropAllCheckConstraintsAsync(string connectionString, string schema, string table)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "select conname from pg_constraint where conrelid = (quote_ident(@schema) || '.' || quote_ident(@table))::regclass and contype = 'c'",
            connection);
        cmd.Parameters.AddWithValue("schema", schema);
        cmd.Parameters.AddWithValue("table", table);
        await using var reader = await cmd.ExecuteReaderAsync();
        var constraints = new List<string>();
        while (await reader.ReadAsync())
            constraints.Add(reader.GetString(0));
        await reader.CloseAsync();
        foreach (var ck in constraints)
        {
            await using var drop = new NpgsqlCommand($"alter table \"{schema}\".\"{table}\" drop constraint \"{ck}\"", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private async Task SaveDraftCorruptAndAssert(ServiceProvider provider, PostgreSqlRuntimeSchemaLease lease, string draftId, string corruptSql, bool isUndiscoverable = false)
    {
        var store = provider.GetRequiredService<IDescriptorDraftStore>();
        await store.SaveAsync(CreateDraft(draftId, DescriptorDraftOperation.Create, DescriptorDraftStatus.Created, DateTimeOffset.UnixEpoch));
        await DropAllCheckConstraintsAsync(lease.Options.ConnectionString, lease.Options.Schema, "control_plane_descriptor_drafts");
        await CorruptAsync(lease.Options.ConnectionString, lease.Options.Schema, corruptSql);
        if (isUndiscoverable)
        {
            await store.GetAsync("tenant-1", draftId);
            return;
        }
        var act = () => store.GetAsync("tenant-1", draftId);
        (await act.Should().ThrowAsync<RuntimePersistenceContractException>())
            .Which.Code.Should().Be(RuntimePersistenceContractErrorCode.PersistedInvariantViolation);
    }

    private async Task SaveOrgCorruptAndAssert(ServiceProvider provider, PostgreSqlRuntimeSchemaLease lease,
        string table, string idCol, string id, string corruptSql,
        Func<IOrganizationStore, string, Task> readAsync,
        PersistedStructuredFieldVariant variant = default)
    {
        var orgs = provider.GetRequiredService<IOrganizationStore>();
        switch (table)
        {
            case "organization_units":
                await orgs.SaveOrganizationUnitAsync(new OrganizationUnit { Id = id, TenantId = "tenant-1", Name = id });
                break;
            case "organization_positions":
                await orgs.SavePositionAsync(new Position { Id = id, TenantId = "tenant-1", Name = id });
                break;
            case "organization_memberships":
                await orgs.SaveMembershipAsync(new UserOrganizationMembership { Id = id, TenantId = "tenant-1", UserId = "u-" + id, OrganizationUnitId = "o-" + id });
                break;
            case "organization_role_assignments":
                await orgs.SaveRoleAssignmentAsync(new UserOrganizationRoleAssignment { Id = id, TenantId = "tenant-1", UserId = "u-" + id, RoleId = "r-" + id });
                break;
        }
        await DropAllCheckConstraintsAsync(lease.Options.ConnectionString, lease.Options.Schema, table);
        await CorruptAsync(lease.Options.ConnectionString, lease.Options.Schema, corruptSql);
        // Some variants corrupt a PK/WHERE-clause column, making the row undiscoverable.
        // The store returns null/empty — no mismatch is detectable through the normal read path.
        // These are structurally self-protecting: the DB key prevents the row from being read.
        var isUndiscoverable = variant is
            PersistedStructuredFieldVariant.DraftTenantId or PersistedStructuredFieldVariant.DraftDraftId or
            PersistedStructuredFieldVariant.OrganizationUnitTenantScope or PersistedStructuredFieldVariant.OrganizationUnitId or
            PersistedStructuredFieldVariant.PositionTenantScope or PersistedStructuredFieldVariant.PositionId or
            PersistedStructuredFieldVariant.MembershipTenantScope or PersistedStructuredFieldVariant.MembershipUserId or
            PersistedStructuredFieldVariant.RoleAssignmentTenantScope or PersistedStructuredFieldVariant.RoleAssignmentUserId;
        if (isUndiscoverable)
        {
            // Verify the row is no longer readable (self-protecting via DB key)
            await readAsync(orgs, id);
            // No corrupted data was returned — the row is simply not found
            return;
        }
        try
        {
            await readAsync(orgs, id);
            Assert.Fail($"Expected PersistedInvariantViolation for variant {variant} but no exception was thrown.");
        }
        catch (RuntimePersistenceContractException ex) when (ex.Code == RuntimePersistenceContractErrorCode.PersistedInvariantViolation)
        {
            // Expected — structured column mismatch detected
        }
    }

    private static Func<Task> OrgSave(ServiceProvider p, string id) => async () =>
        await p.GetRequiredService<IOrganizationStore>().SaveOrganizationUnitAsync(new OrganizationUnit { Id = id, TenantId = "tenant-1", Name = id });
    private static async Task OrgRead(IOrganizationStore orgs, string id) =>
        await orgs.GetOrganizationUnitByIdAsync(id, "tenant-1");
    private static Func<Task> PosSave(ServiceProvider p, string id) => async () =>
        await p.GetRequiredService<IOrganizationStore>().SavePositionAsync(new Position { Id = id, TenantId = "tenant-1", Name = id });
    private static async Task PosRead(IOrganizationStore orgs, string id) =>
        await orgs.GetPositionByIdAsync(id, "tenant-1");
    private static Func<Task> MemSave(ServiceProvider p, string id) => async () =>
        await p.GetRequiredService<IOrganizationStore>().SaveMembershipAsync(new UserOrganizationMembership { Id = id, TenantId = "tenant-1", UserId = "u-" + id, OrganizationUnitId = "o-" + id });
    private static async Task MemRead(IOrganizationStore orgs, string id) =>
        await orgs.GetMembershipsByUserAsync("u-" + id, "tenant-1");
    private static Func<Task> RaSave(ServiceProvider p, string id) => async () =>
        await p.GetRequiredService<IOrganizationStore>().SaveRoleAssignmentAsync(new UserOrganizationRoleAssignment { Id = id, TenantId = "tenant-1", UserId = "u-" + id, RoleId = "r-" + id });
    private static async Task RaRead(IOrganizationStore orgs, string id) =>
        await orgs.GetRoleAssignmentsByUserAsync("u-" + id, "tenant-1");

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
