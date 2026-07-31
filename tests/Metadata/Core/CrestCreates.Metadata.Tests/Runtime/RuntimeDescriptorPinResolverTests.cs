using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Metadata.Tests.Runtime;
using CrestCreates.Metadata.Runtime;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Metadata.Tests.Runtime;

public sealed class RuntimeDescriptorPinResolverTests
{
    private readonly WorkflowDescriptor _descriptor = new()
    {
        Id = "approval",
        Name = "approval",
        Version = 3
    };

    [Fact]
    public void RuntimeDescriptorPinResolver_ShouldReturnValidatedDescriptorObject()
    {
        var hashes = Hashes();
        var resolver = CreateResolver(hashes);
        var captured = resolver.Capture(_descriptor);

        var resolved = resolver.Resolve(captured.Pin);

        resolved.Descriptor.Should().BeSameAs(_descriptor);
        resolved.Pin.Should().Be(captured.Pin);
    }

    [Fact]
    public void RuntimeDescriptorPinResolver_ShouldRejectMissingDescriptor()
    {
        var resolver = CreateResolver(Hashes(), missing: true);
        var pin = Pin(Hashes());

        var act = () => resolver.Resolve(pin);

        act.Should().Throw<RuntimeDescriptorPinValidationException>();
    }

    [Fact]
    public void RuntimeDescriptorPinResolver_ShouldRejectContractHashMismatch()
    {
        var resolver = CreateResolver(Hashes());
        var pin = Pin(Hashes(contractValue: "different"));

        var act = () => resolver.Resolve(pin);

        act.Should().Throw<RuntimeDescriptorPinValidationException>();
    }

    [Fact]
    public void RuntimeDescriptorPinResolver_ShouldRejectDefinitionHashMismatch()
    {
        var resolver = CreateResolver(Hashes());
        var pin = Pin(Hashes(definitionValue: "different"));

        var act = () => resolver.Resolve(pin);

        act.Should().Throw<RuntimeDescriptorPinValidationException>();
    }

    [Fact]
    public void RuntimeDescriptorPinResolver_ShouldRejectHashProfileMismatch()
    {
        var resolver = CreateResolver(Hashes());
        var pin = Pin(Hashes(contractPurpose: "WrongPurpose"));

        var act = () => resolver.Resolve(pin);

        act.Should().Throw<RuntimeDescriptorPinValidationException>();
    }

    [Fact]
    public void DescriptorPinWithoutSnapshotId_ShouldResolveFromRegistry()
    {
        var hashes = Hashes();
        var resolver = CreateResolver(hashes);
        var pin = Pin(hashes);

        resolver.Resolve(pin).Descriptor.Should().BeSameAs(_descriptor);
        pin.SnapshotId.Should().BeNull();
    }

    private RuntimeDescriptorPinResolver<WorkflowDescriptor> CreateResolver(
        DescriptorStableHashes hashes,
        WorkflowDescriptor? descriptor = null,
        bool missing = false)
    {
        var registry = new Mock<IVersionedDescriptorRegistry<WorkflowDescriptor>>();
        registry
            .Setup(x => x.GetByVersion("approval", 3))
            .Returns(descriptor ?? _descriptor);
        if (missing)
            registry.Setup(x => x.GetByVersion("approval", 3)).Returns((WorkflowDescriptor?)null);

        var hashBuilder = new Mock<IDescriptorStableHashBuilder>();
        hashBuilder.Setup(x => x.Build(_descriptor)).Returns(hashes);
        return new RuntimeDescriptorPinResolver<WorkflowDescriptor>(
            registry.Object,
            hashBuilder.Object,
            "workflow",
            DescriptorKind.Workflow);
    }

    private RuntimeDescriptorPin Pin(DescriptorStableHashes hashes) => new()
    {
        Ref = new DescriptorRef("workflow", "approval", 3),
        ContractHash = hashes.ContractHash,
        DefinitionHash = hashes.DefinitionHash
    };

    private static DescriptorStableHashes Hashes(
        string contractValue = "contract",
        string definitionValue = "definition",
        string contractPurpose = "Contract")
        => new()
        {
            ContractHash = RuntimeDescriptorPinTests.Hash(contractValue, contractPurpose),
            DefinitionHash = RuntimeDescriptorPinTests.Hash(definitionValue, "Definition")
        };
}
