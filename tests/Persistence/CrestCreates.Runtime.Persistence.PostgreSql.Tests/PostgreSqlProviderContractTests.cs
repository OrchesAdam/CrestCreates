using CrestCreates.Runtime.Persistence.Abstractions.Providers;
using System;
using System.Linq;
using CrestCreates.Runtime.Persistence.PostgreSql;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

public sealed class PostgreSqlProviderContractTests
{
    [Fact]
    public void DirectNpgsqlProvider_ShouldDeclareDurableProcessAndMigrationSupport()
    {
        var capabilities = new PostgreSqlRuntimeProviderCapabilities();
        capabilities.Tier.Should().Be(RuntimePersistenceProviderTier.FullSemantic);
        capabilities.SupportsProcessDurability.Should().BeTrue();
        capabilities.SupportsRestartRecovery.Should().BeTrue();
        capabilities.SupportsMigrations.Should().BeTrue();
        capabilities.SupportsDatabaseNativeAotEvidence.Should().BeFalse();
    }

    [Fact]
    public void MigrationOptions_ShouldDefaultToValidationOnly()
        => new PostgreSqlRuntimeMigrationOptions().ApplyMigrations.Should().BeFalse();

    [Fact]
    public void ProviderAssembly_ShouldNotReferenceEntityFrameworkCore()
    {
        var referencesEfCore = typeof(PostgreSqlRuntimePersistenceOptions).Assembly
            .GetReferencedAssemblies()
            .Any(reference => reference.Name is not null
                && reference.Name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        referencesEfCore.Should().BeFalse();
    }
}
