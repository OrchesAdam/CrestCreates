using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.DescriptorDraft;

public sealed class DefaultDescriptorDraftMaterializer : IDescriptorDraftMaterializer
{
    public DescriptorDraftMaterializationResult Materialize(
        Draft draft,
        IReadOnlyList<IDescriptor> currentInventory)
    {
        var proposed = new List<IDescriptor>(currentInventory);
        var proposedDescriptor = draft.Payload.GetDescriptor();

        return draft.Operation switch
        {
            DescriptorDraftOperation.Create => MaterializeCreate(proposed, draft, proposedDescriptor),
            DescriptorDraftOperation.Update => MaterializeUpdate(proposed, draft, proposedDescriptor),
            DescriptorDraftOperation.Deprecate => Unsupported("Deprecate", draft.DraftId),
            DescriptorDraftOperation.Remove => Unsupported("Remove", draft.DraftId),
            _ => DescriptorDraftMaterializationResult.Failure(
                Diag("UNKNOWN_OPERATION", DescriptorDraftDiagnosticSeverity.Error,
                    $"Unknown operation: {draft.Operation}", draft.DraftId))
        };
    }

    private static DescriptorDraftMaterializationResult MaterializeCreate(
        List<IDescriptor> proposed, Draft draft, IDescriptor proposedDescriptor)
    {
        var proposedVersion = (proposedDescriptor as IVersionedDescriptor)?.Version;
        var duplicate = proposed.FirstOrDefault(d =>
            d.Kind == proposedDescriptor.Kind &&
            d.Id == proposedDescriptor.Id &&
            (d as IVersionedDescriptor)?.Version == proposedVersion);

        if (duplicate is not null)
            return DescriptorDraftMaterializationResult.Failure(
                Diag("CREATE_DESCRIPTOR_EXISTS", DescriptorDraftDiagnosticSeverity.Error,
                    $"Descriptor {proposedDescriptor.Kind}/{proposedDescriptor.Id} v{proposedVersion} already exists.", draft.DraftId));

        proposed.Add(proposedDescriptor);
        return DescriptorDraftMaterializationResult.Success(proposed.AsReadOnly());
    }

    private static DescriptorDraftMaterializationResult MaterializeUpdate(
        List<IDescriptor> proposed, Draft draft, IDescriptor proposedDescriptor)
    {
        var baseVersion = ParseVersion(draft.BaseVersion, "BaseVersion");
        if (baseVersion is null && draft.Operation == DescriptorDraftOperation.Update)
            return DescriptorDraftMaterializationResult.Failure(
                Diag("UPDATE_BASE_VERSION_INVALID", DescriptorDraftDiagnosticSeverity.Error,
                    $"BaseVersion '{draft.BaseVersion}' cannot be parsed as a version number.", draft.DraftId));

        var index = proposed.FindIndex(d =>
            d.Kind == draft.DescriptorKind &&
            d.Id == draft.DescriptorId &&
            (d as IVersionedDescriptor)?.Version == baseVersion);

        if (index < 0)
            return DescriptorDraftMaterializationResult.Failure(
                Diag("UPDATE_BASE_NOT_FOUND", DescriptorDraftDiagnosticSeverity.Error,
                    $"Base descriptor {draft.DescriptorKind}/{draft.DescriptorId} v{baseVersion} not found.", draft.DraftId));

        var proposedVersion = (proposedDescriptor as IVersionedDescriptor)?.Version;
        for (var i = 0; i < proposed.Count; i++)
        {
            if (i == index)
                continue;

            var existing = proposed[i];
            if (existing.Kind == proposedDescriptor.Kind &&
                existing.Id == proposedDescriptor.Id &&
                (existing as IVersionedDescriptor)?.Version == proposedVersion)
            {
                return DescriptorDraftMaterializationResult.Failure(
                    Diag("UPDATE_DESCRIPTOR_EXISTS", DescriptorDraftDiagnosticSeverity.Error,
                        $"Descriptor {proposedDescriptor.Kind}/{proposedDescriptor.Id} v{proposedVersion} already exists.", draft.DraftId));
            }
        }

        proposed[index] = proposedDescriptor;
        return DescriptorDraftMaterializationResult.Success(proposed.AsReadOnly());
    }

    private static int? ParseVersion(string? version, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;
        return int.TryParse(version, out var v) ? v : null;
    }

    private static DescriptorDraftMaterializationResult Unsupported(string operation, string? draftId)
        => DescriptorDraftMaterializationResult.Failure(
            Diag("UNSUPPORTED_OPERATION", DescriptorDraftDiagnosticSeverity.Error,
                $"{operation} materialization is not supported.", draftId));

    private static DescriptorDraftDiagnostic Diag(string code, DescriptorDraftDiagnosticSeverity severity,
        string message, string? draftId = null)
        => new() { Code = code, Severity = severity, Message = message, DraftId = draftId };
}
