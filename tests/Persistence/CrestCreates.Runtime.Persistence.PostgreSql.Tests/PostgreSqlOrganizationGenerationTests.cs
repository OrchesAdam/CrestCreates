using CrestCreates.ControlPlane.ReferenceData.Persistence.Testing;
using CrestCreates.Organization.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using CrestCreates.Runtime.Persistence.PostgreSql;
using CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

[Collection(PostgreSqlRuntimeCollection.Name)]
public sealed class PostgreSqlOrganizationGenerationTests : IAsyncLifetime
{
    private readonly PostgreSqlRuntimeCollectionFixture _fixture;
    private PostgreSqlRuntimeSchemaLease _lease = null!;
    private ServiceProvider _provider = null!;
    private IOrganizationStore _store = null!;

    public PostgreSqlOrganizationGenerationTests(PostgreSqlRuntimeCollectionFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _lease = await _fixture.CreateSchemaLeaseAsync();
        _provider = BuildProvider(_lease.Options);
        _store = _provider.GetRequiredService<IOrganizationStore>();
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
        if (_lease is not null)
            await _lease.DisposeAsync();
    }

    private static ServiceProvider BuildProvider(PostgreSqlRuntimePersistenceOptions options)
    {
        var services = new ServiceCollection();
        services.AddSingleton(options);
        services.AddCrestCreatesPostgreSqlRuntimePersistence(options);
        services.AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();
        return services.BuildServiceProvider();
    }

    private static OrganizationUnit Unit(string id, string? tenantId, string? parentId = null, DateTimeOffset? createdAt = null)
        => new() { Id = id, TenantId = tenantId, Name = id, ParentId = parentId, CreatedAt = createdAt ?? DateTimeOffset.UnixEpoch };

    private static Position Position(string id, string? tenantId, DateTimeOffset? createdAt = null)
        => new() { Id = id, TenantId = tenantId, Name = id, CreatedAt = createdAt ?? DateTimeOffset.UnixEpoch };

    private static UserOrganizationMembership Membership(string id, string userId, string? tenantId, DateTimeOffset? createdAt = null)
        => new() { Id = id, TenantId = tenantId, UserId = userId, OrganizationUnitId = "unit", CreatedAt = createdAt ?? DateTimeOffset.UnixEpoch };

    private static UserOrganizationRoleAssignment Role(string id, string userId, string? tenantId, DateTimeOffset? createdAt = null)
        => new() { Id = id, TenantId = tenantId, UserId = userId, RoleId = id, CreatedAt = createdAt ?? DateTimeOffset.UnixEpoch };

    [Fact]
    public async Task V013_Migration_Should_Be_Applied()
    {
        // Verify V013 is in the migration history and the table exists.
        await using var connection = new NpgsqlConnection(_lease.Options.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"select version from \"{_lease.Options.Schema}\".crest_runtime_schema_migrations where version = 'V013';",
            connection);
        var version = await cmd.ExecuteScalarAsync();
        version.Should().Be("V013", "V013 migration should be applied");

        await using var cmd2 = new NpgsqlCommand(
            $"select count(*) from information_schema.tables where table_schema = @schema and table_name = 'organization_scope_generations';",
            connection);
        cmd2.Parameters.AddWithValue("schema", _lease.Options.Schema);
        var count = Convert.ToInt64(await cmd2.ExecuteScalarAsync());
        count.Should().Be(1, "organization_scope_generations table should exist");
    }



    [Fact]
    public async Task OrganizationScopeGeneration_Should_StartAtZero()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.OVG01, "Authority", "InitialGeneration", EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
        await OrganizationStoreContractCases.RunInitialGenerationIsZeroAsync(_store, "ovg01");
    }

    [Fact]
    public async Task OrganizationWrite_Should_Atomically_AdvanceGeneration()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.OVG02, "Authority", "OrganizationUnit", EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
        await OrganizationStoreContractCases.RunOrganizationUnitSaveAdvancesGenerationAsync(_store, "ovg02");
    }

    [Fact]
    public async Task KnownPreCommitFailure_Should_AdvanceNeitherDataNorGeneration()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.OVG06, "Authority", "KnownRollback", EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
        using var injection = PostgreSqlRuntimeTestHooks.BlockAfterWritePoint((point, _) =>
        {
            point.Should().Be("organization-unit-snapshot-upserted");
            throw new InvalidOperationException("injected failure after entity upsert");
        });

        await _store.Invoking(store => store.SaveOrganizationUnitAsync(Unit("ovg06-unit", "ovg06")))
            .Should().ThrowAsync<InvalidOperationException>();

        (await _store.GetOrganizationUnitByIdAsync("ovg06-unit", "ovg06")).Should().BeNull();
        (await _store.ReadScopeGenerationAsync(OrganizationScopeIdentity.Tenant("ovg06")))
            .Should().Be(OrganizationScopeGenerationRead.Available(0));
    }

    [Fact]
    public async Task OrganizationSaveSurface_Should_AdvanceSharedScopeGeneration()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.OVG03, "Authority", "Position", EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.OVG04, "Authority", "Membership", EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.OVG05, "Authority", "RoleAssignment", EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
        await OrganizationStoreContractCases.RunPositionSaveAdvancesSameScopeGenerationAsync(_store, "ovg03");
        await OrganizationStoreContractCases.RunMembershipSaveAdvancesSameScopeGenerationAsync(_store, "ovg04");
        await OrganizationStoreContractCases.RunRoleAssignmentSaveAdvancesSameScopeGenerationAsync(_store, "ovg05");
    }

    [Fact]
    public async Task TenantGeneration_Should_Not_Affect_OtherTenants()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.OVG07, "Authority", "TenantIsolation", EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
        await OrganizationStoreContractCases.RunTenantGenerationDoesNotAffectOtherTenantsAsync(_store, "ovg07");
    }

    [Fact]
    public async Task Generation_Should_Not_Change_DomainBlindWriteSemantics()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.OVG08, "Authority", "RepeatedBlindSave", EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
        await OrganizationStoreContractCases.RunRepeatedBlindSaveAdvancesAgainAsync(_store, "ovg08");
    }

    [Fact]
    public async Task V013Upgrade_Should_PreserveV012Rows_AtGenerationZero()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.OVG09, "Provider", "V012Upgrade", EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
        var options = new PostgreSqlRuntimePersistenceOptions
        {
            ConnectionString = _fixture.ConnectionString,
            Schema = $"itest_{Guid.NewGuid():N}"
        };
        await using var upgradeLease = new PostgreSqlRuntimeSchemaLease(_fixture.ConnectionString, options);
        var reachedV013 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseV013 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var barrier = PostgreSqlRuntimeTestHooks.BlockBeforeMigration(async (version, ct) =>
        {
            if (version != "V013")
                return;
            reachedV013.TrySetResult(true);
            await releaseV013.Task.WaitAsync(ct);
        });
        var apply = new PostgreSqlRuntimeMigrationRunner(options)
            .ApplyAsync(new PostgreSqlRuntimeMigrationOptions { ApplyMigrations = true });

        try
        {
            await reachedV013.Task;
            var original = Unit("ovg09-unit", "ovg09");
            var json = PostgreSqlRuntimeStoreSupport.Serialize(
                original,
                PostgreSqlControlPlaneReferenceDataJsonSerializerContext.Default.OrganizationUnit);
            await using (var connection = new NpgsqlConnection(options.ConnectionString))
            {
                await connection.OpenAsync();
                await using var seed = new NpgsqlCommand($"""
                    insert into "{options.Schema}".organization_units
                        (tenant_scope_kind, tenant_id, organization_unit_id, parent_id, sort_order, is_active,
                         created_at_utc_ticks, created_at, state_contract_version, state_json)
                    values ('tenant', 'ovg09', 'ovg09-unit', null, 0, true, @ticks, @created, 1, @json::jsonb);
                    """, connection);
                seed.Parameters.AddWithValue("ticks", original.CreatedAt.UtcTicks);
                seed.Parameters.AddWithValue("created", original.CreatedAt.UtcDateTime);
                seed.Parameters.AddWithValue("json", json);
                await seed.ExecuteNonQueryAsync();
            }
        }
        finally
        {
            releaseV013.TrySetResult(true);
            await apply;
        }

        await using var upgradedProvider = BuildProvider(options);
        var upgradedStore = upgradedProvider.GetRequiredService<IOrganizationStore>();
        (await upgradedStore.ReadScopeGenerationAsync(OrganizationScopeIdentity.Tenant("ovg09")))
            .Should().Be(OrganizationScopeGenerationRead.Available(0));

        await upgradedStore.SaveOrganizationUnitAsync(new OrganizationUnit
        {
            Id = "ovg09-unit",
            TenantId = "ovg09",
            Name = "updated-after-upgrade"
        });
        (await upgradedStore.ReadScopeGenerationAsync(OrganizationScopeIdentity.Tenant("ovg09")))
            .Should().Be(OrganizationScopeGenerationRead.Available(1));
        (await upgradedStore.GetOrganizationUnitByIdAsync("ovg09-unit", "ovg09"))!.Name
            .Should().Be("updated-after-upgrade");
    }

    [Fact]
    public async Task GenerationOverflow_Should_FailWithoutEntityMutationOrWrap()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.OVG10, "Provider", "GenerationOverflow", EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
        await _store.SaveOrganizationUnitAsync(Unit("ovg10-unit", "ovg10"));
        await ExecuteAsync($"""
            update "{_lease.Options.Schema}".organization_scope_generations
            set generation = {long.MaxValue}
            where tenant_scope_kind = 'tenant' and tenant_id = 'ovg10';
            """);

        var replacement = new OrganizationUnit
        {
            Id = "ovg10-unit",
            TenantId = "ovg10",
            Name = "must-not-commit"
        };
        (await Record.ExceptionAsync(() => _store.SaveOrganizationUnitAsync(replacement)))
            .Should().NotBeNull();

        (await _store.ReadScopeGenerationAsync(OrganizationScopeIdentity.Tenant("ovg10")))
            .Should().Be(OrganizationScopeGenerationRead.Available(long.MaxValue));
        (await _store.GetOrganizationUnitByIdAsync("ovg10-unit", "ovg10"))!.Name
            .Should().Be("ovg10-unit");
    }

    [Fact]
    public async Task GenerationSchemaDrift_Should_FailAsContractError_NotUnavailable()
    {
        await ExecuteAsync($"drop table \"{_lease.Options.Schema}\".organization_scope_generations;");

        await _store.Invoking(store => store.ReadScopeGenerationAsync(OrganizationScopeIdentity.Tenant("ovg10-drift")))
            .Should().ThrowAsync<RuntimePersistenceContractException>();
    }

    [Theory]
    [InlineData("42501")] // insufficient_privilege
    [InlineData("57014")] // query_canceled
    [InlineData("XX000")] // internal_error
    public void UnknownPostgresServerFailure_Should_NotBeClassifiedAsAvailability(string sqlState)
    {
        PostgreSqlOrganizationStore.IsGenerationSchemaContractViolation(sqlState)
            .Should().BeFalse("only the explicit persisted-schema contract SQLSTATE allowlist may be translated");
    }

    [Fact]
    public void NonTransientNpgsqlClientFailure_Should_NotBeClassifiedAsAvailability()
    {
        var exception = new NpgsqlException("non-transient client failure");

        PostgreSqlOrganizationStore.IsGenerationAvailabilityFailure(exception)
            .Should().BeFalse(
                "programming or unknown Npgsql client failures must propagate instead of enabling direct fallback");
    }

    [Fact]
    public async Task CommitUnknown_Should_NeverProduce_OneSided_Data_And_Generation()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.OVG11, "Provider", "CommitUnknown", EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
        using var injection = PostgreSqlRuntimeTestHooks.BlockAfterCommit(
            () => throw new InvalidOperationException("injected acknowledgement loss"));

        await _store.Invoking(store => store.SaveOrganizationUnitAsync(Unit("ovg11-unit", "ovg11")))
            .Should().ThrowAsync<RuntimeTransactionCommitUnknownException>();

        (await _store.GetOrganizationUnitByIdAsync("ovg11-unit", "ovg11")).Should().NotBeNull();
        (await _store.ReadScopeGenerationAsync(OrganizationScopeIdentity.Tenant("ovg11")))
            .Should().Be(OrganizationScopeGenerationRead.Available(1));
    }

    [Fact]
    public async Task ConnectivityFailure_Should_ReturnTypedUnavailable()
    {
        var unavailableOptions = new PostgreSqlRuntimePersistenceOptions
        {
            ConnectionString = "Host=127.0.0.1;Port=1;Username=none;Password=none;Database=none;Timeout=1",
            Schema = "unavailable"
        };
        await using var unavailableProvider = BuildProvider(unavailableOptions);
        var unavailableStore = unavailableProvider.GetRequiredService<IOrganizationStore>();

        var result = await unavailableStore.ReadScopeGenerationAsync(OrganizationScopeIdentity.Tenant("unavailable"));

        result.Should().Be(OrganizationScopeGenerationRead.Unavailable);
    }

    [Fact]
    public async Task OrganizationScopeIdentity_Should_Reject_DefaultUnknownAndInvalidTenant()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.OVG12, "Contract", "ScopeIdentity", EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
        await OrganizationStoreContractCases.RunInvalidScopeShouldBeRejectedBeforeIo();
        Assert.Throws<ArgumentException>(() => OrganizationScopeIdentity.Tenant(""));
        Assert.Throws<ArgumentException>(() => OrganizationScopeIdentity.Tenant("   "));
    }

    [Fact]
    public async Task GlobalAndTenantGeneration_Should_BeIndependent()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.OVG12, "Contract", "ScopeIdentity", EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
        await OrganizationStoreContractCases.RunGlobalAndTenantGenerationAreIndependentAsync(_store, "ovg12");
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_lease.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
