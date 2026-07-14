using CrestCreates.DescriptorDraft;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using FluentAssertions;
using Xunit;

namespace CrestCreates.DescriptorDraft.Tests;

public class CapabilityDescriptorDraftPayloadTests
{
    [Fact]
    public void Snapshot_PreservesProjectionKind()
    {
        // Arrange
        var original = new CapabilityDescriptor
        {
            Id = "cap1",
            Name = "Test Capability",
            ProjectionKind = CapabilityProjectionKind.AppServiceCompatibility
        };
        var payload = new CapabilityDescriptorDraftPayload(original);

        // Act
        var snapshot = payload.Snapshot();

        // Assert
        var snapshotDescriptor = snapshot.Should().BeOfType<CapabilityDescriptorDraftPayload>()
            .Subject.Descriptor;
        snapshotDescriptor.ProjectionKind.Should().Be(CapabilityProjectionKind.AppServiceCompatibility);
    }

    [Fact]
    public void Snapshot_PreservesNativeProjectionKind()
    {
        // Arrange
        var original = new CapabilityDescriptor
        {
            Id = "cap2",
            Name = "Native Capability",
            ProjectionKind = CapabilityProjectionKind.Native
        };
        var payload = new CapabilityDescriptorDraftPayload(original);

        // Act
        var snapshot = payload.Snapshot();

        // Assert
        var snapshotDescriptor = snapshot.Should().BeOfType<CapabilityDescriptorDraftPayload>()
            .Subject.Descriptor;
        snapshotDescriptor.ProjectionKind.Should().Be(CapabilityProjectionKind.Native);
    }
}
