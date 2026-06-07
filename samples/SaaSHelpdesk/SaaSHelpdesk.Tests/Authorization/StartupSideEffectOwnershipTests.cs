using System.Threading.Tasks;
using FluentAssertions;
using SaaSHelpdesk.Tests.Fixtures;
using Xunit;

namespace SaaSHelpdesk.Tests.Authorization;

/// <summary>
/// Verifies that startup side effects (migration + seeding) are owned
/// by the app module and execute exactly once during application startup.
/// </summary>
public class StartupSideEffectOwnershipTests : IClassFixture<HelpdeskWebApplicationFactory>
{
    private readonly HelpdeskWebApplicationFactory _factory;

    public StartupSideEffectOwnershipTests(HelpdeskWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// The SaaSHelpdesk WebModule owns the migration-and-seed side effect.
    /// The framework WebModule must not execute it. This test verifies
    /// that the side effect runs exactly once during startup.
    /// </summary>
    [Fact]
    public async Task MigrationAndSeed_Should_Run_Exactly_Once_During_Startup()
    {
        // Trigger full app startup — this runs the module lifecycle
        // including OnApplicationInitializationAsync on each module.
        // The SaaSHelpdesk WebModule calls HostMigrationAndSeedRunner.RunAsync.
        // Our counted wrapper increments the counter on each call.
        using var client = _factory.CreateClient();

        // The counter is available after the app has started.
        var counter = _factory.MigrationSeedCounter;

        // Assert: migration + seeding executed exactly once.
        // Zero means the framework WebModule doesn't own the side effect
        // (which is correct — it shouldn't). More than one means a
        // duplicate execution path exists.
        counter.Count.Should().Be(1,
            "migration and seeding must run exactly once, owned only by the app-level WebModule");
    }
}
