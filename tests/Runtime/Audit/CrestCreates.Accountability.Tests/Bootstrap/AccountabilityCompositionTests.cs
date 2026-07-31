using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Accountability.Bootstrap;
using CrestCreates.Accountability.InMemory;
using CrestCreates.Accountability.Recording;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CrestCreates.Accountability.Tests.Bootstrap;

public sealed class AccountabilityCompositionTests
{
    [Fact]
    public Task ZeroWriteTimeoutFailsStartup()
        => AssertInvalidTimeoutAsync(TimeSpan.Zero);

    [Fact]
    public Task NegativeWriteTimeoutFailsStartup()
        => AssertInvalidTimeoutAsync(TimeSpan.FromMilliseconds(-2));

    [Fact]
    public Task InfiniteWriteTimeoutFailsStartup()
        => AssertInvalidTimeoutAsync(Timeout.InfiniteTimeSpan);

    [Fact]
    public async Task FinitePositiveWriteTimeoutPassesStartup()
    {
        var validator = CreateValidator(options => options.WriteTimeout = TimeSpan.FromSeconds(1));

        await validator.Invoking(service => service.StartAsync(CancellationToken.None))
            .Should().NotThrowAsync();
    }

    [Fact]
    public Task LibraryDefaultDoesNotRequireSink()
        => AddAccountabilityWithoutSinkIsAllowedWhenRequireSinkFalse();

    [Fact]
    public Task FirstPartyProductionHostsRequireAtLeastOneSink()
        => RequiredSinkMissingFailsDuringStartup();

    [Fact]
    public Task DevelopmentHostRegistersInMemorySinkExplicitly()
        => RequiredSinkPassesWhenContractSinkExists();

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

    private static async Task AssertInvalidTimeoutAsync(TimeSpan value)
    {
        var validator = CreateValidator(options => options.WriteTimeout = value);

        await validator.Invoking(service => service.StartAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ACCOUNTABILITY_WRITE_TIMEOUT_INVALID*");
    }

    private static IHostedService CreateValidator(Action<AccountabilityOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAccountability(configure);
        var provider = services.BuildServiceProvider();
        return provider.GetServices<IHostedService>()
            .Single(service => service.GetType().Name == "AccountabilityCompositionValidator");
    }
}
