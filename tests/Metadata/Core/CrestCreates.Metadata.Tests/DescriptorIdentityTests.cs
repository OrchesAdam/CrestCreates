using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorIdentityTests
{
    private class TestDescriptor : IDescriptor
    {
        public string Namespace { get; init; } = "event";
        public string Id { get; init; } = "user.created";
        public string Name { get; init; } = "UserCreated";

        public DescriptorKind Kind => DescriptorKind.Event;
        public DescriptorState State => DescriptorState.Active;
        public string? SupersededById => null;
    }

    [Fact]
    public void FullId_combines_Namespace_and_Id()
    {
        IDescriptor descriptor = new TestDescriptor { Namespace = "event", Id = "user.created" };
        descriptor.FullId.Should().Be("event.user.created");
    }

    [Fact]
    public void FullId_uses_default_interface_implementation()
    {
        IDescriptor descriptor = new TestDescriptor { Namespace = "capability", Id = "approval" };
        descriptor.FullId.Should().Be("capability.approval");
    }
}
