using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Accountability.Bootstrap;
using CrestCreates.Accountability.InMemory;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CrestCreates.Accountability.Tests.Bootstrap;

public sealed class AccountabilityCompositionTests
{
    [Fact]
    public async Task AddAccountabilityWithoutSinkIsAllowedWhenRequireSinkFalse()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAccountability();
        using var provider = services.BuildServiceProvider();

        var validator = provider.GetServices<IHostedService>()
            .Single(service => service.GetType().Name == "AccountabilityCompositionValidator");

        await validator.Invoking(service => service.StartAsync(CancellationToken.None))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task RequiredSinkMissingFailsDuringStartup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAccountability(options => options.RequireAtLeastOneSink = true);
        using var provider = services.BuildServiceProvider();

        var validator = provider.GetServices<IHostedService>()
            .Single(service => service.GetType().Name == "AccountabilityCompositionValidator");

        await validator.Invoking(service => service.StartAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ACCOUNTABILITY_SINK_REQUIRED*");
    }

    [Fact]
    public async Task RequiredSinkPassesWhenContractSinkExists()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAccountability(options => options.RequireAtLeastOneSink = true);
        services.AddSingleton<IAuditSink>(new InMemoryAuditSink());
        using var provider = services.BuildServiceProvider();

        var validator = provider.GetServices<IHostedService>()
            .Single(service => service.GetType().Name == "AccountabilityCompositionValidator");

        await validator.Invoking(service => service.StartAsync(CancellationToken.None))
            .Should().NotThrowAsync();
    }
}
