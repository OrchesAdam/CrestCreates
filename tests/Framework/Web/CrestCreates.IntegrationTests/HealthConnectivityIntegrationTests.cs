using System;
using System.Threading.Tasks;
using CrestCreates.HealthCheck.Mvc.HealthChecks;
using CrestCreates.TestServices;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using StackExchange.Redis;
using Xunit;

namespace CrestCreates.IntegrationTests;

public class HealthConnectivityIntegrationTests
{
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task Database_opens_real_connection_and_detects_stopped_server()
    {
        var server = ExternalTestServices.RequiredConnectionString("CREST_HEALTH_TEST_PG_CONNECTION", "PostgreSQL 17");
        await using var service = await TestServiceControl.AcquirePostgresAsync(DedicatedTestService.HealthPostgres, server);
        var builder = new NpgsqlConnectionStringBuilder(server) { Pooling = false, Timeout = 2 };
        NpgsqlConnection? connection = null;
        var check = new DatabaseHealthCheck(() => connection = new NpgsqlConnection(builder.ConnectionString));
        Assert.Equal(HealthStatus.Healthy, (await check.CheckHealthAsync(new HealthCheckContext())).Status);
        Assert.Equal(System.Data.ConnectionState.Closed, connection!.State);
        try
        {
            await service.StopAsync();
            var result = await check.CheckHealthAsync(new HealthCheckContext());
            Assert.Equal(HealthStatus.Unhealthy, result.Status);
            Assert.Empty(result.Data);
            Assert.Null(result.Exception);
        }
        finally
        {
            await service.RecoverAsync(() => TestServiceControl.WaitPostgresAsync(builder.ConnectionString, ReadinessTimeout));
        }
    }

    [Fact]
    public async Task Redis_pings_real_server_and_detects_stopped_server_without_disposing_connection()
    {
        var endpoint = ExternalTestServices.RequiredConnectionString("CREST_HEALTH_TEST_REDIS_ENDPOINT", "Redis 7");
        var parts = endpoint.Split(':', 2);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
            throw new InvalidOperationException("CREST_HEALTH_TEST_REDIS_ENDPOINT must be a host and port.");

        await using var service = await TestServiceControl.AcquireAsync(DedicatedTestService.HealthRedis, parts[0], port);
        using var connection = await ConnectionMultiplexer.ConnectAsync(new ConfigurationOptions
        {
            EndPoints = { { parts[0], port } },
            AbortOnConnectFail = false,
            AsyncTimeout = 500,
            ConnectTimeout = 2000
        });
        var check = new RedisHealthCheck(connection, TimeSpan.FromSeconds(2));
        Assert.Equal(HealthStatus.Healthy, (await check.CheckHealthAsync(new HealthCheckContext())).Status);
        try
        {
            await service.StopAsync();
            var result = await check.CheckHealthAsync(new HealthCheckContext());
            Assert.Equal(HealthStatus.Unhealthy, result.Status);
            Assert.Empty(result.Data);
            Assert.Null(result.Exception);
        }
        finally
        {
            await service.RecoverAsync(() => TestServiceControl.WaitUntilAsync(async cancellation =>
            {
                try
                {
                    return connection.IsConnected && await connection.GetDatabase().PingAsync().WaitAsync(cancellation) > TimeSpan.Zero;
                }
                catch (RedisException)
                {
                    return false;
                }
            }, ReadinessTimeout, "Redis ping"));
        }

        Assert.Equal(HealthStatus.Healthy, (await check.CheckHealthAsync(new HealthCheckContext())).Status);
    }
}
