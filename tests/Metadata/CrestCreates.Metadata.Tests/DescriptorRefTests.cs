using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorRefTests
{
    // ── Generic DescriptorRef<TDescriptor> ──

    [Fact]
    public void DescriptorRef_Records_Id()
    {
        var ref1 = new DescriptorRef<SchemaDescriptor>("schema_01");
        var ref2 = new DescriptorRef<SchemaDescriptor>("schema_01");

        ref1.Id.Should().Be("schema_01");
        ref1.Should().Be(ref2);
    }

    [Fact]
    public void VersionedDescriptorRef_Records_Id_And_Version()
    {
        var vref = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 3);

        vref.Id.Should().Be("schema_01");
        vref.Version.Should().Be(3);
    }

    [Fact]
    public void VersionedDescriptorRef_Default_SelectionMode_Is_Exact()
    {
        var vref = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 3);

        vref.SelectionMode.Should().Be(VersionSelectionMode.Exact);
    }

    [Fact]
    public void VersionedDescriptorRef_With_Same_Id_Version_Are_Equal()
    {
        var vref1 = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 3);
        var vref2 = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 3);

        vref1.Should().Be(vref2);
    }

    [Fact]
    public void VersionedDescriptorRef_With_Different_Version_Are_Not_Equal()
    {
        var vref1 = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 3);
        var vref2 = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 4);

        vref1.Should().NotBe(vref2);
    }

    // ── Non-generic DescriptorRef (Task 2) ──

    [Fact]
    public void DescriptorRef_with_version_creates_correctly()
    {
        var r = new DescriptorRef("event", "user.created", 2);
        r.Namespace.Should().Be("event");
        r.Id.Should().Be("user.created");
        r.Version.Should().Be(2);
    }

    [Fact]
    public void DescriptorRef_null_version_means_latest()
    {
        var r = new DescriptorRef("event", "user.created");
        r.Version.Should().BeNull();
    }

    [Fact]
    public void DescriptorRef_FullId_combines_namespace_and_id()
    {
        var r = new DescriptorRef("capability", "approval");
        r.FullId.Should().Be("capability.approval");
    }

    [Fact]
    public void DescriptorRef_is_IDescriptorRef()
    {
        IDescriptorRef r = new DescriptorRef("event", "test", 1);
        r.Id.Should().Be("test");
        r.Version.Should().Be(1);
    }

    // ── DescriptorKey ──

    [Fact]
    public void DescriptorKey_requires_version()
    {
        var k = new DescriptorKey("event", "user.created", 1);
        k.Namespace.Should().Be("event");
        k.Id.Should().Be("user.created");
        k.Version.Should().Be(1);
    }

    // ── ValidationReport ──

    [Fact]
    public void ValidationReport_aggregates_issues()
    {
        var report = ValidationReport.FromIssues(
            new ValidationIssue(ValidationSeverity.Error, "Duplicate name"),
            new ValidationIssue(ValidationSeverity.Warning, "Missing description"));

        report.HasErrors.Should().BeTrue();
        report.HasWarnings.Should().BeTrue();
        report.Issues.Should().HaveCount(2);
    }

    [Fact]
    public void ValidationReport_empty_has_no_errors()
    {
        ValidationReport.Empty.HasErrors.Should().BeFalse();
        ValidationReport.Empty.HasWarnings.Should().BeFalse();
    }
}
