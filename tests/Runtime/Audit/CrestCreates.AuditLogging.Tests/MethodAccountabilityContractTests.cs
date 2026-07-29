using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Identity;
using CrestCreates.Accountability.Abstractions.Recording;
using CrestCreates.Accountability.Context;
using CrestCreates.AuditLogging.Interceptors;
using CrestCreates.AuditLogging.Abstractions.MethodAccountability;
using FluentAssertions;
using Rougamo;
using Rougamo.Metadatas;
using Xunit;

namespace CrestCreates.AuditLogging.Tests;

public sealed class MethodAccountabilityContractTests
{
    [Fact]
    public void AuditedMoAttribute_IsDefinedOnceInAbstractionsAndForwardedByRuntime()
    {
        typeof(AuditedMoAttribute).Assembly.GetName().Name
            .Should().Be("CrestCreates.AuditLogging.Abstractions");

        var legacyAssembly = typeof(CrestCreates.AuditLogging.Modules.AuditLoggingModule).Assembly;
        legacyAssembly.GetForwardedTypes().Should().Contain(typeof(AuditedMoAttribute));
    }

    [Fact]
    public void MethodRuntimeContract_UsesOpaqueTypedStateOnly()
    {
        typeof(IAuditedMethodAccountabilityRuntime).GetMethods()
            .SelectMany(method => method.GetParameters())
            .Where(parameter => parameter.Name == "state")
            .Select(parameter => parameter.ParameterType)
            .Should().OnlyContain(type => type == typeof(IAuditedMethodInvocationState));
    }

    [Fact]
    public void Attribute_ForcesSynchronousEntryToPreserveOperationScope()
    {
        var optimization = typeof(AuditedMoAttribute)
            .GetCustomAttributes(typeof(OptimizationAttribute), inherit: true)
            .Cast<OptimizationAttribute>()
            .Single();

        optimization.ForceSync.Should().HaveFlag(ForceSync.OnEntry);
    }

    [Fact]
    public async Task AuditFailureDoesNotReplaceMethodResult()
    {
        var recorder = new ThrowingRecorder();
        var runtime = new AuditedMethodAccountabilityRuntime(
            recorder,
            new TestContextAccessor(),
            new TestIdentityGenerator());
        var state = runtime.Enter(new AuditedMethodInvocationDescriptor
        {
            MethodId = "tests.method",
            ActionName = "tests.method",
            StartedAt = DateTimeOffset.UtcNow
        });

        runtime.SetOutcome(state, new AuditedMethodInvocationOutcome
        {
            Kind = AuditedMethodOutcomeKind.Succeeded
        });

        var action = async () => await runtime.ExitAsync(state);
        await action.Should().NotThrowAsync();
        recorder.Calls.Should().Be(1);
    }

    [Fact]
    public async Task AuditFailureDoesNotReplaceOriginalMethodException()
    {
        var recorder = new ThrowingRecorder();
        var runtime = new AuditedMethodAccountabilityRuntime(
            recorder,
            new TestContextAccessor(),
            new TestIdentityGenerator());
        var state = runtime.Enter(new AuditedMethodInvocationDescriptor
        {
            MethodId = "tests.method",
            ActionName = "tests.method",
            StartedAt = DateTimeOffset.UtcNow
        });

        runtime.SetOutcome(state, new AuditedMethodInvocationOutcome
        {
            Kind = AuditedMethodOutcomeKind.Failed,
            SafeCode = "METHOD_EXCEPTION"
        });

        var original = new InvalidOperationException("original");
        var action = async () =>
        {
            try
            {
                throw original;
            }
            catch (InvalidOperationException)
            {
                await runtime.ExitAsync(state);
                throw;
            }
        };

        var thrown = await action.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Should().BeSameAs(original);
        recorder.Calls.Should().Be(1);
    }

    [Fact]
    public async Task MethodAuditIdEnclosesNestedFactsWhileParentRemainsHttpFact()
    {
        var recorder = new CapturingRecorder();
        var contexts = new AuditOperationContextAccessor();
        using var httpScope = contexts.Push(new AuditOperationContext
        {
            CorrelationId = "correlation-1",
            OperationId = "http-operation",
            EnclosingAuditId = "http-audit",
            Actor = new AuditActor { Kind = "user", Id = "user-1" },
            InvocationSource = "http"
        });
        var runtime = new AuditedMethodAccountabilityRuntime(recorder, contexts, new TestIdentityGenerator());

        var state = runtime.Enter(new AuditedMethodInvocationDescriptor
        {
            MethodId = "tests.method",
            ActionName = "tests.method",
            StartedAt = DateTimeOffset.UtcNow
        });

        var methodAuditId = contexts.Current!.EnclosingAuditId;
        methodAuditId.Should().StartWith("audit-");
        contexts.Current.OperationId.Should().StartWith("operation-");
        runtime.SetOutcome(state, new AuditedMethodInvocationOutcome { Kind = AuditedMethodOutcomeKind.Succeeded });
        await runtime.ExitAsync(state);

        recorder.Envelope!.AuditId.Should().Be(methodAuditId);
        recorder.Envelope.ParentAuditId.Should().Be("http-audit");
        recorder.Envelope.CausationId.Should().Be("http-operation");
        contexts.Current!.EnclosingAuditId.Should().Be("http-audit");
    }

    private sealed class ThrowingRecorder : IAuditRecorder
    {
        public int Calls { get; private set; }

        public ValueTask<AuditRecordResult> RecordAsync(
            AuditEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("recorder failed");
        }
    }

    private sealed class CapturingRecorder : IAuditRecorder
    {
        public AuditEnvelope? Envelope { get; private set; }

        public ValueTask<AuditRecordResult> RecordAsync(AuditEnvelope envelope, CancellationToken cancellationToken = default)
        {
            Envelope = envelope;
            return ValueTask.FromResult(new AuditRecordResult
            {
                AuditId = envelope.AuditId,
                Status = AuditRecordStatus.Recorded,
                ProcessedAt = DateTimeOffset.UtcNow
            });
        }
    }

    private sealed class TestContextAccessor : IAuditOperationContextAccessor
    {
        public AuditOperationContext? Current => null;

        public IDisposable Push(AuditOperationContext context) => new NoopScope();

        private sealed class NoopScope : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class TestIdentityGenerator : IAuditIdentityGenerator
    {
        private int _sequence;

        public string CreateOperationId() => $"operation-{Interlocked.Increment(ref _sequence)}";

        public string CreateAuditId() => $"audit-{Interlocked.Increment(ref _sequence)}";
    }
}
