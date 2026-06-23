using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorPackageDiffTests
{
    private readonly ICanonicalHashComputer _hashComputer = new DefaultCanonicalHashComputer();
    private readonly IDescriptorStableHashBuilder _hashBuilder;
    private readonly IDescriptorPackageBuilder _builder;
    private readonly IDescriptorPackageDiffer _differ = new DescriptorPackageDiffer();

    public DescriptorPackageDiffTests()
    {
        _hashBuilder = new DescriptorStableHashBuilder(_hashComputer);
        _builder = new DefaultDescriptorPackageBuilder(_hashBuilder);
    }

    private DescriptorPackage BuildPackage(string pkgId, IDescriptor[] descriptors, string version = "1.0.0")
    {
        return _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = pkgId, PackageVersion = version, Descriptors = descriptors
        });
    }

    private static SchemaDescriptor MakeSchema(string id, int version, string name)
    {
        return new SchemaDescriptor
        {
            Id = id, Version = version, Name = name, State = DescriptorState.Active
        };
    }

    [Fact]
    public void Diff_AddedRef_ProducesAddedEntry()
    {
        var pkg1 = BuildPackage("pkg", new IDescriptor[] { MakeSchema("a", 1, "A") });
        var pkg2 = BuildPackage("pkg", new IDescriptor[] { MakeSchema("a", 1, "A"), MakeSchema("b", 1, "B") });
        var diff = _differ.Diff(pkg1, pkg2);
        diff.AddedRefs.Should().ContainSingle(r => r.Id == "b");
        diff.RemovedRefs.Should().BeEmpty();
    }

    [Fact]
    public void Diff_RemovedRef_ProducesRemovedEntry()
    {
        var pkg1 = BuildPackage("pkg", new IDescriptor[] { MakeSchema("a", 1, "A"), MakeSchema("b", 1, "B") });
        var pkg2 = BuildPackage("pkg", new IDescriptor[] { MakeSchema("a", 1, "A") });
        var diff = _differ.Diff(pkg1, pkg2);
        diff.RemovedRefs.Should().ContainSingle(r => r.Id == "b");
        diff.AddedRefs.Should().BeEmpty();
    }

    [Fact]
    public void Diff_ChangedDescriptorHash_ProducesChangedEntry()
    {
        var desc1a = new SchemaDescriptor { Id = "a", Version = 1, Name = "A_V1", State = DescriptorState.Active };
        var desc1b = new SchemaDescriptor { Id = "a", Version = 1, Name = "A_V2", State = DescriptorState.Active };
        var pkg1 = BuildPackage("pkg", new IDescriptor[] { desc1a });
        var pkg2 = BuildPackage("pkg", new IDescriptor[] { desc1b });
        var diff = _differ.Diff(pkg1, pkg2);
        diff.ChangedEntries.Should().ContainSingle(e => e.Ref.Id == "a");
        diff.ChangedEntries[0].BeforeContractHash.Should().NotBeNullOrEmpty();
        diff.ChangedEntries[0].AfterContractHash.Should().NotBeNullOrEmpty();
        diff.ChangedEntries[0].BeforeContractHash.Should().NotBe(diff.ChangedEntries[0].AfterContractHash);
    }

    [Fact]
    public void Diff_StateChange_ProducesStateChangeEntry()
    {
        var active = new SchemaDescriptor { Id = "a", Version = 1, Name = "A", State = DescriptorState.Active };
        var deprecated = new SchemaDescriptor { Id = "a", Version = 1, Name = "A", State = DescriptorState.Deprecated };
        var pkg1 = BuildPackage("pkg", new IDescriptor[] { active });
        var pkg2 = BuildPackage("pkg", new IDescriptor[] { deprecated });
        var diff = _differ.Diff(pkg1, pkg2);
        diff.StateChanges.Should().ContainSingle(s =>
            s.Ref.Id == "a" && s.FromState == DescriptorState.Active && s.ToState == DescriptorState.Deprecated);
    }

    [Fact]
    public void Diff_MetadataChange_ProducesMetadataChange()
    {
        var pkg1 = BuildPackage("pkg", new IDescriptor[] { MakeSchema("a", 1, "A") }, "1.0.0");
        var pkg2 = BuildPackage("pkg", new IDescriptor[] { MakeSchema("a", 1, "A") }, "2.0.0");
        var diff = _differ.Diff(pkg1, pkg2);
        diff.MetadataChanges.Should().Contain(m =>
            m.Field == "PackageVersion" && m.BeforeValue == "1.0.0" && m.AfterValue == "2.0.0");
    }

    [Fact]
    public void Diff_IdenticalPackages_ProducesEmptyDiff()
    {
        var pkg1 = BuildPackage("pkg", new IDescriptor[] { MakeSchema("a", 1, "A") });
        var pkg2 = BuildPackage("pkg", new IDescriptor[] { MakeSchema("a", 1, "A") });
        var diff = _differ.Diff(pkg1, pkg2);
        diff.AddedRefs.Should().BeEmpty();
        diff.RemovedRefs.Should().BeEmpty();
        diff.ChangedEntries.Should().BeEmpty();
        diff.StateChanges.Should().BeEmpty();
        diff.MetadataChanges.Should().BeEmpty();
    }

    [Fact]
    public void Diff_DoesNotRunImpactOrCompatibilityAnalysis()
    {
        var pkg1 = BuildPackage("pkg", new IDescriptor[] { MakeSchema("a", 1, "A") });
        var pkg2 = BuildPackage("pkg", new IDescriptor[] { MakeSchema("b", 1, "B") });
        var diff = _differ.Diff(pkg1, pkg2);
        diff.AddedRefs.Should().ContainSingle(r => r.Id == "b");
        diff.RemovedRefs.Should().ContainSingle(r => r.Id == "a");
    }

    [Fact]
    public void Diff_MetadataChanges_UsesStrongTypedRecords()
    {
        var pkg1 = BuildPackage("pkg", new IDescriptor[] { MakeSchema("a", 1, "A") }, "1.0.0");
        var pkg2 = BuildPackage("pkg", new IDescriptor[] { MakeSchema("a", 1, "A") }, "2.0.0");
        var diff = _differ.Diff(pkg1, pkg2);
        diff.MetadataChanges.Should().AllBeOfType<DescriptorPackageMetadataChange>();
        diff.MetadataChanges[0].Field.Should().Be("PackageVersion");
    }
}
