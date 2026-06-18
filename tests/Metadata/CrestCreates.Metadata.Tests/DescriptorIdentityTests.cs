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

        // Existing IDescriptor members (kept for backward compatibility)
        public DescriptorKind Kind => DescriptorKind.Event;
        public DescriptorState State => DescriptorState.Active;
        public string ContractHash => string.Empty;
        public string DefinitionHash => string.Empty;
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

    [Fact]
    public void IHasContractIdentity_provides_hashes()
    {
        IHasContractIdentity descriptor = new TestContractDescriptor();
        descriptor.ContractHash.Should().Be("abc123");
        descriptor.DefinitionHash.Should().Be("def456");
    }

    private class TestContractDescriptor : IDescriptor, IHasContractIdentity
    {
        public string Namespace => "event";
        public string Id => "test";
        public string Name => "Test";

        // Existing IDescriptor members
        public DescriptorKind Kind => DescriptorKind.Event;
        public DescriptorState State => DescriptorState.Active;
        string IHasContractIdentity.ContractHash => "abc123";
        string IHasContractIdentity.DefinitionHash => "def456";
        string? IDescriptor.SupersededById => null;

        // Bridge IDescriptor's ContractHash/DefinitionHash to IHasContractIdentity
        string IDescriptor.ContractHash => ((IHasContractIdentity)this).ContractHash;
        string IDescriptor.DefinitionHash => ((IHasContractIdentity)this).DefinitionHash;
    }

}
