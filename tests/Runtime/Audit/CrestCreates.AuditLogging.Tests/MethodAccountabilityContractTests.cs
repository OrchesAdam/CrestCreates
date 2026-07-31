using System;
using System.Collections.Generic;
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

    [Fact]
    public async Task EmitsOneFactPerInvocation()
    {
        var recorder = new CapturingRecorder();
        var runtime = new AuditedMethodAccountabilityRuntime(
            recorder,
            new AuditOperationContextAccessor(),
            new TestIdentityGenerator());
        var state = runtime.Enter(Descriptor("tests.one"));
        runtime.SetOutcome(state, new AuditedMethodInvocationOutcome { Kind = AuditedMethodOutcomeKind.Succeeded });
        await runtime.ExitAsync(state);
        recorder.Envelopes.Should().ContainSingle();
    }

    [Fact]
    public Task LinksToHttpOrEnclosingMethodScope()
        => MethodAuditIdEnclosesNestedFactsWhileParentRemainsHttpFact();

    [Fact]
    public async Task MultipleMethodsNeverOverwrite()
    {
        var recorder = new CapturingRecorder();
        var runtime = new AuditedMethodAccountabilityRuntime(
            recorder,
            new AuditOperationContextAccessor(),
            new TestIdentityGenerator());
        foreach (var method in new[] { "tests.one", "tests.two" })
        {
            var state = runtime.Enter(Descriptor(method));
            runtime.SetOutcome(state, new AuditedMethodInvocationOutcome { Kind = AuditedMethodOutcomeKind.Succeeded });
            await runtime.ExitAsync(state);
        }

        recorder.Envelopes.Select(x => x.Target.Id).Should().Equal("tests.one", "tests.two");
        recorder.Envelopes.Select(x => x.AuditId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task StandaloneMethodCreatesRootFact()
    {
        var recorder = new CapturingRecorder();
        var runtime = new AuditedMethodAccountabilityRuntime(
            recorder,
            new AuditOperationContextAccessor(),
            new TestIdentityGenerator());
        var state = runtime.Enter(Descriptor("tests.root"));
        runtime.SetOutcome(state, new AuditedMethodInvocationOutcome { Kind = AuditedMethodOutcomeKind.Succeeded });
        await runtime.ExitAsync(state);

        recorder.Envelope!.CausationId.Should().BeNull();
        recorder.Envelope.ParentAuditId.Should().BeNull();
        recorder.Envelope.CorrelationId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void DoesNotReflectionSerializeArgumentsOrResult()
    {
        typeof(AuditedMethodInvocationDescriptor).GetProperties()
            .Should().NotContain(property => property.PropertyType == typeof(object));
        typeof(AuditedMethodInvocationOutcome).GetProperties()
            .Should().NotContain(property => property.PropertyType == typeof(object)
                || typeof(Exception).IsAssignableFrom(property.PropertyType));
    }

    [Fact]
    public async Task AlwaysDisposesScope()
    {
        var recorder = new CapturingRecorder();
        var contexts = new AuditOperationContextAccessor();
        var runtime = new AuditedMethodAccountabilityRuntime(recorder, contexts, new TestIdentityGenerator());
        var state = runtime.Enter(Descriptor("tests.scope"));
        contexts.Current.Should().NotBeNull();
        runtime.SetOutcome(state, new AuditedMethodInvocationOutcome { Kind = AuditedMethodOutcomeKind.Succeeded });
        await runtime.ExitAsync(state);
        contexts.Current.Should().BeNull();
    }

    private static AuditedMethodInvocationDescriptor Descriptor(string methodId)
        => new()
        {
            MethodId = methodId,
            ActionName = methodId,
            StartedAt = DateTimeOffset.UtcNow
        };

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
        public List<AuditEnvelope> Envelopes { get; } = [];
        public AuditEnvelope? Envelope => Envelopes.LastOrDefault();

        public ValueTask<AuditRecordResult> RecordAsync(AuditEnvelope envelope, CancellationToken cancellationToken = default)
        {
            Envelopes.Add(envelope);
            return ValueTask.FromResult(TestAuditRecordResults.Accepted(envelope.AuditId));
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
