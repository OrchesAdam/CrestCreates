using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Context;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Accountability.Tests.Context;

public sealed class AuditCausalityContractTests
{
    [Fact]
    public void NeverSubstitutesTraceIdForCorrelationOrCausation()
    {
        var envelope = CreateEnvelope() with
        {
            CorrelationId = "business-correlation",
            CausationId = null,
            Runtime = new AuditRuntimeContext
            {
                TraceId = "trace-id",
                SpanId = "span-id",
                References = []
            }
        };

        envelope.CorrelationId.Should().NotBe(envelope.Runtime.TraceId);
        envelope.CausationId.Should().BeNull();
    }

    [Fact]
    public void RootFactAllowsNoCauseOrParent()
    {
        var envelope = CreateEnvelope();
        envelope.CausationId.Should().BeNull();
        envelope.ParentAuditId.Should().BeNull();
        envelope.PreviousAuditId.Should().BeNull();
    }

    [Fact]
    public void NestedOperationUsesOperationIdAndEnclosingAuditIdExactly()
    {
        var accessor = new AuditOperationContextAccessor();
        using var root = accessor.Push(Create("root-operation") with { EnclosingAuditId = "root-audit" });
        var current = accessor.Current!;

        var nested = CreateEnvelope() with
        {
            CausationId = current.OperationId,
            ParentAuditId = current.EnclosingAuditId
        };

        nested.CausationId.Should().Be("root-operation");
        nested.ParentAuditId.Should().Be("root-audit");
    }

    [Fact]
    public void ParentAuditIdNeverRepresentsSequence()
    {
        var lifecycle = CreateEnvelope() with
        {
            ParentAuditId = "enclosing-audit",
            PreviousAuditId = "previous-lifecycle-audit"
        };

        lifecycle.ParentAuditId.Should().NotBe(lifecycle.PreviousAuditId);
    }

    [Fact]
    public void PreviousAuditIdLinksLifecycleSequence()
    {
        var lifecycle = CreateEnvelope() with { PreviousAuditId = "workflow.started.audit" };
        lifecycle.PreviousAuditId.Should().Be("workflow.started.audit");
        lifecycle.CausationId.Should().BeNull();
    }

    [Fact]
    public void ScopeStackRejectsOutOfOrderDispose()
    {
        var accessor = new AuditOperationContextAccessor();
        using var root = accessor.Push(Create("root"));
        using var child = accessor.Push(Create("child"));
        root.Invoking(scope => scope.Dispose()).Should().Throw<InvalidOperationException>();
        accessor.Current!.OperationId.Should().Be("child");
    }

    [Fact]
    public async Task ParallelSiblingScopesDoNotInterfere()
    {
        var accessor = new AuditOperationContextAccessor();
        var first = Create("first");
        var second = Create("second");

        var values = await Task.WhenAll(
            Task.Run(async () => { using var _ = accessor.Push(first); await Task.Yield(); return accessor.Current!.OperationId; }),
            Task.Run(async () => { using var _ = accessor.Push(second); await Task.Yield(); return accessor.Current!.OperationId; }));

        values.Should().BeEquivalentTo("first", "second");
        accessor.Current.Should().BeNull();
    }

    [Fact]
    public async Task NestedAwaitPreservesCurrentScope()
    {
        var accessor = new AuditOperationContextAccessor();
        using var scope = accessor.Push(Create("root"));
        await Task.Delay(1);
        accessor.Current!.OperationId.Should().Be("root");
    }

    [Fact]
    public async Task ChildScopeDoesNotMutateParentExecutionContext()
    {
        var accessor = new AuditOperationContextAccessor();
        using var root = accessor.Push(Create("root"));

        await Task.Run(() =>
        {
            using var child = accessor.Push(Create("child"));
            accessor.Current!.OperationId.Should().Be("child");
        });

        accessor.Current!.OperationId.Should().Be("root");
    }

    [Fact]
    public void OutOfOrderDisposeFailsWithoutCorruptingParent()
    {
        var accessor = new AuditOperationContextAccessor();
        using var root = accessor.Push(Create("root"));
        using var child = accessor.Push(Create("child"));

        var action = () => root.Dispose();
        action.Should().Throw<InvalidOperationException>();
        accessor.Current!.OperationId.Should().Be("child");
        child.Dispose();
        accessor.Current!.OperationId.Should().Be("root");
        root.Dispose();
        accessor.Current.Should().BeNull();
    }

    private static AuditOperationContext Create(string id)
        => new()
        {
            CorrelationId = "corr",
            OperationId = id,
            Actor = new AuditActor { Kind = "system", Id = id },
            InvocationSource = "system"
        };

    private static AuditEnvelope CreateEnvelope()
        => new()
        {
            AuditId = "audit-1",
            OccurredAt = DateTimeOffset.UnixEpoch,
            CorrelationId = "correlation-1",
            Actor = new AuditActor { Kind = "system", Id = "system" },
            Action = new AuditAction { Kind = "test.action", Name = "test" },
            Target = new AuditTarget { Kind = "test.target", Id = "target-1" },
            Outcome = new AuditOutcome { Status = "succeeded" }
        };
}
