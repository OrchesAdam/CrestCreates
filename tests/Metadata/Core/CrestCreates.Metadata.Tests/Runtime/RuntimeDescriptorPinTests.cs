using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.Runtime;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests.Runtime;

public sealed class RuntimeDescriptorPinTests
{
    [Fact]
    public void RuntimeDescriptorPin_ShouldRequireExactVersion()
    {
        var pin = new RuntimeDescriptorPin
        {
            Ref = new DescriptorRef("workflow", "approval", null),
            ContractHash = Hash("contract", "Contract"),
            DefinitionHash = Hash("definition", "Definition")
        };

        var act = () => pin.EnsureValid();

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RuntimeDescriptorPin_ShouldPreserveStructuredCanonicalHashes()
    {
        var contract = Hash("contract", "Contract");
        var definition = Hash("definition", "Definition");
        var pin = new RuntimeDescriptorPin
        {
            Ref = new DescriptorRef("workflow", "approval", 3),
            ContractHash = contract,
            DefinitionHash = definition
        };

        pin.EnsureValid();
        pin.ContractHash.Should().Be(contract);
        pin.DefinitionHash.Should().Be(definition);
        pin.Ref.Version.Should().Be(3);
    }

    internal static CanonicalHash Hash(string value, string purpose, string? scope = "InternalFull") => new()
    {
        Value = value,
        Algorithm = "SHA-256",
        AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = "Descriptor",
        DescriptorKind = "Workflow",
        Scope = scope!,
        Purpose = purpose,
        ContractVersion = "canonical-hash-v1",
        CanonicalShapeVersion = "workflow-descriptor-v1"
    };
}
