using System.Threading;
using CrestCreates.Accountability.Bootstrap;
using CrestCreates.AuditLogging.Modules;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CrestCreates.AuditLogging.Tests;

public sealed class AuditLoggingCompositionTests
{
    [Fact]
    public async Task AuditLoggingHostWithoutAccountabilityFailsDuringStartup()
    {
        var services = new ServiceCollection();
        new AuditLoggingModule().OnConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        var validators = provider.GetServices<IHostedService>()
            .Where(service => service.GetType().Name == "AuditLoggingAccountabilityCompositionValidator")
            .ToArray();

        validators.Should().ContainSingle();
        var action = () => validators[0].StartAsync(CancellationToken.None);
        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*AUDIT_LOGGING_ACCOUNTABILITY_FOUNDATION_MISSING*");
    }

    [Fact]
    public async Task AuditLoggingModuleRegistersOneValidatorAndPassesWithFoundation()
    {
        var services = new ServiceCollection();
        new AuditLoggingModule().OnConfigureServices(services);
        services.AddAccountability();
        using var provider = services.BuildServiceProvider();
        var validators = provider.GetServices<IHostedService>()
            .Where(service => service.GetType().Name == "AuditLoggingAccountabilityCompositionValidator")
            .ToArray();

        validators.Should().ContainSingle();
        var action = () => validators[0].StartAsync(CancellationToken.None);
        await action.Should().NotThrowAsync();
    }
}
