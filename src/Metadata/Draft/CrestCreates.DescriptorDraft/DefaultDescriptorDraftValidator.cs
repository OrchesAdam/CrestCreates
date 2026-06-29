using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.DescriptorDraft;

public sealed class DefaultDescriptorDraftValidator : IDescriptorDraftValidator
{
    public DescriptorDraftValidationResult Validate(Draft draft)
    {
        var diagnostics = new List<DescriptorDraftDiagnostic>();

        if (string.IsNullOrWhiteSpace(draft.DraftId))
            diagnostics.Add(Diag(new DiagnosticCode("DRAFT_ID_EMPTY"), SeverityLevel.Error,
                "DraftId must not be empty.", draft.DraftId));

        if (string.IsNullOrWhiteSpace(draft.DescriptorId))
            diagnostics.Add(Diag(new DiagnosticCode("DESCRIPTOR_ID_EMPTY"), SeverityLevel.Error,
                "DescriptorId must not be empty.", draft.DraftId));

        if (string.IsNullOrWhiteSpace(draft.AuthorId))
            diagnostics.Add(Diag(new DiagnosticCode("AUTHOR_ID_EMPTY"), SeverityLevel.Error,
                "AuthorId must not be empty.", draft.DraftId));

        if (draft.DescriptorKind != draft.Payload.DescriptorKind)
            diagnostics.Add(Diag(new DiagnosticCode("KIND_PAYLOAD_MISMATCH"), SeverityLevel.Error,
                $"DescriptorKind '{draft.DescriptorKind}' does not match Payload kind '{draft.Payload.DescriptorKind}'.",
                draft.DraftId));

        var payloadDescriptor = draft.Payload.GetDescriptor();
        if (payloadDescriptor.Id != draft.DescriptorId)
            diagnostics.Add(Diag(new DiagnosticCode("PAYLOAD_ID_MISMATCH"), SeverityLevel.Error,
                $"Payload descriptor Id '{payloadDescriptor.Id}' does not match draft DescriptorId '{draft.DescriptorId}'.",
                draft.DraftId));

        // Version consistency: ProposedVersion must match payload descriptor version
        var payloadVersion = (payloadDescriptor as IVersionedDescriptor)?.Version;
        ValidateVersionConsistency(draft, payloadVersion, diagnostics);

        switch (draft.Operation)
        {
            case DescriptorDraftOperation.Create:
                if (!string.IsNullOrWhiteSpace(draft.BaseVersion))
                    diagnostics.Add(Diag(new DiagnosticCode("CREATE_BASE_VERSION_MUST_BE_EMPTY"),
                        SeverityLevel.Error,
                        "Create operation must not specify BaseVersion.", draft.DraftId));
                break;
            case DescriptorDraftOperation.Update:
                if (string.IsNullOrWhiteSpace(draft.BaseVersion))
                    diagnostics.Add(Diag(new DiagnosticCode("UPDATE_BASE_VERSION_REQUIRED"),
                        SeverityLevel.Error,
                        "Update operation requires BaseVersion.", draft.DraftId));
                break;
            case DescriptorDraftOperation.Deprecate:
                if (string.IsNullOrWhiteSpace(draft.BaseVersion))
                    diagnostics.Add(Diag(new DiagnosticCode("DEPRECATE_BASE_VERSION_REQUIRED"),
                        SeverityLevel.Error,
                        "Deprecate operation requires BaseVersion.", draft.DraftId));
                break;
            case DescriptorDraftOperation.Remove:
                if (string.IsNullOrWhiteSpace(draft.BaseVersion))
                    diagnostics.Add(Diag(new DiagnosticCode("REMOVE_BASE_VERSION_REQUIRED"),
                        SeverityLevel.Error,
                        "Remove operation requires BaseVersion.", draft.DraftId));
                break;
        }

        return diagnostics.Count == 0
            ? DescriptorDraftValidationResult.Success()
            : DescriptorDraftValidationResult.Failure(diagnostics.ToArray());
    }

    private static void ValidateVersionConsistency(Draft draft, int? payloadVersion,
        List<DescriptorDraftDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(draft.ProposedVersion))
        {
            // ProposedVersion must be present for Create/Update; infer from payload if missing
            if (draft.Operation is DescriptorDraftOperation.Create or DescriptorDraftOperation.Update)
                diagnostics.Add(Diag(new DiagnosticCode("PROPOSED_VERSION_MISSING"), SeverityLevel.Error,
                    $"ProposedVersion is required for {draft.Operation}.", draft.DraftId));
            return;
        }

        if (!int.TryParse(draft.ProposedVersion, out var envelopeVersion))
        {
            diagnostics.Add(Diag(new DiagnosticCode("PROPOSED_VERSION_NOT_INTEGER"), SeverityLevel.Error,
                $"ProposedVersion '{draft.ProposedVersion}' is not a valid integer.", draft.DraftId));
            return;
        }

        if (payloadVersion.HasValue && envelopeVersion != payloadVersion.Value)
            diagnostics.Add(Diag(new DiagnosticCode("PROPOSED_VERSION_MISMATCH"), SeverityLevel.Error,
                $"ProposedVersion '{envelopeVersion}' does not match payload descriptor version '{payloadVersion}'.",
                draft.DraftId));
    }

    private static DescriptorDraftDiagnostic Diag(DiagnosticCode code, SeverityLevel severity,
        string message, string? draftId = null)
        => new() { Code = code, Severity = severity, Message = message, DraftId = draftId };
}
