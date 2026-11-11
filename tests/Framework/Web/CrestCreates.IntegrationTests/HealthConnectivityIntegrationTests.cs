using System;
using System.Threading.Tasks;
using CrestCreates.HealthCheck.Mvc.HealthChecks;
using DotNet.Testcontainers.Builders;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Xunit;

namespace CrestCreates.IntegrationTests;

public class HealthConnectivityIntegrationTests
{
    [Fact]
    public async Task Database_opens_real_connection_and_detects_stopped_server()
    {
        await using var server = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await server.StartAsync();
        var builder = new NpgsqlConnectionStringBuilder(server.GetConnectionString()) { Pooling = false, Timeout = 2 };
        NpgsqlConnection? connection = null;
        var check = new DatabaseHealthCheck(() => connection = new NpgsqlConnection(builder.ConnectionString));
        Assert.Equal(HealthStatus.Healthy, (await check.CheckHealthAsync(new HealthCheckContext())).Status);
        Assert.Equal(System.Data.ConnectionState.Closed, connection!.State);
        await server.StopAsync();
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Empty(result.Data);
        Assert.Null(result.Exception);
    }

    [Fact]
    public async Task Redis_pings_real_server_and_detects_stopped_server_without_disposing_connection()
    {
        await using var server = new ContainerBuilder().WithImage("redis:7-alpine")
            .WithPortBinding(6379, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(6379)).Build();
        await server.StartAsync();
        using var connection = await ConnectionMultiplexer.ConnectAsync(new ConfigurationOptions
        {
            EndPoints = { { server.Hostname, server.GetMappedPublicPort(6379) } },
            AbortOnConnectFail = false,
            AsyncTimeout = 500,
            ConnectTimeout = 2000
        });
        var check = new RedisHealthCheck(connection, TimeSpan.FromSeconds(2));
        Assert.Equal(HealthStatus.Healthy, (await check.CheckHealthAsync(new HealthCheckContext())).Status);
        await server.StopAsync();
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Empty(result.Data);
        Assert.Null(result.Exception);
        await server.StartAsync();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!connection.IsConnected && DateTime.UtcNow < deadline)
            await Task.Delay(100);
        Assert.Equal(HealthStatus.Healthy, (await check.CheckHealthAsync(new HealthCheckContext())).Status);
    }
}
