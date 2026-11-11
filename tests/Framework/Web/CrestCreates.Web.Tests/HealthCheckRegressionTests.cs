using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.HealthCheck;
using CrestCreates.HealthCheck.Mvc.Controllers;
using CrestCreates.Domain.Repositories.Permission;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Xunit;

namespace CrestCreates.Web.Tests;

public class HealthCheckRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Public_response_does_not_disclose_check_details(bool tagged)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthChecks().AddCheck("probe", () => HealthCheckResult.Unhealthy(
            "SECRET-description", new Exception("SECRET-exception"),
            new Dictionary<string, object> { ["connectionString"] = "SECRET-password" }), tags: new[] { "infra" });
        using var provider = services.BuildServiceProvider();
        var controller = new HealthController(provider.GetRequiredService<HealthCheckService>());
        var result = (ObjectResult)(tagged ? await controller.GetHealthByTag("infra") : await controller.GetHealth());
        var json = JsonSerializer.Serialize(result.Value);
        Assert.DoesNotContain("SECRET", json);
        Assert.Equal(503, result.StatusCode);
    }

    [Fact]
    public void Mainline_projection_drops_all_untrusted_details()
    {
        var report = new HealthReport(new Dictionary<string, HealthReportEntry>
        {
            ["probe"] = new HealthReportEntry(HealthStatus.Degraded, "SECRET", TimeSpan.Zero,
                new Exception("SECRET"), new Dictionary<string, object> { ["secret"] = "SECRET" })
        }, TimeSpan.Zero);
        var response = CrestCreates.HealthCheck.AspNetCore.Modules.HealthReportResponseMapper.FromHealthReport(report);
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(response));
        Assert.Equal("HEALTH_CHECK_DEGRADED", response.Checks[0].Code);
    }

    [Fact]
    public async Task Redis_timeout_and_cancellation_preserve_connection_ownership()
    {
        var database = new Mock<StackExchange.Redis.IDatabase>(MockBehavior.Strict);
        database.Setup(d => d.PingAsync(StackExchange.Redis.CommandFlags.None))
            .Returns(new TaskCompletionSource<TimeSpan>().Task);
        var connection = new Mock<StackExchange.Redis.IConnectionMultiplexer>(MockBehavior.Strict);
        connection.Setup(c => c.GetDatabase(-1, null)).Returns(database.Object);
        var check = new CrestCreates.HealthCheck.Mvc.HealthChecks.RedisHealthCheck(connection.Object, TimeSpan.FromMilliseconds(20));
        Assert.Equal(HealthStatus.Unhealthy, (await check.CheckHealthAsync(new HealthCheckContext())).Status);
        using var cancellation = new CancellationTokenSource();
        var pending = check.CheckHealthAsync(new HealthCheckContext(), cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        connection.Verify(c => c.GetDatabase(-1, null), Times.Exactly(2));
        database.Verify(d => d.PingAsync(StackExchange.Redis.CommandFlags.None), Times.Exactly(2));
        connection.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Database_disposes_owned_connection_on_success_or_failure(bool fails)
    {
        var connection = new Mock<System.Data.Common.DbConnection>();
        connection.Setup(c => c.OpenAsync(It.IsAny<CancellationToken>()))
            .Returns(fails ? Task.FromException(new Exception("SECRET")) : Task.CompletedTask);
        connection.Setup(c => c.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var check = new CrestCreates.HealthCheck.Mvc.HealthChecks.DatabaseHealthCheck(() => connection.Object);
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(fails ? HealthStatus.Unhealthy : HealthStatus.Healthy, result.Status);
        Assert.Null(result.Exception);
        Assert.Empty(result.Data);
        connection.Verify(c => c.OpenAsync(It.IsAny<CancellationToken>()), Times.Once);
        connection.Verify(c => c.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task Database_cancellation_reaches_driver_and_disposes_connection()
    {
        using var cancellation = new CancellationTokenSource();
        var connection = new Mock<System.Data.Common.DbConnection>();
        connection.Setup(c => c.OpenAsync(It.IsAny<CancellationToken>())).Returns(async (CancellationToken token) =>
        {
            Assert.True(token.CanBeCanceled);
            Assert.NotEqual(cancellation.Token, token);
            cancellation.Cancel();
            await Task.Delay(Timeout.Infinite, token);
        });
        connection.Setup(c => c.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var check = new CrestCreates.HealthCheck.Mvc.HealthChecks.DatabaseHealthCheck(() => connection.Object);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check.CheckHealthAsync(new HealthCheckContext(), cancellation.Token));
        connection.Verify(c => c.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task Database_internal_deadline_cancels_stalled_open_and_disposes_connection()
    {
        CancellationToken driverToken = default;
        var connection = new Mock<System.Data.Common.DbConnection>();
        connection.Setup(c => c.OpenAsync(It.IsAny<CancellationToken>())).Returns(async (CancellationToken token) =>
        {
            driverToken = token;
            await Task.Delay(Timeout.Infinite, token);
        });
        connection.Setup(c => c.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var check = new CrestCreates.HealthCheck.Mvc.HealthChecks.DatabaseHealthCheck(
            () => connection.Object, TimeSpan.FromMilliseconds(30));
        var result = await check.CheckHealthAsync(new HealthCheckContext()).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(driverToken.IsCancellationRequested);
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("DATABASE_UNREACHABLE", result.Description);
        Assert.Null(result.Exception);
        Assert.Empty(result.Data);
        connection.Verify(c => c.DisposeAsync(), Times.Once);
    }

    [Theory]
    [InlineData(HealthStatus.Healthy, false)]
    [InlineData(HealthStatus.Degraded, false)]
    [InlineData(HealthStatus.Unhealthy, false)]
    [InlineData(HealthStatus.Healthy, true)]
    [InlineData(HealthStatus.Degraded, true)]
    [InlineData(HealthStatus.Unhealthy, true)]
    public async Task Loopback_health_response_sanitizes_all_statuses(HealthStatus status, bool mvc)
    {
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddHealthChecks().AddCheck("probe", () => new HealthCheckResult(status,
            "SECRET-description", new Exception("SECRET-exception"),
            new Dictionary<string, object> { ["password"] = "SECRET-password" }), tags: new[] { "infra" });
        if (mvc)
            builder.Services.AddControllers().AddApplicationPart(typeof(HealthController).Assembly);
        await using var app = builder.Build();
        if (mvc)
            app.MapControllers();
        else
            CrestCreates.HealthCheck.AspNetCore.HealthCheckAspNetCoreEndpointRouteBuilderExtensions.MapCrestHealthChecks(app);
        await app.StartAsync();
        try
        {
            using var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(System.Linq.Enumerable.Single(app.Urls)) };
            using var response = await client.GetAsync("/health");
            Assert.Equal(status == HealthStatus.Healthy ? 200 : 503, (int)response.StatusCode);
            var json = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("SECRET", json);
            using var document = JsonDocument.Parse(json);
            Assert.Equal(status.ToString(), document.RootElement.GetProperty("status").GetString());
            var probe = document.RootElement.GetProperty("checks")[0];
            Assert.Equal("probe", probe.GetProperty("name").GetString());
            Assert.Equal("HEALTH_CHECK_" + status.ToString().ToUpperInvariant(), probe.GetProperty("code").GetString());
            foreach (var member in new[] { "description", "exception", "data" })
                Assert.True(!probe.TryGetProperty(member, out var value) || value.ValueKind == JsonValueKind.Null);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Tenant_probe_uses_only_aggregate_even_for_empty_directory()
    {
        var repository = new Mock<ITenantRepository>(MockBehavior.Strict);
        repository.Setup(r => r.AnyAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var services = new ServiceCollection().AddSingleton(repository.Object);
        using var provider = services.BuildServiceProvider();
        var result = await new TenantHealthCheck(provider).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Empty(result.Data);
        repository.Verify(r => r.AnyAsync(It.IsAny<CancellationToken>()), Times.Once);
        repository.VerifyNoOtherCalls();
    }
}
