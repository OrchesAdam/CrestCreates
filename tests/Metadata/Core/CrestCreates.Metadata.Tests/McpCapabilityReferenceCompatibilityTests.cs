using System.Reflection;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.Mcp;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public sealed class McpCapabilityReferenceCompatibilityTests
{
    [Fact]
    public void Mcp_descriptor_mainline_uses_shared_capability_reference()
    {
        typeof(McpToolDescriptor)
            .GetProperty(nameof(McpToolDescriptor.Capability))!
            .PropertyType.Should().Be(typeof(CapabilityProjectionReference));
    }

    [Fact]
    public void Obsolete_wrapper_round_trips_all_fields_through_shared_reference()
    {
#pragma warning disable CS0618 // Compatibility behavior is the subject under test.
        var legacy = new McpCapabilityReference(
            "orders.get",
            7,
            VersionSelectionMode.Exact,
            "sha256:expected");

        CapabilityProjectionReference shared = legacy;
        McpCapabilityReference roundTripped = shared;
#pragma warning restore CS0618

        shared.Should().Be(new CapabilityProjectionReference(
            "orders.get",
            7,
            VersionSelectionMode.Exact,
            "sha256:expected"));
        roundTripped.Id.Should().Be(legacy.Id);
        roundTripped.Version.Should().Be(legacy.Version);
        roundTripped.SelectionMode.Should().Be(legacy.SelectionMode);
        roundTripped.ExpectedContractHash.Should().Be(legacy.ExpectedContractHash);
    }

    [Fact]
    public void Compatibility_wrapper_declares_its_removal_window()
    {
#pragma warning disable CS0618 // Compatibility metadata is the subject under test.
        var obsolete = typeof(McpCapabilityReference).GetCustomAttribute<ObsoleteAttribute>();
#pragma warning restore CS0618

        obsolete.Should().NotBeNull();
        obsolete!.Message.Should().Contain("Phase 8f migration window");
        obsolete.IsError.Should().BeFalse();
    }
}
