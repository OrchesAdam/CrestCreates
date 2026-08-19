using CrestCreates.DescriptorDraft;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Organization;
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
    [InlineData("unit")]
    [InlineData("position")]
    [InlineData("membership-by-user")]
    [InlineData("membership-by-unit")]
    [InlineData("role-by-user")]
    public async Task Organization_queries_Should_RejectWhitespaceTenantBeforeDatabaseAccess(string surface)
    {
        await using var lease = await _fixture.CreateSchemaLeaseAsync();
        var services = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IOrganizationStore>();

        Func<Task> act = surface switch
        {
            "unit" => () => store.GetOrganizationUnitByIdAsync("unit", "   "),
            "position" => () => store.GetPositionByIdAsync("position", "   "),
            "membership-by-user" => () => store.GetMembershipsByUserAsync("user", "   "),
            "membership-by-unit" => () => store.GetMembershipsByOrganizationUnitAsync("unit", "   "),
            "role-by-user" => () => store.GetRoleAssignmentsByUserAsync("user", "   "),
            _ => throw new ArgumentOutOfRangeException(nameof(surface), surface, null)
        };

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rule_lookup_Should_RejectWhitespaceTenantBeforeDatabaseAccess(string tenantId)
    {
        await using var lease = await _fixture.CreateSchemaLeaseAsync();
        var services = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDataPermissionScopeRuleStore>();

        await ((Func<Task>)(() => store.GetScopeKindAsync("resource", "read", "view", tenantId)))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Organization_unfiltered_read_Should_RejectCorruptedPersistedIdentity()
    {
        await using var lease = await _fixture.CreateSchemaLeaseAsync();
        var services = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IOrganizationStore>();
        await store.SaveOrganizationUnitAsync(new OrganizationUnit { Id = "corrupt-identity", TenantId = "tenant-1", Name = "Unit" });

        await DropAllCheckConstraintsAsync(lease.Options.ConnectionString, lease.Options.Schema, "organization_units");
        await CorruptAsync(lease.Options.ConnectionString, lease.Options.Schema,
            $"update \"{lease.Options.Schema}\".organization_units set tenant_id='   ', state_json=jsonb_set(state_json, '{{tenantId}}', '\"   \"'::jsonb) where organization_unit_id='corrupt-identity'");

        Func<Task> read = () => store.GetOrganizationUnitsAsync();
        await read.Should().ThrowAsync<RuntimePersistenceContractException>();
    }

    [Fact]
    public async Task Organization_shared_contract_cases_Should_Run_All_Frozen_Surfaces()
    {
        await using var lease = await _fixture.CreateSchemaLeaseAsync();
        var services = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IOrganizationStore>();

        foreach (var surface in Enum.GetValues<OrganizationIdentitySurface>())
        {
            ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O01, "Organization", surface.ToString(), EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
            await OrganizationStoreContractCases.RunIdentityAsync(store, surface, $"pg-o01-{surface}");
            ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O02, "Organization", surface.ToString(), EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
            await OrganizationStoreContractCases.RunIdentityAsync(store, surface, $"pg-o02-{surface}");
        }

        foreach (var surface in Enum.GetValues<OrganizationQuerySurface>())
        {
            ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O03, "Organization", surface.ToString(), EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
            await OrganizationStoreContractCases.RunExplicitQueryAsync(store, surface, $"pg-o03-{surface}");
            ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O04, "Organization", surface.ToString(), EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
            await OrganizationStoreContractCases.RunUnfilteredQueryAsync(store, surface, $"pg-o04-{surface}");
        }

        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O05, "Organization", "OrganizationUnit", EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
        await store.SaveOrganizationUnitAsync(new OrganizationUnit { Id = "pg-o05-z", TenantId = "tenant", SortOrder = 2 });
        await store.SaveOrganizationUnitAsync(new OrganizationUnit { Id = "pg-o05-a", TenantId = "tenant", SortOrder = 1 });
        (await store.GetOrganizationUnitsAsync("tenant")).Select(x => x.Id).Should().Equal("pg-o05-a", "pg-o05-z");

        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O06, "Organization", "Position", EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
        await store.SavePositionAsync(new Position { Id = "pg-o06-z", TenantId = "tenant" });
        await store.SavePositionAsync(new Position { Id = "pg-o06-a", TenantId = "tenant" });
        (await store.GetPositionsAsync("tenant")).Select(x => x.Id).Should().Equal("pg-o06-a", "pg-o06-z");

        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O07, "Organization", "MembershipByUser", EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
        await store.SaveMembershipAsync(new UserOrganizationMembership { Id = "pg-o07-z", TenantId = "tenant", UserId = "user", OrganizationUnitId = "unit", CreatedAt = DateTimeOffset.UnixEpoch.AddTicks(2) });
        await store.SaveMembershipAsync(new UserOrganizationMembership { Id = "pg-o07-a", TenantId = "tenant", UserId = "user", OrganizationUnitId = "unit", CreatedAt = DateTimeOffset.UnixEpoch.AddTicks(1) });
        (await store.GetMembershipsByUserAsync("user", "tenant")).Select(x => x.Id).Should().Equal("pg-o07-a", "pg-o07-z");

        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O08, "Organization", "MembershipByUnit", EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
        await store.SaveMembershipAsync(new UserOrganizationMembership { Id = "pg-o08-z", TenantId = "tenant", UserId = "user-o08", OrganizationUnitId = "unit-o08", CreatedAt = DateTimeOffset.UnixEpoch.AddTicks(2) });
        await store.SaveMembershipAsync(new UserOrganizationMembership { Id = "pg-o08-a", TenantId = "tenant", UserId = "user-o08", OrganizationUnitId = "unit-o08", CreatedAt = DateTimeOffset.UnixEpoch.AddTicks(1) });
        (await store.GetMembershipsByOrganizationUnitAsync("unit-o08", "tenant")).Select(x => x.Id).Should().Equal("pg-o08-a", "pg-o08-z");

        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O09, "Organization", "RoleAssignment", EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
        await store.SaveRoleAssignmentAsync(new UserOrganizationRoleAssignment { Id = "pg-o09-z", TenantId = "tenant", UserId = "user", RoleId = "role", CreatedAt = DateTimeOffset.UnixEpoch.AddTicks(2) });
        await store.SaveRoleAssignmentAsync(new UserOrganizationRoleAssignment { Id = "pg-o09-a", TenantId = "tenant", UserId = "user", RoleId = "role", CreatedAt = DateTimeOffset.UnixEpoch.AddTicks(1) });
        (await store.GetRoleAssignmentsByUserAsync("user", "tenant")).Select(x => x.Id).Should().Equal("pg-o09-a", "pg-o09-z");

        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O10, "Organization", "Membership", EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
        await store.SaveMembershipAsync(new UserOrganizationMembership { Id = "same", TenantId = null, UserId = "primary", OrganizationUnitId = "global", IsPrimary = true });
        await store.SaveMembershipAsync(new UserOrganizationMembership { Id = "same", TenantId = "tenant", UserId = "primary", OrganizationUnitId = "tenant", IsPrimary = true });
        (await new DefaultOrganizationIdentityService(store).GetContextAsync("primary"))!.PrimaryOrganizationUnitId.Should().Be("global");

        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O13, "Organization", "OrganizationUnit", EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
        await store.SaveOrganizationUnitAsync(new OrganizationUnit { Id = "pg-o13", TenantId = "tenant", ParentId = "missing" });
        (await store.GetOrganizationUnitByIdAsync("pg-o13", "tenant")).Should().NotBeNull();

        foreach (var variant in Enum.GetValues<MissingReferenceVariant>())
        {
            ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O14, "Organization", variant.ToString(), EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
            await store.SaveMembershipAsync(new UserOrganizationMembership
            {
                Id = $"pg-o14-m-{variant}", TenantId = "tenant", UserId = "user", OrganizationUnitId = variant == MissingReferenceVariant.MembershipOrganizationUnit ? "missing" : "unit",
                PositionId = variant == MissingReferenceVariant.MembershipPosition ? "missing-position" : null
            });
            await store.SaveRoleAssignmentAsync(new UserOrganizationRoleAssignment
            {
                Id = $"pg-o14-r-{variant}", TenantId = "tenant", UserId = "user", RoleId = variant == MissingReferenceVariant.RoleAssignmentRole ? "missing-role" : "role",
                OrganizationUnitId = variant == MissingReferenceVariant.RoleAssignmentOrganizationUnit ? "missing-unit" : null
            });
        }

        foreach (var variant in Enum.GetValues<ScopedKeyCollisionVariant>())
        {
            ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O19, "Organization", variant.ToString(), EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
            await OrganizationStoreContractCases.RunScopedKeyAsync(store, new DefaultOrganizationHierarchyService(store), $"pg-o19-{variant}");
        }
        foreach (var surface in Enum.GetValues<OrganizationEntitySurface>())
        {
            ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O20, "Organization", surface.ToString(), EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
            await OrganizationStoreContractCases.RunEntitySnapshotAsync(store, surface, $"pg-o20-{surface}");
        }
        foreach (var surface in Enum.GetValues<OrganizationReadSurface>())
        {
            ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O21, "Organization", surface.ToString(), EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
            await OrganizationStoreContractCases.RunDetachedReadAsync(store, surface, $"pg-o21-{surface}");
        }
        foreach (var variant in Enum.GetValues<OrganizationCreatedAtVariant>())
        {
            ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O22, "Organization", variant.ToString(), EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
            await OrganizationStoreContractCases.RunCreatedAtAsync(store, variant, $"pg-o22-{variant}");
        }
    }

    [Fact]
    public async Task Rule_shared_contract_cases_Should_Run_All_Frozen_Surfaces()
    {
        await using var lease = await _fixture.CreateSchemaLeaseAsync();
        var services = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDataPermissionScopeRuleStore>();

        async Task Save(string resource, string? action, string? permission, string? tenant, DataPermissionScopeKind scope)
            => await store.SaveRuleAsync(new DataPermissionScopeRule
            {
                Resource = resource, Action = action, Permission = permission, TenantId = tenant, ScopeKind = scope
            });

        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.P01, "Rule", "Rule", EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
        await DataPermissionScopeRuleStoreContractCases.ExactTenantAsync(
            store, "p01", "tenant", DataPermissionScopeKind.Self);

        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.P02, "Rule", "Rule", EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
        await Save("p02", "read", null, "tenant", DataPermissionScopeKind.OwnOrganization);
        (await store.GetScopeKindAsync("p02", "read", "other", "tenant")).Should().Be(DataPermissionScopeKind.OwnOrganization);

        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.P03, "Rule", "Rule", EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
        await Save("p03", null, null, "tenant", DataPermissionScopeKind.All);
        (await store.GetScopeKindAsync("p03", "write", "view", "tenant")).Should().Be(DataPermissionScopeKind.All);

        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.P04, "Rule", "Rule", EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
        await Save("p04", "read", "view", null, DataPermissionScopeKind.All);
        (await store.GetScopeKindAsync("p04", "read", "view", "tenant")).Should().Be(DataPermissionScopeKind.All);

        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.P05, "Rule", "Rule", EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
        await Save("p05", "read", "view", null, DataPermissionScopeKind.All);
        await Save("p05", "read", null, "tenant", DataPermissionScopeKind.Self);
        (await store.GetScopeKindAsync("p05", "read", "view", "tenant")).Should().Be(DataPermissionScopeKind.Self);

        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.P06, "Rule", "Rule", EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
        await Save("p06", "read", "view", "other-tenant", DataPermissionScopeKind.All);
        (await store.GetScopeKindAsync("p06", "read", "view", "tenant")).Should().BeNull();

        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.P07, "Rule", "Rule", EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
        await Save("p07", "read", "view", "tenant", DataPermissionScopeKind.Self);
        await Save("p07", "read", "view", "tenant", DataPermissionScopeKind.All);
        (await store.GetScopeKindAsync("p07", "read", "view", "tenant")).Should().Be(DataPermissionScopeKind.All);

        foreach (var variant in Enum.GetValues<RuleExactEmptyVariant>())
        {
            var resource = $"p10-{variant}";
            ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.P10, "Rule", variant.ToString(), EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
            switch (variant)
            {
                case RuleExactEmptyVariant.ActionEmpty:
                    await Save(resource, string.Empty, "view", "tenant", DataPermissionScopeKind.Self);
                    await Save(resource, null, null, "tenant", DataPermissionScopeKind.All);
                    (await store.GetScopeKindAsync(resource, string.Empty, "view", "tenant")).Should().Be(DataPermissionScopeKind.Self);
                    break;
                case RuleExactEmptyVariant.PermissionEmpty:
                    await Save(resource, "read", string.Empty, "tenant", DataPermissionScopeKind.Self);
                    await Save(resource, null, null, "tenant", DataPermissionScopeKind.All);
                    (await store.GetScopeKindAsync(resource, "read", string.Empty, "tenant")).Should().Be(DataPermissionScopeKind.Self);
                    break;
                default:
                    await Save(resource, string.Empty, string.Empty, "tenant", DataPermissionScopeKind.Self);
                    await Save(resource, null, null, "tenant", DataPermissionScopeKind.All);
                    (await store.GetScopeKindAsync(resource, string.Empty, string.Empty, "tenant")).Should().Be(DataPermissionScopeKind.Self);
                    break;
            }
        }

        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.P11, "Rule", "Rule", EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
        await Save("p11", null, "view", "tenant", DataPermissionScopeKind.Self);
        (await store.GetScopeKindAsync("p11", "read", "view", "tenant")).Should().BeNull();

        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.P12, "Rule", "Rule", EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
        await Save("p12", null, "view", "tenant", DataPermissionScopeKind.Self);
        (await store.GetScopeKindAsync("p12", null, "view", "tenant")).Should().Be(DataPermissionScopeKind.Self);
    }

    [Theory]
    [MemberData(nameof(PersistedRuleCorruptionData))]
    public async Task PersistedRuleCorruptionVariant_Should_FailClosed(PersistedRuleCorruptionVariant variant)
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.P13, "Rule", variant.ToString(), EvidenceVectorKey.ProviderFailClosed, RequiredRunner.PostgreSql);
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

        await DropConstraintAsync(
            lease.Options.ConnectionString,
            lease.Options.Schema,
            "data_permission_scope_rules",
            RuleConstraintForVariant(variant));
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

    [Theory]
    [MemberData(nameof(PersistedRuleCorruptionData))]
    public async Task PersistedRuleCorruptionVariant_IntactSchema_Should_RejectRawDml(PersistedRuleCorruptionVariant variant)
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.P13, "Rule", variant.ToString(), EvidenceVectorKey.SchemaReject, RequiredRunner.PostgreSql);
        await using var lease = await _fixture.CreateSchemaLeaseAsync();
        var services = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();
        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IDataPermissionScopeRuleStore>().SaveRuleAsync(new DataPermissionScopeRule
        {
            Resource = "schema-rule",
            Action = "read",
            Permission = "view",
            TenantId = "tenant-1",
            ScopeKind = DataPermissionScopeKind.Self
        });

        Func<Task> act = async () =>
        {
            await using var connection = new NpgsqlConnection(lease.Options.ConnectionString);
            await connection.OpenAsync();
            await using var update = new NpgsqlCommand(
                $"update \"{lease.Options.Schema}\".data_permission_scope_rules set {CorruptionSql(variant)} where tenant_scope_kind='tenant' and tenant_id='tenant-1' and resource='schema-rule'",
                connection);
            await update.ExecuteNonQueryAsync();
        };

        await act.Should().ThrowAsync<PostgresException>();
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

    private static string RuleConstraintForVariant(PersistedRuleCorruptionVariant variant)
        => variant switch
        {
            PersistedRuleCorruptionVariant.InvalidTenantScopeKind
                or PersistedRuleCorruptionVariant.TenantScopeTupleMismatch => "ck_data_permission_tenant_scope",
            PersistedRuleCorruptionVariant.InvalidActionMatchKind
                or PersistedRuleCorruptionVariant.ActionWildcardValueMismatch => "ck_data_permission_action_match",
            PersistedRuleCorruptionVariant.InvalidPermissionMatchKind
                or PersistedRuleCorruptionVariant.PermissionWildcardValueMismatch => "ck_data_permission_permission_match",
            PersistedRuleCorruptionVariant.InvalidScopeKind => "ck_data_permission_scope_kind",
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
    [InlineData("descriptor_kind", "ck_cp_draft_descriptor_kind", "descriptorKind")]
    [InlineData("operation", "ck_cp_draft_operation", "operation")]
    [InlineData("author_kind", "ck_cp_draft_author_kind", "authorKind")]
    [InlineData("status", "ck_cp_draft_status", "status")]
    public async Task Draft_read_rejects_doubly_corrupt_undefined_enum(
        string column,
        string constraint,
        string jsonProperty)
    {
        await using var lease = await _fixture.CreateSchemaLeaseAsync();
        var services = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDescriptorDraftStore>();
        var draft = CreateDraft($"draft-double-corrupt-{column}", DescriptorDraftOperation.Create, DescriptorDraftStatus.Created,
            DateTimeOffset.UnixEpoch);
        await store.SaveAsync(draft);
        await DropConstraintAsync(lease.Options.ConnectionString, lease.Options.Schema,
            "control_plane_descriptor_drafts", constraint);

        await using (var connection = new NpgsqlConnection(lease.Options.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                $"update \"{lease.Options.Schema}\".control_plane_descriptor_drafts set {column} = 999, state_json = jsonb_set(state_json, '{{{jsonProperty}}}', '999'::jsonb) where tenant_id = @tenant and draft_id = @draft",
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

    [Theory]
    [InlineData("organization-unit")]
    [InlineData("membership")]
    [InlineData("role-assignment")]
    public async Task OptionalStructuredField_NonNullJsonNullColumn_Should_FailClosed(string surface)
    {
        await using var lease = await _fixture.CreateSchemaLeaseAsync();
        var services = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IOrganizationStore>();
        var table = surface switch
        {
            "organization-unit" => "organization_units",
            "membership" => "organization_memberships",
            "role-assignment" => "organization_role_assignments",
            _ => throw new ArgumentOutOfRangeException(nameof(surface), surface, null)
        };
        var idColumn = surface switch
        {
            "organization-unit" => "organization_unit_id",
            "membership" => "membership_id",
            "role-assignment" => "assignment_id",
            _ => throw new ArgumentOutOfRangeException(nameof(surface), surface, null)
        };
        var id = $"null-column-{surface}";
        string column;
        switch (surface)
        {
            case "organization-unit":
                await store.SaveOrganizationUnitAsync(new OrganizationUnit { Id = id, TenantId = "tenant-1", Name = id, ParentId = "parent" });
                column = "parent_id";
                break;
            case "membership":
                await store.SaveMembershipAsync(new UserOrganizationMembership { Id = id, TenantId = "tenant-1", UserId = id, OrganizationUnitId = "unit", PositionId = "position" });
                column = "position_id";
                break;
            default:
                await store.SaveRoleAssignmentAsync(new UserOrganizationRoleAssignment { Id = id, TenantId = "tenant-1", UserId = id, RoleId = "role", OrganizationUnitId = "unit" });
                column = "organization_unit_id";
                break;
        }

        await CorruptAsync(lease.Options.ConnectionString, lease.Options.Schema,
            $"update \"{lease.Options.Schema}\".{table} set {column}=null where tenant_scope_kind='tenant' and tenant_id='tenant-1' and {idColumn}=@id",
            ("id", id));

        Func<Task> act = surface switch
        {
            "organization-unit" => async () => await store.GetOrganizationUnitByIdAsync(id, "tenant-1"),
            "membership" => async () => await store.GetMembershipsByUserAsync(id, "tenant-1"),
            "role-assignment" => async () => await store.GetRoleAssignmentsByUserAsync(id, "tenant-1"),
            _ => throw new ArgumentOutOfRangeException(nameof(surface), surface, null)
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

    private static string SaveSurfaceName(string surface)
        => surface switch
        {
            "draft" => nameof(SaveSurface.Draft),
            "organization-unit" => nameof(SaveSurface.OrganizationUnit),
            "position" => nameof(SaveSurface.Position),
            "membership" => nameof(SaveSurface.Membership),
            "role-assignment" => nameof(SaveSurface.RoleAssignment),
            "rule" => nameof(SaveSurface.Rule),
            _ => throw new ArgumentOutOfRangeException(nameof(surface), surface, null)
        };

    [Theory]
    [MemberData(nameof(SaveSurfaces))]
    public async Task SaveSurface_ConcurrentBlindSave_Should_ExposeOneCompleteSnapshot(string surface)
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.F01, "Failure", SaveSurfaceName(surface), EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
        await using var lease = await _fixture.CreateSchemaLeaseAsync();
        var services = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();
        await using var provider = services.BuildServiceProvider();
        using var snapshotBarrier = PostgreSqlRuntimeTestHooks.BlockAfterReferenceSnapshotCaptured(2);

        switch (surface)
        {
            case "draft":
            {
                var drafts = provider.GetRequiredService<IDescriptorDraftStore>();
                // AuthorId is init-only; use two separate draft instances with different AuthorId via CreateDraft variants
                var draftA = new Draft
                {
                    TenantId = "tenant-1", DraftId = "concurrent",
                    DescriptorKind = Metadata.Abstractions.DescriptorKind.Schema, DescriptorId = "schema-1",
                    Operation = DescriptorDraftOperation.Create, AuthorKind = DescriptorDraftAuthorKind.System,
                    AuthorId = "author-a", CreatedAt = DateTimeOffset.UnixEpoch, Status = DescriptorDraftStatus.Created,
                    Intent = "intent-a",
                    Payload = new SchemaDescriptorDraftPayload(new SchemaDescriptor { Id = "schema-1", Name = "Schema A" })
                };
                var draftB = new Draft
                {
                    TenantId = "tenant-1", DraftId = "concurrent",
                    DescriptorKind = Metadata.Abstractions.DescriptorKind.Schema, DescriptorId = "schema-1",
                    Operation = DescriptorDraftOperation.Create, AuthorKind = DescriptorDraftAuthorKind.System,
                    AuthorId = "author-b", CreatedAt = DateTimeOffset.UnixEpoch.AddTicks(1), Status = DescriptorDraftStatus.Reviewed,
                    Intent = "intent-b",
                    Payload = new SchemaDescriptorDraftPayload(new SchemaDescriptor { Id = "schema-1", Name = "Schema B" })
                };
                await Task.WhenAll(drafts.SaveAsync(draftA), drafts.SaveAsync(draftB));
                var result = await drafts.GetAsync("tenant-1", "concurrent");
                result.Should().NotBeNull();
                var schema = (SchemaDescriptorDraftPayload)result!.Payload;
                var matchesA = result.AuthorId == "author-a" && result.Intent == "intent-a" && result.Status == DescriptorDraftStatus.Created && schema.Descriptor.Name == "Schema A";
                var matchesB = result.AuthorId == "author-b" && result.Intent == "intent-b" && result.Status == DescriptorDraftStatus.Reviewed && schema.Descriptor.Name == "Schema B";
                (matchesA || matchesB).Should().BeTrue("the row must be one complete submitted snapshot");
                break;
            }
            case "organization-unit":
            {
                var orgs = provider.GetRequiredService<IOrganizationStore>();
                var a = new OrganizationUnit { Id = "unit-c", TenantId = "tenant-1", Name = "A", SortOrder = 1, IsActive = true };
                var b = new OrganizationUnit { Id = "unit-c", TenantId = "tenant-1", Name = "B", SortOrder = 2, IsActive = false };
                await Task.WhenAll(orgs.SaveOrganizationUnitAsync(a), orgs.SaveOrganizationUnitAsync(b));
                var r = await orgs.GetOrganizationUnitByIdAsync("unit-c", "tenant-1");
                r.Should().NotBeNull();
                var matchesA = r!.Name == "A" && r.SortOrder == 1 && r.IsActive;
                var matchesB = r.Name == "B" && r.SortOrder == 2 && !r.IsActive;
                (matchesA || matchesB).Should().BeTrue("the row must be one complete submitted snapshot");
                break;
            }
            case "position":
            {
                var orgs = provider.GetRequiredService<IOrganizationStore>();
                var a = new Position { Id = "pos-c", TenantId = "tenant-1", Name = "PA", IsActive = true };
                var b = new Position { Id = "pos-c", TenantId = "tenant-1", Name = "PB", IsActive = false };
                await Task.WhenAll(orgs.SavePositionAsync(a), orgs.SavePositionAsync(b));
                var r = await orgs.GetPositionByIdAsync("pos-c", "tenant-1");
                r.Should().NotBeNull();
                var matchesA = r!.Name == "PA" && r.IsActive;
                var matchesB = r.Name == "PB" && !r.IsActive;
                (matchesA || matchesB).Should().BeTrue("the row must be one complete submitted snapshot");
                break;
            }
            case "membership":
            {
                var orgs = provider.GetRequiredService<IOrganizationStore>();
                var a = new UserOrganizationMembership { Id = "mem-c", TenantId = "tenant-1", UserId = "u1", OrganizationUnitId = "o1", PositionId = "p1", IsPrimary = true, IsActive = true };
                var b = new UserOrganizationMembership { Id = "mem-c", TenantId = "tenant-1", UserId = "u1", OrganizationUnitId = "o2", PositionId = "p2", IsPrimary = false, IsActive = false };
                await Task.WhenAll(orgs.SaveMembershipAsync(a), orgs.SaveMembershipAsync(b));
                var r = (await orgs.GetMembershipsByUserAsync("u1", "tenant-1")).Single();
                var matchesA = r.OrganizationUnitId == "o1" && r.PositionId == "p1" && r.IsPrimary && r.IsActive;
                var matchesB = r.OrganizationUnitId == "o2" && r.PositionId == "p2" && !r.IsPrimary && !r.IsActive;
                (matchesA || matchesB).Should().BeTrue("the row must be one complete submitted snapshot");
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
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.F02, "Failure", SaveSurfaceName(surface), EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
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
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.F06, "Failure", surface.ToString(), EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
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
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.F07, "Failure", variant.ToString(), EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
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

                await CorruptAsync(lease.Options.ConnectionString, schema,
                    $"update \"{schema}\".control_plane_descriptor_drafts set state_json = jsonb_set(jsonb_set(state_json, '{{workflow,steps,0,target,type}}', '0'::jsonb), '{{workflow,steps,0,target,humanTask}}', '{{\"id\":\"task-1\",\"version\":1}}'::jsonb) where tenant_id='tenant-1' and draft_id='f07-draft-wf'");
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
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.F08, "Failure", SaveSurfaceName(surface), EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
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

    // ── F09: PersistedStructuredFieldVariant — fail closed, both EvidenceVectorKey directions ──

    [Theory]
    [MemberData(nameof(StructuredFieldData))]
    public async Task PersistedStructuredFieldVariant_Mismatch_Should_FailClosed(
        PersistedStructuredFieldVariant variant,
        EvidenceVectorKey key)
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.F09, "Failure", variant.ToString(), key, RequiredRunner.PostgreSql);
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
                    $"update \"{schema}\".control_plane_descriptor_drafts set tenant_id='tampered' where tenant_id='tenant-1' and draft_id='f09-d-tid'",
                    DraftTamperedTenantRead);
                break;
            case PersistedStructuredFieldVariant.DraftDraftId:
                await SaveDraftCorruptAndAssert(provider, lease, "f09-d-did",
                    $"update \"{schema}\".control_plane_descriptor_drafts set draft_id='tampered' where tenant_id='tenant-1' and draft_id='f09-d-did'",
                    DraftListRead);
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
                if (key == EvidenceVectorKey.JsonGlobalColumnsExact)
                    await SaveGlobalOrgCorruptAndAssert(provider, lease, "organization_units", "organization_unit_id", "f09-ou-scope-global",
                        $"update \"{schema}\".organization_units set tenant_scope_kind='tenant', tenant_id='tenant-1' where tenant_scope_kind='global' and tenant_id = '' and organization_unit_id='f09-ou-scope-global'",
                        UnitUnfilteredRead, variant);
                else
                    await SaveOrgCorruptAndAssert(provider, lease, "organization_units", "organization_unit_id", "f09-ou-scope",
                        $"update \"{schema}\".organization_units set tenant_scope_kind='global' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and organization_unit_id='f09-ou-scope'",
                        UnitUnfilteredRead, variant);
                break;
            case PersistedStructuredFieldVariant.OrganizationUnitId:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_units", "organization_unit_id", "f09-ou-id",
                    $"update \"{schema}\".organization_units set organization_unit_id='tampered' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and organization_unit_id='f09-ou-id'",
                    UnitListRead, variant);
                break;
            case PersistedStructuredFieldVariant.OrganizationUnitParentId:
                if (key == EvidenceVectorKey.JsonNonNullColumnNull)
                    await SaveNullableOrgCorruptAndAssert(provider, lease, "organization-unit", "f09-ou-pid-null", key, OrgRead, variant);
                else
                    await SaveNullableOrgCorruptAndAssert(provider, lease, "organization-unit", "f09-ou-pid", key, OrgRead, variant);
                break;
            case PersistedStructuredFieldVariant.OrganizationUnitSortOrder:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_units", "organization_unit_id", "f09-ou-so",
                    $"update \"{schema}\".organization_units set sort_order=999 where tenant_scope_kind='tenant' and tenant_id='tenant-1' and organization_unit_id='f09-ou-so'",
                    OrgRead, variant);
                break;
            case PersistedStructuredFieldVariant.OrganizationUnitIsActive:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_units", "organization_unit_id", "f09-ou-ia",
                    $"update \"{schema}\".organization_units set is_active=not is_active where tenant_scope_kind='tenant' and tenant_id='tenant-1' and organization_unit_id='f09-ou-ia'",
                    OrgRead, variant);
                break;
            case PersistedStructuredFieldVariant.OrganizationUnitCreatedAtUtcTicks:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_units", "organization_unit_id", "f09-ou-ticks",
                    $"update \"{schema}\".organization_units set created_at_utc_ticks=created_at_utc_ticks+1 where tenant_scope_kind='tenant' and tenant_id='tenant-1' and organization_unit_id='f09-ou-ticks'",
                    OrgRead, variant);
                break;
            case PersistedStructuredFieldVariant.OrganizationUnitCreatedAtReadableProjection:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_units", "organization_unit_id", "f09-ou-readable",
                    $"update \"{schema}\".organization_units set created_at=created_at+interval '1 microsecond' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and organization_unit_id='f09-ou-readable'",
                    OrgRead, variant);
                break;

            // ── Position fields ──
            case PersistedStructuredFieldVariant.PositionTenantScope:
                if (key == EvidenceVectorKey.JsonGlobalColumnsExact)
                    await SaveGlobalOrgCorruptAndAssert(provider, lease, "organization_positions", "position_id", "f09-pos-scope-global",
                        $"update \"{schema}\".organization_positions set tenant_scope_kind='tenant', tenant_id='tenant-1' where tenant_scope_kind='global' and tenant_id = '' and position_id='f09-pos-scope-global'",
                        PositionUnfilteredRead, variant);
                else
                    await SaveOrgCorruptAndAssert(provider, lease, "organization_positions", "position_id", "f09-pos-scope",
                        $"update \"{schema}\".organization_positions set tenant_scope_kind='global' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and position_id='f09-pos-scope'",
                        PositionUnfilteredRead, variant);
                break;
            case PersistedStructuredFieldVariant.PositionId:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_positions", "position_id", "f09-pos-id",
                    $"update \"{schema}\".organization_positions set position_id='tampered' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and position_id='f09-pos-id'",
                    PositionListRead, variant);
                break;
            case PersistedStructuredFieldVariant.PositionIsActive:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_positions", "position_id", "f09-pos-ia",
                    $"update \"{schema}\".organization_positions set is_active=not is_active where tenant_scope_kind='tenant' and tenant_id='tenant-1' and position_id='f09-pos-ia'",
                    PosRead, variant);
                break;
            case PersistedStructuredFieldVariant.PositionCreatedAtUtcTicks:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_positions", "position_id", "f09-pos-ticks",
                    $"update \"{schema}\".organization_positions set created_at_utc_ticks=created_at_utc_ticks+1 where tenant_scope_kind='tenant' and tenant_id='tenant-1' and position_id='f09-pos-ticks'",
                    PosRead, variant);
                break;
            case PersistedStructuredFieldVariant.PositionCreatedAtReadableProjection:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_positions", "position_id", "f09-pos-readable",
                    $"update \"{schema}\".organization_positions set created_at=created_at+interval '1 microsecond' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and position_id='f09-pos-readable'",
                    PosRead, variant);
                break;

            // ── Membership fields ──
            case PersistedStructuredFieldVariant.MembershipTenantScope:
                if (key == EvidenceVectorKey.JsonGlobalColumnsExact)
                    await SaveGlobalOrgCorruptAndAssert(provider, lease, "organization_memberships", "membership_id", "f09-mem-scope-global",
                        $"update \"{schema}\".organization_memberships set tenant_scope_kind='tenant', tenant_id='tenant-1' where tenant_scope_kind='global' and tenant_id = '' and membership_id='f09-mem-scope-global'",
                        MembershipUnfilteredRead, variant);
                else
                    await SaveOrgCorruptAndAssert(provider, lease, "organization_memberships", "membership_id", "f09-mem-scope",
                        $"update \"{schema}\".organization_memberships set tenant_scope_kind='global' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and membership_id='f09-mem-scope'",
                        MembershipUnfilteredRead, variant);
                break;
            case PersistedStructuredFieldVariant.MembershipId:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_memberships", "membership_id", "f09-mem-id",
                    $"update \"{schema}\".organization_memberships set membership_id='tampered' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and membership_id='f09-mem-id'",
                    MemRead, variant);
                break;
            case PersistedStructuredFieldVariant.MembershipUserId:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_memberships", "membership_id", "f09-mem-uid",
                    $"update \"{schema}\".organization_memberships set user_id='tampered' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and membership_id='f09-mem-uid'",
                    MembershipUnitRead, variant);
                break;
            case PersistedStructuredFieldVariant.MembershipOrganizationUnitId:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_memberships", "membership_id", "f09-mem-oid",
                    $"update \"{schema}\".organization_memberships set organization_unit_id='tampered' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and membership_id='f09-mem-oid'",
                    MemRead, variant);
                break;
            case PersistedStructuredFieldVariant.MembershipPositionId:
                if (key == EvidenceVectorKey.JsonNonNullColumnNull)
                    await SaveNullableOrgCorruptAndAssert(provider, lease, "membership", "f09-mem-pid-null", key, MemRead, variant);
                else
                    await SaveNullableOrgCorruptAndAssert(provider, lease, "membership", "f09-mem-pid", key, MemRead, variant);
                break;
            case PersistedStructuredFieldVariant.MembershipIsPrimary:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_memberships", "membership_id", "f09-mem-ip",
                    $"update \"{schema}\".organization_memberships set is_primary=not is_primary where tenant_scope_kind='tenant' and tenant_id='tenant-1' and membership_id='f09-mem-ip'",
                    MemRead, variant);
                break;
            case PersistedStructuredFieldVariant.MembershipIsActive:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_memberships", "membership_id", "f09-mem-ia",
                    $"update \"{schema}\".organization_memberships set is_active=not is_active where tenant_scope_kind='tenant' and tenant_id='tenant-1' and membership_id='f09-mem-ia'",
                    MemRead, variant);
                break;
            case PersistedStructuredFieldVariant.MembershipCreatedAtUtcTicks:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_memberships", "membership_id", "f09-mem-ticks",
                    $"update \"{schema}\".organization_memberships set created_at_utc_ticks=created_at_utc_ticks+1 where tenant_scope_kind='tenant' and tenant_id='tenant-1' and membership_id='f09-mem-ticks'",
                    MemRead, variant);
                break;
            case PersistedStructuredFieldVariant.MembershipCreatedAtReadableProjection:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_memberships", "membership_id", "f09-mem-readable",
                    $"update \"{schema}\".organization_memberships set created_at=created_at+interval '1 microsecond' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and membership_id='f09-mem-readable'",
                    MemRead, variant);
                break;

            // ── RoleAssignment fields ──
            case PersistedStructuredFieldVariant.RoleAssignmentTenantScope:
                if (key == EvidenceVectorKey.JsonGlobalColumnsExact)
                    await SaveGlobalOrgCorruptAndAssert(provider, lease, "organization_role_assignments", "assignment_id", "f09-ra-scope-global",
                        $"update \"{schema}\".organization_role_assignments set tenant_scope_kind='tenant', tenant_id='tenant-1' where tenant_scope_kind='global' and tenant_id = '' and assignment_id='f09-ra-scope-global'",
                        RoleUnfilteredRead, variant);
                else
                    await SaveOrgCorruptAndAssert(provider, lease, "organization_role_assignments", "assignment_id", "f09-ra-scope",
                        $"update \"{schema}\".organization_role_assignments set tenant_scope_kind='global' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and assignment_id='f09-ra-scope'",
                        RoleUnfilteredRead, variant);
                break;
            case PersistedStructuredFieldVariant.RoleAssignmentId:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_role_assignments", "assignment_id", "f09-ra-id",
                    $"update \"{schema}\".organization_role_assignments set assignment_id='tampered' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and assignment_id='f09-ra-id'",
                    RaRead, variant);
                break;
            case PersistedStructuredFieldVariant.RoleAssignmentUserId:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_role_assignments", "assignment_id", "f09-ra-uid",
                    $"update \"{schema}\".organization_role_assignments set user_id='tampered' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and assignment_id='f09-ra-uid'",
                    RoleTamperedUserRead, variant);
                break;
            case PersistedStructuredFieldVariant.RoleAssignmentRoleId:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_role_assignments", "assignment_id", "f09-ra-rid",
                    $"update \"{schema}\".organization_role_assignments set role_id='tampered' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and assignment_id='f09-ra-rid'",
                    RaRead, variant);
                break;
            case PersistedStructuredFieldVariant.RoleAssignmentOrganizationUnitId:
                if (key == EvidenceVectorKey.JsonNonNullColumnNull)
                    await SaveNullableOrgCorruptAndAssert(provider, lease, "role-assignment", "f09-ra-oid-null", key, RaRead, variant);
                else
                    await SaveNullableOrgCorruptAndAssert(provider, lease, "role-assignment", "f09-ra-oid", key, RaRead, variant);
                break;
            case PersistedStructuredFieldVariant.RoleAssignmentIsActive:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_role_assignments", "assignment_id", "f09-ra-ia",
                    $"update \"{schema}\".organization_role_assignments set is_active=not is_active where tenant_scope_kind='tenant' and tenant_id='tenant-1' and assignment_id='f09-ra-ia'",
                    RaRead, variant);
                break;
            case PersistedStructuredFieldVariant.RoleAssignmentCreatedAtUtcTicks:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_role_assignments", "assignment_id", "f09-ra-ticks",
                    $"update \"{schema}\".organization_role_assignments set created_at_utc_ticks=created_at_utc_ticks+1 where tenant_scope_kind='tenant' and tenant_id='tenant-1' and assignment_id='f09-ra-ticks'",
                    RaRead, variant);
                break;
            case PersistedStructuredFieldVariant.RoleAssignmentCreatedAtReadableProjection:
                await SaveOrgCorruptAndAssert(provider, lease, "organization_role_assignments", "assignment_id", "f09-ra-readable",
                    $"update \"{schema}\".organization_role_assignments set created_at=created_at+interval '1 microsecond' where tenant_scope_kind='tenant' and tenant_id='tenant-1' and assignment_id='f09-ra-readable'",
                    RaRead, variant);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(variant));
        }
    }

    public static IEnumerable<object[]> StructuredFieldData()
    {
        foreach (var tuple in ControlPlaneReferenceDataCaseManifest.EvidenceTuplesFor(CaseId.F09, RequiredRunner.PostgreSql))
            yield return new object[]
            {
                Enum.Parse<PersistedStructuredFieldVariant>(tuple.Variant),
                tuple.Key
            };
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

    private static async Task DropConstraintAsync(
        string connectionString,
        string schema,
        string table,
        string constraint)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"alter table \"{schema}\".\"{table}\" drop constraint \"{constraint}\"",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task SaveDraftCorruptAndAssert(
        ServiceProvider provider,
        PostgreSqlRuntimeSchemaLease lease,
        string draftId,
        string corruptSql,
        Func<IDescriptorDraftStore, string, Task>? readAsync = null)
    {
        var store = provider.GetRequiredService<IDescriptorDraftStore>();
        await store.SaveAsync(CreateDraft(draftId, DescriptorDraftOperation.Create, DescriptorDraftStatus.Created, DateTimeOffset.UnixEpoch));
        await DropAllCheckConstraintsAsync(lease.Options.ConnectionString, lease.Options.Schema, "control_plane_descriptor_drafts");
        await CorruptAsync(lease.Options.ConnectionString, lease.Options.Schema, corruptSql);
        Func<Task> act = readAsync is null
            ? () => store.GetAsync("tenant-1", draftId)
            : () => readAsync(store, draftId);
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

    private async Task SaveGlobalOrgCorruptAndAssert(ServiceProvider provider, PostgreSqlRuntimeSchemaLease lease,
        string table, string idCol, string id, string corruptSql,
        Func<IOrganizationStore, string, Task> readAsync,
        PersistedStructuredFieldVariant variant)
    {
        var orgs = provider.GetRequiredService<IOrganizationStore>();
        switch (table)
        {
            case "organization_units":
                await orgs.SaveOrganizationUnitAsync(new OrganizationUnit { Id = id, TenantId = null, Name = id });
                break;
            case "organization_positions":
                await orgs.SavePositionAsync(new Position { Id = id, TenantId = null, Name = id });
                break;
            case "organization_memberships":
                await orgs.SaveMembershipAsync(new UserOrganizationMembership { Id = id, TenantId = null, UserId = "u-" + id, OrganizationUnitId = "o-" + id });
                break;
            case "organization_role_assignments":
                await orgs.SaveRoleAssignmentAsync(new UserOrganizationRoleAssignment { Id = id, TenantId = null, UserId = "u-" + id, RoleId = "r-" + id });
                break;
        }
        await CorruptAsync(lease.Options.ConnectionString, lease.Options.Schema, corruptSql);
        try
        {
            await readAsync(orgs, id);
            Assert.Fail($"Expected PersistedInvariantViolation for variant {variant} but no exception was thrown.");
        }
        catch (RuntimePersistenceContractException ex) when (ex.Code == RuntimePersistenceContractErrorCode.PersistedInvariantViolation)
        {
            // Expected — structured column scope disagrees with the JSON snapshot
        }
    }

    private async Task SaveNullableOrgCorruptAndAssert(ServiceProvider provider, PostgreSqlRuntimeSchemaLease lease,
        string surface, string id, EvidenceVectorKey key,
        Func<IOrganizationStore, string, Task> readAsync,
        PersistedStructuredFieldVariant variant)
    {
        var orgs = provider.GetRequiredService<IOrganizationStore>();
        string table;
        string idCol;
        string column;
        switch (surface)
        {
            case "organization-unit":
                table = "organization_units";
                idCol = "organization_unit_id";
                column = "parent_id";
                await orgs.SaveOrganizationUnitAsync(new OrganizationUnit
                {
                    Id = id, TenantId = "tenant-1", Name = id,
                    ParentId = key == EvidenceVectorKey.JsonNonNullColumnNull ? "parent" : null
                });
                break;
            case "membership":
                table = "organization_memberships";
                idCol = "membership_id";
                column = "position_id";
                await orgs.SaveMembershipAsync(new UserOrganizationMembership
                {
                    Id = id, TenantId = "tenant-1", UserId = "u-" + id, OrganizationUnitId = "o-" + id,
                    PositionId = key == EvidenceVectorKey.JsonNonNullColumnNull ? "position" : null
                });
                break;
            default:
                table = "organization_role_assignments";
                idCol = "assignment_id";
                column = "organization_unit_id";
                await orgs.SaveRoleAssignmentAsync(new UserOrganizationRoleAssignment
                {
                    Id = id, TenantId = "tenant-1", UserId = "u-" + id, RoleId = "r-" + id,
                    OrganizationUnitId = key == EvidenceVectorKey.JsonNonNullColumnNull ? "unit" : null
                });
                break;
        }

        await DropAllCheckConstraintsAsync(lease.Options.ConnectionString, lease.Options.Schema, table);
        var columnValue = key == EvidenceVectorKey.JsonNullColumnNonNull
            ? "'unexpected'"
            : "null";
        await CorruptAsync(lease.Options.ConnectionString, lease.Options.Schema,
            $"update \"{lease.Options.Schema}\".{table} set {column}={columnValue} where tenant_scope_kind='tenant' and tenant_id='tenant-1' and {idCol}=@id",
            ("id", id));
        try
        {
            await readAsync(orgs, id);
            Assert.Fail($"Expected PersistedInvariantViolation for variant {variant} but no exception was thrown.");
        }
        catch (RuntimePersistenceContractException ex) when (ex.Code == RuntimePersistenceContractErrorCode.PersistedInvariantViolation)
        {
            // Expected — structured column disagrees with the JSON snapshot
        }
    }

    private static Func<Task> OrgSave(ServiceProvider p, string id) => async () =>
        await p.GetRequiredService<IOrganizationStore>().SaveOrganizationUnitAsync(new OrganizationUnit { Id = id, TenantId = "tenant-1", Name = id });
    private static async Task OrgRead(IOrganizationStore orgs, string id) =>
        await orgs.GetOrganizationUnitByIdAsync(id, "tenant-1");
    private static async Task UnitListRead(IOrganizationStore orgs, string _) =>
        await orgs.GetOrganizationUnitsAsync("tenant-1");
    private static async Task UnitUnfilteredRead(IOrganizationStore orgs, string _) =>
        await orgs.GetOrganizationUnitsAsync();
    private static Func<Task> PosSave(ServiceProvider p, string id) => async () =>
        await p.GetRequiredService<IOrganizationStore>().SavePositionAsync(new Position { Id = id, TenantId = "tenant-1", Name = id });
    private static async Task PosRead(IOrganizationStore orgs, string id) =>
        await orgs.GetPositionByIdAsync(id, "tenant-1");
    private static async Task PositionListRead(IOrganizationStore orgs, string _) =>
        await orgs.GetPositionsAsync("tenant-1");
    private static async Task PositionUnfilteredRead(IOrganizationStore orgs, string _) =>
        await orgs.GetPositionsAsync();
    private static Func<Task> MemSave(ServiceProvider p, string id) => async () =>
        await p.GetRequiredService<IOrganizationStore>().SaveMembershipAsync(new UserOrganizationMembership { Id = id, TenantId = "tenant-1", UserId = "u-" + id, OrganizationUnitId = "o-" + id });
    private static async Task MemRead(IOrganizationStore orgs, string id) =>
        await orgs.GetMembershipsByUserAsync("u-" + id, "tenant-1");
    private static async Task MembershipUnitRead(IOrganizationStore orgs, string id) =>
        await orgs.GetMembershipsByOrganizationUnitAsync("o-" + id, "tenant-1");
    private static async Task MembershipUnfilteredRead(IOrganizationStore orgs, string id) =>
        await orgs.GetMembershipsByUserAsync("u-" + id);
    private static Func<Task> RaSave(ServiceProvider p, string id) => async () =>
        await p.GetRequiredService<IOrganizationStore>().SaveRoleAssignmentAsync(new UserOrganizationRoleAssignment { Id = id, TenantId = "tenant-1", UserId = "u-" + id, RoleId = "r-" + id });
    private static async Task RaRead(IOrganizationStore orgs, string id) =>
        await orgs.GetRoleAssignmentsByUserAsync("u-" + id, "tenant-1");
    private static async Task RoleTamperedUserRead(IOrganizationStore orgs, string _) =>
        await orgs.GetRoleAssignmentsByUserAsync("tampered", "tenant-1");
    private static async Task RoleUnfilteredRead(IOrganizationStore orgs, string id) =>
        await orgs.GetRoleAssignmentsByUserAsync("u-" + id);

    private static async Task DraftListRead(IDescriptorDraftStore store, string _) =>
        await store.ListAsync("tenant-1");

    private static async Task DraftTamperedTenantRead(IDescriptorDraftStore store, string _) =>
        await store.ListAsync("tampered");

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
