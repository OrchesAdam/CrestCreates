using System.Collections.Generic;
using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Identity;
using CrestCreates.Accountability.Abstractions.Recording;
using CrestCreates.Accountability.Context;
using CrestCreates.AuditLogging.Middlewares;
using CrestCreates.AuditLogging.Abstractions.MethodAccountability;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CrestCreates.AuditLogging.Tests.Middlewares;

public sealed class AccountabilityHttpMiddlewareTests
{
    [Fact]
    public async Task EmitsSafeSuccessFactWithoutBody()
    {
        var recorder = new CaptureRecorder();
        var middleware = CreateMiddleware(recorder);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/orders";
        context.Response.StatusCode = StatusCodes.Status201Created;

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        recorder.Envelope.Should().NotBeNull();
        recorder.Envelope!.Action.Kind.Should().Be("http.request");
        recorder.Envelope.Outcome.Status.Should().Be("succeeded");
        recorder.Envelope.Target.Id.Should().Be("POST <unmatched>");
        recorder.Envelope.Payload.Should().BeNull();
        recorder.Envelope.DataSnapshot.Should().BeNull();
    }

    [Fact]
    public async Task OriginalExceptionIsPreservedWhenRecordingFails()
    {
        var recorder = new CaptureRecorder { Throw = true };
        var middleware = CreateMiddleware(recorder);
        var context = new DefaultHttpContext();
        var expected = new InvalidOperationException("boom");

        var action = () => middleware.InvokeAsync(context, _ => Task.FromException(expected));

        (await action.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(expected);
        recorder.Envelope.Should().NotBeNull();
        recorder.Envelope!.Outcome.Status.Should().Be("failed");
    }

    [Fact]
    public async Task ProvidesMethodRuntimeOnlyInsideRequestScope()
    {
        var runtime = new TestMethodRuntime();
        var middleware = CreateMiddleware(new CaptureRecorder(), runtime);
        var context = new DefaultHttpContext();

        AuditedMethodAccountabilityRuntimeContext.Current.Should().BeNull();
        await middleware.InvokeAsync(context, _ =>
        {
            AuditedMethodAccountabilityRuntimeContext.Current.Should().BeSameAs(runtime);
            return Task.CompletedTask;
        });
        AuditedMethodAccountabilityRuntimeContext.Current.Should().BeNull();
    }

    [Fact]
    public async Task UnmatchedRouteNeverPersistsRawPath()
    {
        var recorder = new CaptureRecorder();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/reset-password/signed-secret";
        context.Request.QueryString = new QueryString("?token=also-secret");

        await CreateMiddleware(recorder).InvokeAsync(context, _ => Task.CompletedTask);

        recorder.Envelope!.Action.Name.Should().Be("GET <unmatched>");
        recorder.Envelope.Target.Id.Should().Be("GET <unmatched>");
        recorder.Envelope.ToString().Should().NotContain("signed-secret").And.NotContain("also-secret");
    }

    [Fact]
    public async Task SameRouteDifferentMethodsHaveDifferentTargets()
    {
        var getRecorder = new CaptureRecorder();
        var postRecorder = new CaptureRecorder();
        var get = CreateRoutedContext(HttpMethods.Get, "/orders/{id}", "/orders/secret-id");
        var post = CreateRoutedContext(HttpMethods.Post, "/orders/{id}", "/orders/secret-id");

        await CreateMiddleware(getRecorder).InvokeAsync(get, _ => Task.CompletedTask);
        await CreateMiddleware(postRecorder).InvokeAsync(post, _ => Task.CompletedTask);

        getRecorder.Envelope!.Target.Id.Should().Be("GET /orders/{id}");
        postRecorder.Envelope!.Target.Id.Should().Be("POST /orders/{id}");
        getRecorder.Envelope.Target.Id.Should().NotBe(postRecorder.Envelope.Target.Id);
        getRecorder.Envelope.ToString().Should().NotContain("secret-id");
        postRecorder.Envelope.ToString().Should().NotContain("secret-id");
    }

    [Fact]
    public Task EmitsSucceededForCompletedSuccess() => EmitsSafeSuccessFactWithoutBody();

    [Fact]
    public async Task NoBuiltInHttpRejectedPathWithoutTypedFirstPartyProducer()
    {
        var recorder = new CaptureRecorder();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Response.StatusCode = StatusCodes.Status403Forbidden;

        await CreateMiddleware(recorder).InvokeAsync(context, _ => Task.CompletedTask);

        recorder.Envelope!.Outcome.Should().Be(new AuditOutcome { Status = "failed", Code = "HTTP_403" });
    }

    [Fact]
    public async Task Generic4xxIsFailedWithStableStatusCode()
    {
        var recorder = new CaptureRecorder();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await CreateMiddleware(recorder).InvokeAsync(context, _ => Task.CompletedTask);
        recorder.Envelope!.Outcome.Should().Be(new AuditOutcome { Status = "failed", Code = "HTTP_404" });
    }

    [Fact]
    public async Task FiveHundredWithoutExceptionIsFailed()
    {
        var recorder = new CaptureRecorder();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await CreateMiddleware(recorder).InvokeAsync(context, _ => Task.CompletedTask);
        recorder.Envelope!.Outcome.Should().Be(new AuditOutcome { Status = "failed", Code = "HTTP_503" });
    }

    [Fact]
    public async Task RequestAbortIsCancelled()
    {
        var recorder = new CaptureRecorder();
        var context = new DefaultHttpContext();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        context.RequestAborted = cancellation.Token;
        await CreateMiddleware(recorder).InvokeAsync(context, _ => Task.CompletedTask);
        recorder.Envelope!.Outcome.Status.Should().Be("cancelled");
    }

    [Fact]
    public async Task HttpOccurredAtIsTerminalObservationTime()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var recorder = new CaptureRecorder();
        var context = new DefaultHttpContext();
        await CreateMiddleware(recorder, timeProvider: clock).InvokeAsync(context, _ =>
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            return Task.CompletedTask;
        });
        recorder.Envelope!.OccurredAt.Should().Be(DateTimeOffset.UnixEpoch.AddMinutes(1));
    }

    [Fact]
    public async Task UsesRouteTemplateNotDisplayUrlOrQuery()
    {
        var recorder = new CaptureRecorder();
        var context = CreateRoutedContext(HttpMethods.Get, "/users/{id}", "/users/secret");
        context.Request.QueryString = new QueryString("?signature=secret");
        await CreateMiddleware(recorder).InvokeAsync(context, _ => Task.CompletedTask);
        recorder.Envelope!.Target.Id.Should().Be("GET /users/{id}");
        recorder.Envelope.ToString().Should().NotContain("secret");
    }

    [Fact]
    public async Task SeparatesRequestIdTraceIdSpanId()
    {
        using var activity = new System.Diagnostics.Activity("test").Start();
        var recorder = new CaptureRecorder();
        var context = new DefaultHttpContext { TraceIdentifier = "request-id" };
        await CreateMiddleware(recorder).InvokeAsync(context, _ => Task.CompletedTask);
        recorder.Envelope!.Runtime.RequestId.Should().Be("request-id");
        recorder.Envelope.Runtime.TraceId.Should().Be(activity!.TraceId.ToString());
        recorder.Envelope.Runtime.SpanId.Should().Be(activity.SpanId.ToString());
    }

    [Fact]
    public async Task DoesNotCaptureBodyHeadersIpOrUserAgentByDefault()
    {
        var recorder = new CaptureRecorder();
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer secret";
        context.Request.Headers.UserAgent = "secret-agent";
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        context.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("secret-body"));
        await CreateMiddleware(recorder).InvokeAsync(context, _ => Task.CompletedTask);
        recorder.Envelope!.Payload.Should().BeNull();
        recorder.Envelope.DataSnapshot.Should().BeNull();
        recorder.Envelope.ToString().Should().NotContain("secret");
    }

    [Fact]
    public async Task PreservesTenantActorAndCorrelation()
    {
        var recorder = new CaptureRecorder();
        var context = new DefaultHttpContext();
        var identity = new System.Security.Claims.ClaimsIdentity("test");
        identity.AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "user-1"));
        context.User = new System.Security.Claims.ClaimsPrincipal(identity);
        await CreateMiddleware(recorder).InvokeAsync(context, _ => Task.CompletedTask);
        recorder.Envelope!.TenantId.Should().Be("tenant-1");
        recorder.Envelope.Actor.Should().Be(new AuditActor { Kind = "user", Id = "user-1" });
        recorder.Envelope.CorrelationId.Should().StartWith("operation-");
    }

    [Fact]
    public Task AuditFailureDoesNotReplaceHttpOutcome() => OriginalExceptionIsPreservedWhenRecordingFails();

    private static DefaultHttpContext CreateRoutedContext(string method, string template, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(template),
            order: 0,
            new EndpointMetadataCollection(),
            template));
        return context;
    }

    private static TestHttpAccountabilityPipeline CreateMiddleware(
        IAuditRecorder recorder,
        IAuditedMethodAccountabilityRuntime? runtime = null,
        TimeProvider? timeProvider = null)
        => new(
            new AccountabilityHttpTerminalObserverMiddleware(
                recorder,
                new FixedIdentity(),
                NullLogger<AccountabilityHttpTerminalObserverMiddleware>.Instance,
                timeProvider),
            new AccountabilityHttpOperationScopeMiddleware(
                new TenantContext(),
                new AuditOperationContextAccessor(),
                runtime ?? new TestMethodRuntime()));

    private sealed class TestHttpAccountabilityPipeline(
        AccountabilityHttpTerminalObserverMiddleware terminalObserver,
        AccountabilityHttpOperationScopeMiddleware operationScope)
    {
        public Task InvokeAsync(HttpContext context, RequestDelegate next)
            => terminalObserver.InvokeAsync(
                context,
                innerContext => operationScope.InvokeAsync(innerContext, next));
    }

    private sealed class TestMethodRuntime : IAuditedMethodAccountabilityRuntime
    {
        public IAuditedMethodInvocationState Enter(AuditedMethodInvocationDescriptor descriptor)
            => throw new NotSupportedException();

        public void SetOutcome(IAuditedMethodInvocationState state, AuditedMethodInvocationOutcome outcome)
            => throw new NotSupportedException();

        public ValueTask ExitAsync(IAuditedMethodInvocationState state)
            => throw new NotSupportedException();
    }

    private sealed class CaptureRecorder : IAuditRecorder
    {
        public AuditEnvelope? Envelope { get; private set; }
        public bool Throw { get; init; }
        public ValueTask<AuditRecordResult> RecordAsync(AuditEnvelope envelope, CancellationToken cancellationToken = default)
        {
            Envelope = envelope;
            if (Throw) throw new InvalidOperationException("recorder");
            return ValueTask.FromResult(TestAuditRecordResults.Accepted(envelope.AuditId));
        }
    }

    private sealed class FixedIdentity : IAuditIdentityGenerator
    {
        private int _counter;
        public string CreateAuditId() => "audit-" + Interlocked.Increment(ref _counter);
        public string CreateOperationId() => "operation-" + Interlocked.Increment(ref _counter);
    }

    private sealed class TenantContext : ICurrentTenant
    {
        public ITenantInfo? TenantInfo => null;
        public ITenantInfo? Tenant => TenantInfo;
        public string? Id => "tenant-1";
        public Task<IDisposable> ChangeAsync(string tenantId) => Task.FromResult<IDisposable>(new Noop());
        public IDisposable Change(ITenantInfo tenant) => new Noop();
        public void SetTenantId(string tenantId) { }
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    private sealed class ManualTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _now = initial;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan value) => _now += value;
    }
}
