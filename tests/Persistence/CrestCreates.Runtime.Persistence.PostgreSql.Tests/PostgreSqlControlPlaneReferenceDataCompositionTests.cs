using CrestCreates.ControlPlane.ReferenceData.Persistence.Testing;
using CrestCreates.DescriptorDraft;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Organization;
using CrestCreates.Organization.Abstractions;
using CrestCreates.Runtime.Persistence.PostgreSql;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

public class PostgreSqlControlPlaneReferenceDataCompositionTests
{
    [Fact]
    public void C09_OptInRegistration_Should_ReplaceExactlySelectedStores()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.C09, "Composition", "Composition", EvidenceVectorKey.Default, RequiredRunner.PostgreSql);
        var services = new ServiceCollection();
        services.AddSingleton<PostgreSqlRuntimeProviderRegistrationMarker>();
        services.AddSingleton<IDescriptorDraftStore, InMemoryDescriptorDraftStore>();
        services.AddSingleton<IOrganizationStore, InMemoryOrganizationStore>();
        services.AddSingleton<IDataPermissionScopeRuleStore, InMemoryDataPermissionScopeRuleStore>();
        services.AddCrestCreatesPostgreSqlRuntimePersistence(TestOptions());

        services.AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();

        var descriptors = services.Where(d =>
            d.ServiceType == typeof(IDescriptorDraftStore) ||
            d.ServiceType == typeof(IOrganizationStore) ||
            d.ServiceType == typeof(IDataPermissionScopeRuleStore)).ToList();

        descriptors.Should().AllSatisfy(d => d.ImplementationType.Should().NotBe(typeof(InMemoryDescriptorDraftStore),
            "opt-in must replace InMemory stores"));
    }

    [Fact]
    public void C14_OptInWithoutBaseProvider_Should_FailWithClearCompositionError()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.C14, "Composition", "Composition", EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var services = new ServiceCollection();

        var act = () => services.AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("base PostgreSQL Runtime persistence provider");
    }

    [Fact]
    public void C14_OptInWithMarkerOnly_Should_RejectIncompleteProviderKernel()
    {
        var services = new ServiceCollection();
        services.AddSingleton<PostgreSqlRuntimeProviderRegistrationMarker>();

        var act = () => services.AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("complete base PostgreSQL Runtime persistence provider kernel");
    }

    [Fact]
    public void C14_OptInWithOptionsOnly_Should_RejectIncompleteProviderKernel()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TestOptions());

        var act = () => services.AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("complete base PostgreSQL Runtime persistence provider kernel");
    }

    [Fact]
    public void C15_RepeatedBaseFirstOptIn_Should_RemainIdempotent()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.C15, "Composition", "Composition", EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var services = new ServiceCollection();
        services.AddCrestCreatesPostgreSqlRuntimePersistence(TestOptions());

        services.AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();
        services.AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();

        var descriptors = services.Where(d =>
            d.ServiceType == typeof(IDescriptorDraftStore)).ToList();

        descriptors.Should().HaveCount(1, "repeated opt-in should leave exactly one descriptor store");
        descriptors[0].ImplementationType.Should().Be(typeof(PostgreSqlDescriptorDraftStore));
    }

    [Fact]
    public void P08_DataPermissionScope_Should_RemainDerived()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.P08, "Rule", "Rule", EvidenceVectorKey.Default, RequiredRunner.Architecture);
        var services = new ServiceCollection();
        services.AddCrestCreatesPostgreSqlRuntimePersistence(TestOptions());
        services.AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();

        var serviceTypes = services.Select(s => s.ServiceType).ToList();
        serviceTypes.Should().NotContain(t => t.Name == "IDataPermissionScopeStore",
            "DataPermissionScope should remain derived, not persisted");
    }

    [Fact]
    public void C08_BaseProviderRegistration_Should_NotReplaceReferenceStores()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.C08, "Composition", "Composition", EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var services = new ServiceCollection();
        services.AddSingleton<IDescriptorDraftStore, InMemoryDescriptorDraftStore>();
        services.AddSingleton<IOrganizationStore, InMemoryOrganizationStore>();
        services.AddSingleton<IDataPermissionScopeRuleStore, InMemoryDataPermissionScopeRuleStore>();
        services.AddCrestCreatesPostgreSqlRuntimePersistence(TestOptions());

        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IDescriptorDraftStore>().Should().BeOfType<InMemoryDescriptorDraftStore>();
        provider.GetRequiredService<IOrganizationStore>().Should().BeOfType<InMemoryOrganizationStore>();
        provider.GetRequiredService<IDataPermissionScopeRuleStore>().Should().BeOfType<InMemoryDataPermissionScopeRuleStore>();
    }

    private static PostgreSqlRuntimePersistenceOptions TestOptions()
        => new()
        {
            ConnectionString = "Host=localhost;Database=crestcreates;Username=crest;Password=crest",
            Schema = "composition_test"
        };
}
