using FluentAssertions;
using Xunit;

namespace CrestCreates.Snapshot.Tests;

public class SnapshotStringDictionaryTests
{
    [Fact]
    public void Returns_New_Dictionary_Instance()
    {
        var source = new Dictionary<string, string> { ["key"] = "value" };

        var snapshot = source.SnapshotStringDictionary();

        snapshot.Should().NotBeSameAs(source);
    }

    [Fact]
    public void Source_Mutation_Does_Not_Affect_Snapshot()
    {
        var source = new Dictionary<string, string> { ["key"] = "value" };

        var snapshot = source.SnapshotStringDictionary();

        source["key"] = "changed";
        source.Add("new", "entry");

        snapshot["key"].Should().Be("value");
        snapshot.Should().HaveCount(1);
    }

    [Fact]
    public void Null_Source_Returns_Empty_Dictionary()
    {
        IReadOnlyDictionary<string, string>? source = null;

        var snapshot = source.SnapshotStringDictionary();

        snapshot.Should().NotBeNull();
        snapshot.Should().BeEmpty();
    }

    [Fact]
    public void Null_Source_Returns_Independent_Instance_Not_Shared_Static()
    {
        IReadOnlyDictionary<string, string>? source1 = null;
        IReadOnlyDictionary<string, string>? source2 = null;

        var snapshot1 = source1.SnapshotStringDictionary();
        var snapshot2 = source2.SnapshotStringDictionary();

        // Each call should return a new independent instance
        snapshot1.Should().NotBeSameAs(snapshot2);
    }

    [Fact]
    public void Empty_Source_Returns_Empty_Dictionary()
    {
        var source = new Dictionary<string, string>();

        var snapshot = source.SnapshotStringDictionary();

        snapshot.Should().NotBeNull();
        snapshot.Should().BeEmpty();
    }

    [Fact]
    public void Empty_Source_Returns_Independent_Instance()
    {
        var source1 = new Dictionary<string, string>();
        var source2 = new Dictionary<string, string>();

        var snapshot1 = source1.SnapshotStringDictionary();
        var snapshot2 = source2.SnapshotStringDictionary();

        snapshot1.Should().NotBeSameAs(snapshot2);
    }

    [Fact]
    public void Deterministic_Ordinal_Comparer()
    {
        var source = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Key"] = "value",
        };

        var snapshot = source.SnapshotStringDictionary();

        // Snapshot uses Ordinal comparer, so case-sensitive lookup should work
        snapshot["Key"].Should().Be("value");
        // Case-insensitive lookup should NOT work (Ordinal is case-sensitive)
        snapshot.ContainsKey("key").Should().BeFalse();
    }

    [Fact]
    public void Deterministic_Enumeration_Order_Regardless_Of_Insertion_Order()
    {
        // Two dictionaries with same content but different insertion order
        var source1 = new Dictionary<string, string>
        {
            ["zebra"] = "z",
            ["apple"] = "a",
            ["mango"] = "m",
        };

        var source2 = new Dictionary<string, string>
        {
            ["mango"] = "m",
            ["zebra"] = "z",
            ["apple"] = "a",
        };

        var snapshot1 = source1.SnapshotStringDictionary();
        var snapshot2 = source2.SnapshotStringDictionary();

        // Both snapshots must enumerate in the same ordinal-sorted order
        var keys1 = snapshot1.Keys.ToList();
        var keys2 = snapshot2.Keys.ToList();

        keys1.Should().Equal(keys2);
        keys1.Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public void Preserves_All_Entries()
    {
        var source = new Dictionary<string, string>
        {
            ["a"] = "1",
            ["b"] = "2",
            ["c"] = "3",
        };

        var snapshot = source.SnapshotStringDictionary();

        snapshot.Should().HaveCount(3);
        snapshot["a"].Should().Be("1");
        snapshot["b"].Should().Be("2");
        snapshot["c"].Should().Be("3");
    }

    [Fact]
    public void Returns_IReadOnlyDictionary()
    {
        var source = new Dictionary<string, string> { ["key"] = "value" };

        IReadOnlyDictionary<string, string> snapshot = source.SnapshotStringDictionary();

        snapshot["key"].Should().Be("value");
    }
}
