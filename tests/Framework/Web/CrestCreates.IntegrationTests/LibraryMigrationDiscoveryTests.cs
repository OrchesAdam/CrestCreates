using System.Linq;
using FluentAssertions;
using LibraryManagement.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CrestCreates.IntegrationTests;

public class LibraryMigrationDiscoveryTests
{
    [Fact]
    public void Library_context_discovers_the_setting_host_null_unique_migration()
    {
        var options = new DbContextOptionsBuilder<LibraryDbContext>()
            .UseNpgsql("Host=localhost;Database=crest_migration_discovery;Username=probe;Password=probe")
            .Options;

        using var context = new LibraryDbContext(options);

        var migrations = context.Database.GetMigrations().ToList();

        migrations.Should().Contain(
            "20261109090000_R3_12_SettingHostNullUnique",
            "an existing LibraryManagement database must apply the host-null uniqueness migration through the normal EF migration chain");
        migrations.Should().Contain("20260506065811_InitialCreate");
    }
}
