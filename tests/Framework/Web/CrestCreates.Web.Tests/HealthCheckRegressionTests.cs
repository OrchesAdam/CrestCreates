using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Extensions.Logging;
using CrestCreates.HealthCheck.Mvc.HealthChecks;
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

    [Theory]
    [InlineData("Database", false, 17601, "DATABASE_PROBE_FAILED")]
    [InlineData("Redis", false, 17602, "REDIS_PROBE_FAILED")]
    [InlineData("Tenant", false, 17603, "TENANT_PROBE_FAILED")]
    [InlineData("Database", true, 17601, "DATABASE_PROBE_FAILED")]
    [InlineData("Redis", true, 17602, "REDIS_PROBE_FAILED")]
    [InlineData("Tenant", true, 17603, "TENANT_PROBE_FAILED")]
    public async Task Failing_probe_http_emits_safe_diagnostic(string probe, bool mvc, int eventId, string eventName)
    {
        var logs = new ProbeLogs();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.AddProvider(logs);
        builder.Services.AddHealthChecks().Add(new HealthCheckRegistration(probe,
            provider => CreateProbe(provider, probe, "failure"), null, null));
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
            using var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            using var response = await client.GetAsync("/health");
            Assert.Equal(503, (int)response.StatusCode);
            var json = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("SECRET", json);
            Assert.DoesNotContain("InvalidOperationException", json);
            Assert.DoesNotContain("PROVIDER_FAILURE", json);
            using var document = JsonDocument.Parse(json);
            var entry = document.RootElement.GetProperty("checks")[0];
            Assert.Equal(probe, entry.GetProperty("name").GetString());
            Assert.Equal("HEALTH_CHECK_UNHEALTHY", entry.GetProperty("code").GetString());
            foreach (var member in new[] { "description", "exception", "data" })
                Assert.True(!entry.TryGetProperty(member, out var value) || value.ValueKind == JsonValueKind.Null);
            AssertDiagnostic(logs, probe, eventId, eventName, "PROVIDER_FAILURE", "InvalidOperationException");
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Theory]
    [InlineData("Database", 17601, "DATABASE_PROBE_FAILED", "TaskCanceledException", "DEADLINE_EXCEEDED")]
    [InlineData("Redis", 17602, "REDIS_PROBE_FAILED", "TimeoutException", "TIMEOUT")]
    public async Task Probe_timeout_has_controlled_classification(string probe, int eventId, string eventName,
        string exceptionType, string failureCode)
    {
        var logs = new ProbeLogs();
        using var provider = new ServiceCollection().AddLogging(b => b.AddProvider(logs)).BuildServiceProvider();
        var result = await CreateProbe(provider, probe, "timeout").CheckHealthAsync(new HealthCheckContext())
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Null(result.Exception);
        Assert.Empty(result.Data);
        AssertDiagnostic(logs, probe, eventId, eventName, failureCode, exceptionType);
    }

    [Theory]
    [InlineData("Database")]
    [InlineData("Redis")]
    [InlineData("Tenant")]
    public async Task Successful_and_caller_cancelled_probes_emit_no_failure_diagnostic(string probe)
    {
        var logs = new ProbeLogs();
        using var provider = new ServiceCollection().AddLogging(b => b.AddProvider(logs)).BuildServiceProvider();
        Assert.Equal(HealthStatus.Healthy,
            (await CreateProbe(provider, probe, "success").CheckHealthAsync(new HealthCheckContext())).Status);
        using var cancellation = new CancellationTokenSource();
        var pending = CreateProbe(provider, probe, "cancel").CheckHealthAsync(new HealthCheckContext(), cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Empty(logs.Entries);
    }

    [Theory]
    [InlineData("Database", 17601, "DATABASE_PROBE_FAILED", "ArgumentException", "CONFIGURATION_FAILURE")]
    [InlineData("Redis", 17602, "REDIS_PROBE_FAILED", "RedisConnectionException", "CONNECTION_FAILURE")]
    [InlineData("Tenant", 17603, "TENANT_PROBE_FAILED", "InvalidOperationException", "CONFIGURATION_FAILURE")]
    public async Task Probe_known_failure_has_controlled_classification(string probe, int eventId, string eventName,
        string exceptionType, string failureCode)
    {
        var logs = new ProbeLogs();
        using var provider = new ServiceCollection().AddLogging(b => b.AddProvider(logs)).BuildServiceProvider();
        var result = await CreateProbe(provider, probe, "known").CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Null(result.Exception);
        Assert.Empty(result.Data);
        AssertDiagnostic(logs, probe, eventId, eventName, failureCode, exceptionType);
    }

    private static IHealthCheck CreateProbe(IServiceProvider provider, string probe, string behavior)
    {
        async Task Execute(CancellationToken token)
        {
            if (behavior is "timeout" or "cancel")
                await Task.Delay(Timeout.Infinite, token);
            if (behavior == "failure")
                throw new InvalidOperationException("SECRET-password=connection-config", new Exception("SECRET-inner"));
        }

        if (probe == "Database")
        {
            var connection = new Mock<System.Data.Common.DbConnection>();
            connection.Setup(c => c.OpenAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken token) => Execute(token));
            connection.Setup(c => c.DisposeAsync()).Returns(ValueTask.CompletedTask);
            Func<System.Data.Common.DbConnection> factory = () => behavior == "known"
                ? throw new ArgumentException("SECRET-connection-config") : connection.Object;
            return ActivatorUtilities.CreateInstance<DatabaseHealthCheck>(provider, factory,
                behavior == "timeout" ? TimeSpan.FromMilliseconds(30) : TimeSpan.FromSeconds(5));
        }
        if (probe == "Redis")
        {
            var database = new Mock<StackExchange.Redis.IDatabase>();
            database.Setup(d => d.PingAsync(StackExchange.Redis.CommandFlags.None)).Returns(async () =>
            {
                if (behavior == "known")
                    throw new StackExchange.Redis.RedisConnectionException(StackExchange.Redis.ConnectionFailureType.UnableToConnect,
                        "SECRET-redis-endpoint");
                await Execute(CancellationToken.None);
                return TimeSpan.Zero;
            });
            var connection = new Mock<StackExchange.Redis.IConnectionMultiplexer>();
            connection.Setup(c => c.GetDatabase(-1, null)).Returns(database.Object);
            return ActivatorUtilities.CreateInstance<RedisHealthCheck>(provider, connection.Object,
                behavior == "timeout" ? TimeSpan.FromMilliseconds(30) : TimeSpan.FromSeconds(5));
        }
        var repository = new Mock<ITenantRepository>();
        repository.Setup(r => r.AnyAsync(It.IsAny<CancellationToken>())).Returns(async (CancellationToken token) =>
        {
            await Execute(token);
            return false;
        });
        // Keep the repository scope separate from the host while exercising the probe's actual scope resolution.
        var tenantProvider = new ServiceCollection();
        if (behavior != "known")
            tenantProvider.AddSingleton(repository.Object);
        return new OwnedTenantProbe(provider, tenantProvider);
    }

    private sealed class OwnedTenantProbe(IServiceProvider loggerProvider, IServiceCollection services) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            await using var provider = services.BuildServiceProvider();
            var probe = ActivatorUtilities.CreateInstance<TenantHealthCheck>(loggerProvider, provider);
            return await probe.CheckHealthAsync(context, cancellationToken);
        }
    }

    private static void AssertDiagnostic(ProbeLogs logs, string probe, int eventId, string eventName,
        string failureCode, string exceptionType)
    {
        var entry = Assert.Single(logs.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(eventId, entry.Event.Id);
        Assert.Equal(eventName, entry.Event.Name);
        Assert.Null(entry.Exception);
        Assert.Equal(probe, entry.State["Probe"]);
        Assert.Equal(failureCode, entry.State["FailureCode"]);
        Assert.Equal(exceptionType, entry.State["ExceptionType"]);
        Assert.DoesNotContain("SECRET", entry.Message + JsonSerializer.Serialize(entry.State));
        Assert.All(entry.State.Values, value => Assert.IsType<string>(value));
    }

    private sealed class ProbeLogs : ILoggerProvider
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new ProbeLogger(this,
            categoryName == typeof(DatabaseHealthCheck).FullName || categoryName == typeof(RedisHealthCheck).FullName ||
            categoryName == typeof(TenantHealthCheck).FullName);
        public void Dispose() { }

        private sealed class ProbeLogger(ProbeLogs owner, bool capture) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => capture;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (capture)
                    owner.Entries.Enqueue(new LogEntry(logLevel, eventId, exception, formatter(state, exception),
                        ((IEnumerable<KeyValuePair<string, object?>>)(object)state!).ToDictionary(p => p.Key, p => p.Value)));
            }
        }
    }

    private sealed record LogEntry(LogLevel Level, EventId Event, Exception? Exception, string Message,
        Dictionary<string, object?> State);

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
