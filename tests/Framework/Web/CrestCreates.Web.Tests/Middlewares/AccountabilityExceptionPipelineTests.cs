using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Recording;
using CrestCreates.Accountability.Bootstrap;
using CrestCreates.AspNetCore.Errors;
using CrestCreates.AspNetCore.Middlewares;
using CrestCreates.AuditLogging.Abstractions.MethodAccountability;
using CrestCreates.AuditLogging.Middlewares;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CrestCreates.Web.Tests.Middlewares;

public sealed class AccountabilityExceptionPipelineTests
{
    [Fact]
    public async Task ConvertedInvalidOperationExceptionProducesHttp400Fact()
    {
        await using var harness = await PipelineHarness.StartAsync();

        var response = await harness.GetAsync("/invalid-operation");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        harness.Recorder.Envelope!.Outcome.Should().Be(new AuditOutcome
        {
            Status = "failed",
            Code = "HTTP_400"
        });
    }

    [Fact]
    public async Task ConvertedKeyNotFoundExceptionProducesHttp404Fact()
    {
        await using var harness = await PipelineHarness.StartAsync();

        var response = await harness.GetAsync("/not-found");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        harness.Recorder.Envelope!.Outcome.Should().Be(new AuditOutcome
        {
            Status = "failed",
            Code = "HTTP_404"
        });
    }

    [Fact]
    public async Task ConvertedPermissionExceptionProducesHttp403Fact()
    {
        await using var harness = await PipelineHarness.StartAsync();

        var response = await harness.GetAsync("/forbidden");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        harness.Recorder.Envelope!.Outcome.Should().Be(new AuditOutcome
        {
            Status = "failed",
            Code = "HTTP_403"
        });
    }

    [Fact]
    public async Task UnknownExceptionProducesHttp500Fact()
    {
        await using var harness = await PipelineHarness.StartAsync();

        var response = await harness.GetAsync("/unknown");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        harness.Recorder.Envelope!.Outcome.Should().Be(new AuditOutcome
        {
            Status = "failed",
            Code = "HTTP_500"
        });
    }

    [Fact]
    public async Task AccountabilityFactIsRecordedAfterExceptionConversion()
    {
        await using var harness = await PipelineHarness.StartAsync();

        var response = await harness.GetAsync("/invalid-operation");
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("Crest.Operation.Invalid");
        harness.Recorder.ConversionWasComplete.Should().BeTrue();
        harness.Recorder.ResponseStatusAtRecord.Should().Be(StatusCodes.Status400BadRequest);
        harness.Recorder.Envelope!.OccurredAt.Should().BeOnOrAfter(harness.Conversion.CompletedAt);
    }

    [Fact]
    public async Task ConvertedExceptionPreservesAuthenticatedActorAndChildCorrelation()
    {
        await using var harness = await PipelineHarness.StartAsync();

        var response = await harness.GetAsync("/child-operation", authenticated: true);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        harness.Recorder.Envelope!.Actor.Should().BeEquivalentTo(new AuditActor
        {
            Kind = "user",
            Id = "user-1",
            DisplayName = "user-1"
        });
        harness.Probe.ChildActor.Should().Be(harness.Recorder.Envelope.Actor);
        harness.Probe.ChildCorrelationId.Should().Be(harness.Recorder.Envelope.CorrelationId);
    }

    [Fact]
    public async Task AuditFailureDoesNotReplaceConvertedErrorResponse()
    {
        await using var harness = await PipelineHarness.StartAsync();
        harness.Recorder.Throw = true;

        var response = await harness.GetAsync("/invalid-operation");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        body.Should().Contain("Crest.Operation.Invalid");
    }

    private sealed class PipelineHarness : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _client;

        private PipelineHarness(
            WebApplication app,
            HttpClient client,
            CaptureRecorder recorder,
            ConversionProbe conversion,
            ChildProbe probe)
        {
            _app = app;
            _client = client;
            Recorder = recorder;
            Conversion = conversion;
            Probe = probe;
        }

        public CaptureRecorder Recorder { get; }
        public ConversionProbe Conversion { get; }
        public ChildProbe Probe { get; }

        public static async Task<PipelineHarness> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Testing"
            });
            builder.WebHost.UseTestServer();
            var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
            var conversion = new ConversionProbe();
            var child = new ChildProbe();

            builder.Services.AddLogging();
            builder.Services.AddRouting();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { });
            builder.Services.AddCrestExceptionHandling();
            builder.Services.AddAccountability();
            builder.Services.AddScoped<AccountabilityHttpMiddleware>();
            builder.Services.AddSingleton<TimeProvider>(clock);
            builder.Services.AddSingleton(conversion);
            builder.Services.AddSingleton(child);
            builder.Services.AddSingleton<ICurrentTenant, TestTenant>();
            builder.Services.AddSingleton<IAuditedMethodAccountabilityRuntime, NoopMethodRuntime>();
            builder.Services.AddSingleton<CaptureRecorder>();
            builder.Services.Replace(ServiceDescriptor.Singleton<IAuditRecorder>(provider =>
                provider.GetRequiredService<CaptureRecorder>()));
            builder.Services.Replace(ServiceDescriptor.Singleton<ICrestExceptionConverter>(provider =>
                new TrackingExceptionConverter(provider, conversion, clock)));

            var app = builder.Build();
            app.UseRouting();
            app.UseAuthentication();
            app.UseAccountabilityHttpAudit();
            app.UseExceptionHandling();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/invalid-operation", _ => Task.FromException(new InvalidOperationException("invalid")));
                endpoints.MapGet("/not-found", _ => Task.FromException(new KeyNotFoundException("missing")));
                endpoints.MapGet("/forbidden", _ => Task.FromException(new CrestPermissionException("orders.read")));
                endpoints.MapGet("/unknown", _ => Task.FromException(new Exception("unknown")));
                endpoints.MapGet("/child-operation", context =>
                {
                    var ambient = context.RequestServices.GetRequiredService<IAuditOperationContextAccessor>().Current;
                    child.ChildCorrelationId = ambient?.CorrelationId;
                    child.ChildActor = ambient?.Actor;
                    return Task.FromException(new InvalidOperationException("child failed"));
                });
            });
            await app.StartAsync();
            return new PipelineHarness(
                app,
                app.GetTestClient(),
                app.Services.GetRequiredService<CaptureRecorder>(),
                conversion,
                child);
        }

        public async Task<HttpResponseMessage> GetAsync(string path, bool authenticated = false)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            if (authenticated)
                request.Headers.Add(TestAuthenticationHandler.UserHeader, "user-1");
            return await _client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _app.DisposeAsync();
        }
    }

    private sealed class CaptureRecorder(
        IHttpContextAccessor httpContextAccessor,
        ConversionProbe conversion) : IAuditRecorder
    {
        public AuditEnvelope? Envelope { get; private set; }
        public int ResponseStatusAtRecord { get; private set; }
        public bool ConversionWasComplete { get; private set; }
        public bool Throw { get; set; }

        public ValueTask<AuditRecordResult> RecordAsync(
            AuditEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            Envelope = envelope;
            ResponseStatusAtRecord = httpContextAccessor.HttpContext?.Response.StatusCode ?? 0;
            ConversionWasComplete = conversion.IsComplete;
            if (Throw)
                throw new InvalidOperationException("audit failed");
            return ValueTask.FromResult(new AuditRecordResult
            {
                AuditId = envelope.AuditId,
                Status = AuditRecordStatus.NoSinkConfigured,
                ProcessedAt = DateTimeOffset.UtcNow
            });
        }
    }

    private sealed class TrackingExceptionConverter : ICrestExceptionConverter
    {
        private readonly DefaultCrestExceptionConverter _inner;
        private readonly ConversionProbe _probe;
        private readonly ManualTimeProvider _clock;

        public TrackingExceptionConverter(
            IServiceProvider services,
            ConversionProbe probe,
            ManualTimeProvider clock)
        {
            _inner = new DefaultCrestExceptionConverter(
                services,
                new CrestExceptionLocalizationResources(
                    new Dictionary<string, IReadOnlyDictionary<string, string>>()),
                NullLogger<DefaultCrestExceptionConverter>.Instance);
            _probe = probe;
            _clock = clock;
        }

        public CrestExceptionConversionResult Convert(HttpContext context, Exception exception)
        {
            var result = _inner.Convert(context, exception);
            _clock.Advance(TimeSpan.FromSeconds(1));
            _probe.CompletedAt = _clock.GetUtcNow();
            _probe.IsComplete = true;
            return result;
        }
    }

    private sealed class ConversionProbe
    {
        public bool IsComplete { get; set; }
        public DateTimeOffset CompletedAt { get; set; }
    }

    private sealed class ChildProbe
    {
        public string? ChildCorrelationId { get; set; }
        public AuditActor? ChildActor { get; set; }
    }

    private sealed class ManualTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _now = initial;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan value) => _now += value;
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "test";
        public const string UserHeader = "X-Test-User";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var userId = Request.Headers[UserHeader].ToString();
            if (string.IsNullOrWhiteSpace(userId))
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId), new Claim(ClaimTypes.Name, userId)],
                SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    private sealed class TestTenant : ICurrentTenant
    {
        public ITenantInfo? TenantInfo => null;
        public ITenantInfo? Tenant => null;
        public string? Id => "tenant-1";
        public Task<IDisposable> ChangeAsync(string tenantId) => Task.FromResult<IDisposable>(new Noop());
        public IDisposable Change(ITenantInfo tenant) => new Noop();
        public void SetTenantId(string tenantId) { }

        private sealed class Noop : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class NoopMethodRuntime : IAuditedMethodAccountabilityRuntime
    {
        public IAuditedMethodInvocationState Enter(AuditedMethodInvocationDescriptor descriptor)
            => throw new NotSupportedException();

        public void SetOutcome(IAuditedMethodInvocationState state, AuditedMethodInvocationOutcome outcome)
            => throw new NotSupportedException();

        public ValueTask ExitAsync(IAuditedMethodInvocationState state)
            => throw new NotSupportedException();
    }
}
