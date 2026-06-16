using CrestCreates.Snapshot.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Snapshot.Tests;

public class ISnapshotableContractTests
{
    /// <summary>
    /// Test model: sealed record implementing ISnapshotable via 'with' expression.
    /// </summary>
    private sealed record TestRecordModel(int Value) : ISnapshotable<TestRecordModel>
    {
        public TestRecordModel Snapshot() => this with { };
    }

    /// <summary>
    /// Test model: mutable class implementing ISnapshotable with manual copy.
    /// </summary>
    private sealed class MutableModel : ISnapshotable<MutableModel>
    {
        public int Value { get; set; }
        public MutableModel Snapshot() => new() { Value = Value };
    }

    /// <summary>
    /// Test model: class with both Clone() and Snapshot() where Clone() delegates to Snapshot().
    /// This verifies the backward-compatibility migration pattern.
    /// </summary>
    private sealed class LegacyBridgeModel : ISnapshotable<LegacyBridgeModel>
    {
        public int Value { get; set; }
        public LegacyBridgeModel Snapshot() => new() { Value = Value };
        public LegacyBridgeModel Clone() => Snapshot();
    }

    [Fact]
    public void Snapshot_Returns_Different_Instance()
    {
        var original = new MutableModel { Value = 42 };
        var snapshot = original.Snapshot();

        snapshot.Should().NotBeSameAs(original);
    }

    [Fact]
    public void Snapshot_Returns_Equivalent_Values()
    {
        var original = new MutableModel { Value = 42 };
        var snapshot = original.Snapshot();

        snapshot.Value.Should().Be(original.Value);
    }

    [Fact]
    public void Snapshot_Isolation_Mutation_Does_Not_Affect_Original()
    {
        var original = new MutableModel { Value = 42 };
        var snapshot = original.Snapshot();

        original.Value = 99;

        snapshot.Value.Should().Be(42);
    }

    [Fact]
    public void Snapshot_Isolation_Snapshot_Mutation_Does_Not_Affect_Original()
    {
        var original = new MutableModel { Value = 42 };
        var snapshot = original.Snapshot();

        snapshot.Value = 99;

        original.Value.Should().Be(42);
    }

    [Fact]
    public void Record_Snapshot_Uses_With_Expression()
    {
        var original = new TestRecordModel(10);
        var snapshot = original.Snapshot();

        snapshot.Should().NotBeSameAs(original);
        snapshot.Value.Should().Be(10);
    }

    [Fact]
    public void Clone_Delegates_To_Snapshot()
    {
        var original = new LegacyBridgeModel { Value = 7 };
        var clone = original.Clone();
        var snapshot = original.Snapshot();

        // Both should produce equivalent but independent copies
        clone.Value.Should().Be(snapshot.Value).And.Be(7);
        clone.Should().NotBeSameAs(snapshot);
        clone.Should().NotBeSameAs(original);
        snapshot.Should().NotBeSameAs(original);
    }

    [Fact]
    public void Clone_Delegates_To_Snapshot_Mutation_Isolation()
    {
        var original = new LegacyBridgeModel { Value = 7 };
        var clone = original.Clone();

        original.Value = 100;

        clone.Value.Should().Be(7);
    }
}
