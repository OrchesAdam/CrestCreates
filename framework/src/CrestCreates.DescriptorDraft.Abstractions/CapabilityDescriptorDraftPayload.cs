using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.DescriptorDraft.Abstractions;

/// <summary>
/// Carries a proposed Capability descriptor.
/// The descriptor type is hosted in the capability abstraction assembly so this
/// payload stays on the abstraction side of the boundary.
/// </summary>
public sealed record CapabilityDescriptorDraftPayload(
    CapabilityDescriptor Descriptor
) : DescriptorDraftPayload
{
    public override DescriptorKind DescriptorKind => DescriptorKind.Capability;
    public override IDescriptor GetDescriptor() => Descriptor;
    public override DescriptorDraftPayload CreateClone() => this with
    {
        Descriptor = new CapabilityDescriptor
        {
            Id = Descriptor.Id,
            Name = Descriptor.Name,
            State = Descriptor.State,
            SupersededById = Descriptor.SupersededById,
            ContractHash = Descriptor.ContractHash,
            DefinitionHash = Descriptor.DefinitionHash,
            Version = Descriptor.Version,
            CapabilityKind = Descriptor.CapabilityKind,
            InputSchema = Descriptor.InputSchema,
            OutputSchema = Descriptor.OutputSchema,
            Categories = Descriptor.Categories.ToArray(),
            Produces = Descriptor.Produces.ToArray(),
            Consumes = Descriptor.Consumes.ToArray(),
            SemanticTags = Descriptor.SemanticTags.ToArray(),
            Permissions = Descriptor.Permissions.ToArray(),
            RiskLevel = Descriptor.RiskLevel
        }
    };
}
