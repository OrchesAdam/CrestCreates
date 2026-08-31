using CrestCreates.ControlPlane.ReferenceData.Persistence.Testing;
using CrestCreates.Organization.Abstractions;
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
        var count = (long)await cmd2.ExecuteScalarAsync();
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
    public async Task RepeatedBlindSave_Should_AdvanceGenerationAgain()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.OVG08, "Authority", "RepeatedBlindSave", EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
        await OrganizationStoreContractCases.RunRepeatedBlindSaveAdvancesAgainAsync(_store, "ovg08");
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
}
