using System;
using System.Threading.Tasks;
using CrestCreates.HealthCheck.Mvc.HealthChecks;
using CrestCreates.TestServices;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using StackExchange.Redis;
using Xunit;

namespace CrestCreates.IntegrationTests;

// 故障注入停启的是专属长驻实例（infra/docker-compose.test-services.yml），不得指向共享服务。
public class HealthConnectivityIntegrationTests
{
    [Fact]
    public async Task Database_opens_real_connection_and_detects_stopped_server()
    {
        var server = ExternalTestServices.RequiredConnectionString("CREST_HEALTH_TEST_PG_CONNECTION", "PostgreSQL 17");
        var container = ExternalTestServices.RequiredContainerName("CREST_HEALTH_TEST_PG_CONTAINER", "PostgreSQL 17");
        var builder = new NpgsqlConnectionStringBuilder(server) { Pooling = false, Timeout = 2 };
        NpgsqlConnection? connection = null;
        var check = new DatabaseHealthCheck(() => connection = new NpgsqlConnection(builder.ConnectionString));
        Assert.Equal(HealthStatus.Healthy, (await check.CheckHealthAsync(new HealthCheckContext())).Status);
        Assert.Equal(System.Data.ConnectionState.Closed, connection!.State);
        await TestServiceControl.StopAsync(container);
        try
        {
            var result = await check.CheckHealthAsync(new HealthCheckContext());
            Assert.Equal(HealthStatus.Unhealthy, result.Status);
            Assert.Empty(result.Data);
            Assert.Null(result.Exception);
        }
        finally
        {
            await TestServiceControl.StartAsync(container);
            await TestServiceControl.WaitUntilAsync(async () =>
            {
                try
                {
                    await using var probe = new NpgsqlConnection(builder.ConnectionString);
                    await probe.OpenAsync();
                    return true;
                }
                catch (NpgsqlException)
                {
                    return false;
                }
            }, TimeSpan.FromSeconds(60));
        }
    }

    [Fact]
    public async Task Redis_pings_real_server_and_detects_stopped_server_without_disposing_connection()
    {
        var endpoint = ExternalTestServices.RequiredConnectionString("CREST_HEALTH_TEST_REDIS_ENDPOINT", "Redis 7");
        var container = ExternalTestServices.RequiredContainerName("CREST_HEALTH_TEST_REDIS_CONTAINER", "Redis 7");
        var parts = endpoint.Split(':', 2);
        using var connection = await ConnectionMultiplexer.ConnectAsync(new ConfigurationOptions
        {
            EndPoints = { { parts[0], int.Parse(parts[1]) } },
            AbortOnConnectFail = false,
            AsyncTimeout = 500,
            ConnectTimeout = 2000
        });
        var check = new RedisHealthCheck(connection, TimeSpan.FromSeconds(2));
        Assert.Equal(HealthStatus.Healthy, (await check.CheckHealthAsync(new HealthCheckContext())).Status);
        await TestServiceControl.StopAsync(container);
        try
        {
            var result = await check.CheckHealthAsync(new HealthCheckContext());
            Assert.Equal(HealthStatus.Unhealthy, result.Status);
            Assert.Empty(result.Data);
            Assert.Null(result.Exception);
        }
        finally
        {
            await TestServiceControl.StartAsync(container);
        }

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!connection.IsConnected && DateTime.UtcNow < deadline)
            await Task.Delay(100);
        Assert.Equal(HealthStatus.Healthy, (await check.CheckHealthAsync(new HealthCheckContext())).Status);
    }
}