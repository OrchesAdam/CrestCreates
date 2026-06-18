using CrestCreates.Snapshot.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Snapshot.Tests;

public class SnapshotListTests
{
    private sealed class MutableModel : ISnapshotable<MutableModel>
    {
        public int Value { get; set; }
        public MutableModel Snapshot() => new() { Value = Value };
    }

    [Fact]
    public void Returns_New_Item_Snapshots_Not_Original_References()
    {
        var items = new List<MutableModel>
        {
            new() { Value = 1 },
            new() { Value = 2 },
            new() { Value = 3 },
        };

        var snapshot = items.SnapshotList();

        snapshot.Should().HaveCount(3);
        snapshot[0].Should().NotBeSameAs(items[0]);
        snapshot[1].Should().NotBeSameAs(items[1]);
        snapshot[2].Should().NotBeSameAs(items[2]);
        snapshot[0].Value.Should().Be(1);
        snapshot[1].Value.Should().Be(2);
        snapshot[2].Value.Should().Be(3);
    }

    [Fact]
    public void Source_Mutation_Does_Not_Affect_Snapshot()
    {
        var items = new List<MutableModel>
        {
            new() { Value = 10 },
        };

        var snapshot = items.SnapshotList();

        items[0].Value = 99;
        items.Add(new() { Value = 50 });

        snapshot.Should().HaveCount(1);
        snapshot[0].Value.Should().Be(10);
    }

    [Fact]
    public void Rejects_Null_Source()
    {
        IEnumerable<MutableModel> source = null!;

        var act = () => source.SnapshotList();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Empty_Source_Returns_Empty_Result()
    {
        var items = new List<MutableModel>();

        var snapshot = items.SnapshotList();

        snapshot.Should().BeEmpty();
    }

    [Fact]
    public void Returns_IReadOnlyList()
    {
        var items = new List<MutableModel> { new() { Value = 1 } };

        IReadOnlyList<MutableModel> snapshot = items.SnapshotList();

        snapshot.Should().HaveCount(1);
    }
}
