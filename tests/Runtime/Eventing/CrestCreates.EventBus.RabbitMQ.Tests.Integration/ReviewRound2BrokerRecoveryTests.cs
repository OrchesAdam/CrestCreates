using System.Reflection;
using CrestCreates.EventBus.RabbitMQ.Connection;
using CrestCreates.RabbitMq.TestHandlersA;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Xunit;
using Xunit.Abstractions;

namespace CrestCreates.EventBus.RabbitMQ.Tests.Integration;

public sealed class ReviewRound2BrokerRecoveryTests(ITestOutputHelper output) : RabbitMqIntegrationTestBase(output)
{
    [Fact]
    public async Task Slow_SDK_topology_recovery_must_not_be_misclassified_as_terminal_channel_loss()
    {
        using var host = BuildHost();
        var pool = host.Services.GetRequiredService<RabbitMqConnectionPool>();
        var factory = (ConnectionFactory)typeof(RabbitMqConnectionPool)
            .GetField("_factory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pool)!;
        using var releaseTopology = new ManualResetEventSlim(false);
        var topologyEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = 0;
        // The SDK opens its replacement connection before recovering recorded exchanges/channels.
        // Hold this documented SDK phase briefly, far below the framework's 30s recovery grace.
        factory.TopologyRecoveryFilter = new TopologyRecoveryFilter {
            ExchangeFilter = _ => {
                if (Interlocked.Exchange(ref first, 1) == 0) {
                    topologyEntered.TrySetResult();
                    releaseTopology.Wait(TimeSpan.FromSeconds(10));
                }
                return true;
            }
        };
        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stopRegistration = host.Services.GetRequiredService<IHostApplicationLifetime>()
            .ApplicationStopping.Register(() => stopping.TrySetResult());
        await host.StartAsync();
        try
        {
            (await WaitUntilAsync(() => QueueExistsAsync(VHost, QueueNames.Orders), TimeSpan.FromSeconds(10))).Should().BeTrue();
            (await WaitUntilAsync(async () => (await ManagementJsonAsync(HttpMethod.Get, "connections"))
                .EnumerateArray().Any(c => c.TryGetProperty("vhost", out var v) && v.GetString() == VHost),
                TimeSpan.FromSeconds(10))).Should().BeTrue();
            await CloseAllConnectionsAsync(VHost);
            (await Task.WhenAny(topologyEntered.Task, Task.Delay(TimeSpan.FromSeconds(10))) == topologyEntered.Task)
                .Should().BeTrue("SDK topology recovery must really be in progress");
            Output.WriteLine($"connection open while topology recovery is held: {pool.IsConnectionOpen}");
            var stopped = await Task.WhenAny(stopping.Task, Task.Delay(TimeSpan.FromSeconds(3))) == stopping.Task;
            Output.WriteLine($"host stopped before topology recovery completed: {stopped}");
            stopped.Should().BeFalse("channel restoration is still in progress inside the SDK, not a terminal loss");
        }
        finally
        {
            releaseTopology.Set();
            await host.StopAsync(TimeSpan.FromSeconds(15));
        }
    }
}
