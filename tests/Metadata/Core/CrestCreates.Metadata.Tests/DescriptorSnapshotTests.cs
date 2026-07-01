using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorSnapshotTests
{
    [Fact]
    public void TakeSnapshot_Captures_All_Descriptors()
    {
        var hashBuilder = new DescriptorStableHashBuilder(new DefaultCanonicalHashComputer());
        var registry = new GlobalDescriptorRegistry();
        registry.Register(new SchemaDescriptor { Id = "schema_01", Name = "CustomerInput", Version = 1 });
        registry.Register(new SchemaDescriptor { Id = "schema_02", Name = "OrderInput", Version = 1 });

#pragma warning disable CS0618
        var snapshot = DescriptorSnapshotBuilder.TakeSnapshot(registry, "CrestCreates.CRM", "1.0.0", hashBuilder);
#pragma warning restore CS0618

        snapshot.Descriptors.Should().HaveCount(2);
        snapshot.PackageId.Should().Be("CrestCreates.CRM");
        snapshot.PackageVersion.Should().Be("1.0.0");
        snapshot.SnapshotId.Should().StartWith("snapshot_");
    }
}