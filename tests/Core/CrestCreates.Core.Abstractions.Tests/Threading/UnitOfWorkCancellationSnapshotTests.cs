using CrestCreates.Core.Abstractions.Threading;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Core.Abstractions.Tests.Threading;

public class UnitOfWorkCancellationSnapshotTests
{
    [Fact]
    public void Merge_Keeps_Inner_To_Outer_Order_And_Outer_Root()
    {
        using var innerCaller = new CancellationTokenSource();
        using var outerCaller = new CancellationTokenSource();

        var inner = new UnitOfWorkCancellationSnapshot(
            [Frame(innerCaller.Token, depth: 2)]);
        var outer = new UnitOfWorkCancellationSnapshot(
            [Frame(outerCaller.Token, depth: 1)]);

        var merged = inner.Merge(outer);

        merged.Frames.Should().HaveCount(2);
        merged.RootCallerToken.Should().Be(outerCaller.Token);
        merged.CallerToken.Should().Be(innerCaller.Token);
    }

    [Fact]
    public void Merge_Deduplicates_Frames_By_Execution_Token()
    {
        using var caller = new CancellationTokenSource();
        var frame = Frame(caller.Token, depth: 1);
        var snapshot = new UnitOfWorkCancellationSnapshot([frame]);

        var merged = snapshot.Merge(snapshot);

        merged.Frames.Should().ContainSingle();
    }

    [Fact]
    public void Attach_Annotates_Only_Cancellation_Exceptions_And_Merges()
    {
        using var caller = new CancellationTokenSource();
        var snapshot = new UnitOfWorkCancellationSnapshot([Frame(caller.Token, depth: 1)]);
        var exception = new OperationCanceledException(caller.Token);

        UnitOfWorkCancellationSnapshot.Attach(exception, snapshot);
        UnitOfWorkCancellationSnapshot.Attach(exception, snapshot);

        exception.Data[UnitOfWorkCancellationSnapshot.ExceptionDataKey]
            .Should().BeOfType<UnitOfWorkCancellationSnapshot>()
            .Which.Frames.Should().ContainSingle();
    }

    [Fact]
    public void Attach_Ignores_Non_Cancellation_Exceptions()
    {
        using var caller = new CancellationTokenSource();
        var exception = new InvalidOperationException("boom");

        UnitOfWorkCancellationSnapshot.Attach(
            exception,
            new UnitOfWorkCancellationSnapshot([Frame(caller.Token, depth: 1)]));

        exception.Data.Contains(UnitOfWorkCancellationSnapshot.ExceptionDataKey).Should().BeFalse();
    }

    [Fact]
    public void ContainsToken_Matches_Execution_And_Derived_Tokens()
    {
        using var caller = new CancellationTokenSource();
        using var derived = new CancellationTokenSource();
        using var unrelated = new CancellationTokenSource();
        var snapshot = new UnitOfWorkCancellationSnapshot(
            [Frame(caller.Token, depth: 1, derivedToken: derived.Token)]);

        snapshot.ContainsToken(caller.Token).Should().BeTrue();
        snapshot.ContainsToken(derived.Token).Should().BeTrue();
        snapshot.ContainsToken(unrelated.Token).Should().BeFalse();
    }

    private static UnitOfWorkCancellationFrame Frame(
        CancellationToken token, int depth, CancellationToken derivedToken = default)
        => new(
            CallerToken: token,
            ExecutionToken: token,
            CallerTokenWasCancelled: false,
            DeadlineExpired: false,
            DerivedTokens: derivedToken == default ? [] : [derivedToken],
            DepthFromRoot: depth);
}
