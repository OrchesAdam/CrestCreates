using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.DescriptorDraft.Abstractions;

public sealed record DescriptorDraftMaterializationResult
{
    public required bool IsMaterialized { get; init; }
    public required IReadOnlyList<IDescriptor> ProposedInventory { get; init; }
    public required IReadOnlyList<DescriptorDraftDiagnostic> Diagnostics { get; init; }

    public static DescriptorDraftMaterializationResult Success(IReadOnlyList<IDescriptor> proposedInventory)
        => new() { IsMaterialized = true, ProposedInventory = proposedInventory, Diagnostics = Array.Empty<DescriptorDraftDiagnostic>() };

    public static DescriptorDraftMaterializationResult Failure(params DescriptorDraftDiagnostic[] diagnostics)
        => new() { IsMaterialized = false, ProposedInventory = Array.Empty<IDescriptor>(), Diagnostics = diagnostics };
}
