using CrestCreates.Runtime.Persistence.Abstractions.Providers;
using System;
using System.Linq;
using CrestCreates.Runtime.Persistence.PostgreSql;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

public sealed class PostgreSqlProviderContractTests
{
    [Fact]
    public void PostgreSqlProvider_AfterVerifiedFixture_ShouldDeclareFullDurable()
    {
        var capabilities = new PostgreSqlRuntimeProviderCapabilities();
        capabilities.Tier.Should().Be(RuntimePersistenceProviderTier.FullDurable);
        capabilities.SupportsProcessDurability.Should().BeTrue();
        capabilities.SupportsRestartRecovery.Should().BeTrue();
        capabilities.SupportsMigrations.Should().BeTrue();
        capabilities.SupportsDatabaseNativeAotEvidence.Should().BeTrue();
    }

    [Fact]
    public void MigrationOptions_ShouldDefaultToValidationOnly()
    {
        new PostgreSqlRuntimeMigrationOptions().ApplyMigrations.Should().BeFalse();
        new PostgreSqlRuntimePersistenceOptions { ConnectionString = "Host=localhost" }.ApplyMigrations.Should().BeFalse();
    }

    [Fact]
    public void ProviderAssembly_ShouldNotReferenceEntityFrameworkCore()
    {
        var referencesEfCore = typeof(PostgreSqlRuntimePersistenceOptions).Assembly
            .GetReferencedAssemblies()
            .Any(reference => reference.Name is not null
                && reference.Name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        referencesEfCore.Should().BeFalse();
    }

    [Fact]
    public void ProviderAssembly_ShouldNotReferenceRuntimePersistenceConcrete()
    {
        var referencesConcrete = typeof(PostgreSqlRuntimePersistenceOptions).Assembly
            .GetReferencedAssemblies()
            .Any(reference => string.Equals(
                reference.Name,
                "CrestCreates.Runtime.Persistence",
                StringComparison.Ordinal));

        referencesConcrete.Should().BeFalse();
    }

    [Fact]
    public void ProviderOptions_ShouldRejectUnsafeSchemaBeforeBuildingCommands()
    {
        var act = () => new ServiceCollection().AddCrestCreatesPostgreSqlRuntimePersistence(
            new PostgreSqlRuntimePersistenceOptions
            {
                ConnectionString = "Host=localhost",
                Schema = "runtime;drop_schema"
            });

        act.Should().Throw<ArgumentException>();
    }
}
