using CrestCreates.Snapshot.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Snapshot.Tests;

public class SnapshotDictionaryTests
{
    private sealed class MutableModel : ISnapshotable<MutableModel>
    {
        public int Value { get; set; }
        public MutableModel Snapshot() => new() { Value = Value };
    }

    [Fact]
    public void Returns_New_Value_Snapshots_Not_Original_References()
    {
        var source = new Dictionary<string, MutableModel>
        {
            ["a"] = new() { Value = 1 },
            ["b"] = new() { Value = 2 },
        };

        var snapshot = source.SnapshotDictionary();

        snapshot.Should().HaveCount(2);
        snapshot["a"].Should().NotBeSameAs(source["a"]);
        snapshot["b"].Should().NotBeSameAs(source["b"]);
        snapshot["a"].Value.Should().Be(1);
        snapshot["b"].Value.Should().Be(2);
    }

    [Fact]
    public void Source_Mutation_Does_Not_Affect_Snapshot()
    {
        var source = new Dictionary<string, MutableModel>
        {
            ["key"] = new() { Value = 10 },
        };

        var snapshot = source.SnapshotDictionary();

        source["key"].Value = 99;
        source.Add("new", new() { Value = 50 });

        snapshot["key"].Value.Should().Be(10);
        snapshot.Should().HaveCount(1);
    }

    [Fact]
    public void Rejects_Null_Source()
    {
        IReadOnlyDictionary<string, MutableModel> source = null!;

        var act = () => source.SnapshotDictionary();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Custom_Comparer_Is_Used()
    {
        var source = new Dictionary<string, MutableModel>(StringComparer.OrdinalIgnoreCase)
        {
            ["Key"] = new() { Value = 1 },
        };

        var snapshot = source.SnapshotDictionary(StringComparer.OrdinalIgnoreCase);

        // The snapshot should use the custom comparer, so lookup is case-insensitive
        snapshot["key"].Value.Should().Be(1);
        snapshot["KEY"].Value.Should().Be(1);
    }

    [Fact]
    public void Default_Comparer_Is_Used_When_None_Specified()
    {
        var source = new Dictionary<string, MutableModel>
        {
            ["Key"] = new() { Value = 1 },
        };

        var snapshot = source.SnapshotDictionary();

        // Default comparer is case-sensitive
        snapshot["Key"].Value.Should().Be(1);
        snapshot.ContainsKey("key").Should().BeFalse();
    }

    [Fact]
    public void Empty_Source_Returns_Empty_Result()
    {
        var source = new Dictionary<string, MutableModel>();

        var snapshot = source.SnapshotDictionary();

        snapshot.Should().BeEmpty();
    }

    [Fact]
    public void Int_Key_Works_With_Default_Comparer()
    {
        var source = new Dictionary<int, MutableModel>
        {
            [1] = new() { Value = 10 },
            [2] = new() { Value = 20 },
        };

        var snapshot = source.SnapshotDictionary();

        snapshot[1].Value.Should().Be(10);
        snapshot[2].Value.Should().Be(20);
        snapshot[1].Should().NotBeSameAs(source[1]);
    }
}
