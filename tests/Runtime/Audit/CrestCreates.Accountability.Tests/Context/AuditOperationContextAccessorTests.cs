using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Context;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Accountability.Tests.Context;

public sealed class AuditOperationContextAccessorTests
{
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
}
