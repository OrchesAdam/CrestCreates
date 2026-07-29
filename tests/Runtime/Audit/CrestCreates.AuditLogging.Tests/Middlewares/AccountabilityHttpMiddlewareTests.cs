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
        recorder.Envelope.Target.Id.Should().Be("/api/orders");
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

    private static AccountabilityHttpMiddleware CreateMiddleware(
        IAuditRecorder recorder,
        IAuditedMethodAccountabilityRuntime? runtime = null)
        => new(
            recorder,
            new FixedIdentity(),
            new TenantContext(),
            NullLogger<AccountabilityHttpMiddleware>.Instance,
            new AuditOperationContextAccessor(),
            runtime ?? new TestMethodRuntime());

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
            return ValueTask.FromResult(new AuditRecordResult { AuditId = envelope.AuditId, Status = AuditRecordStatus.Recorded, ProcessedAt = DateTimeOffset.UtcNow });
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
}
