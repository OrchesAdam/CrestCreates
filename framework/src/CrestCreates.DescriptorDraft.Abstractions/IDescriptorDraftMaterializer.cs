using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.DescriptorDraft.Abstractions;

public interface IDescriptorDraftMaterializer
{
    DescriptorDraftMaterializationResult Materialize(
        DescriptorDraft draft,
        IReadOnlyList<IDescriptor> currentInventory);
}
