using System.Net.Sockets;
using CrestCreates.EventBus.RabbitMQ.Connection;
using CrestCreates.EventBus.RabbitMQ.Options;
using CrestCreates.EventBus.RabbitMQ.Publishing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CrestCreates.EventBus.RabbitMQ.Tests.Integration;

public abstract class RabbitMqIntegrationTestBase : IAsyncLifetime
{
    protected ServiceProvider ServiceProvider { get; private set; } = null!;
    protected RabbitMqOptions Options { get; private set; } = new();

    public virtual async Task InitializeAsync()
    {
        if (!await IsRabbitMqAvailable())
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "RabbitMQ is not available. Start it with: docker compose -f infra/docker-compose.eventbus.yml up -d");
        }

        Options = new RabbitMqOptions
        {
            HostName = "localhost",
            Port = 5672,
            UserName = "guest",
            Password = "guest",
            VirtualHost = "/",
            RetryCount = 2,
            RetryDelaySeconds = 1,
            DeadLetterExchange = "crestcreates.test.dlx",
            DefaultExchange = "crestcreates.test.events",
            MaxChannels = 5,
            PublisherConfirmTimeoutSeconds = 30
        };

        var services = new ServiceCollection();
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(Options));
        services.AddSingleton<RabbitMqConnectionPool>();
        services.AddSingleton<RabbitMqPublisher>();
        services.AddSingleton<RabbitMqEventBus>();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        ServiceProvider = services.BuildServiceProvider();
    }

    public virtual async Task DisposeAsync()
    {
        if (ServiceProvider is not null)
        {
            await ServiceProvider.DisposeAsync();
        }
    }

    private static async Task<bool> IsRabbitMqAvailable()
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("localhost", 5672);
            return true;
        }
        catch
        {
            return false;
        }
    }
}