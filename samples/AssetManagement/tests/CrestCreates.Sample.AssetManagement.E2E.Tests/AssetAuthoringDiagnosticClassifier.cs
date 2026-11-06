using CrestCreates.Agent.Authoring.Abstractions.Authoring;

namespace CrestCreates.Sample.AssetManagement.E2E.Tests;

/// <summary>
/// Attributes one authoring diagnostic to a fixed category without ever retaining its message text.
/// Classification is code-first: the two explicit terminal outcomes are named by their own codes, while
/// <c>AUTHORING_INVALID_PROVIDER_OUTPUT</c> covers many different causes and is therefore split by the
/// message families the `8a.v1` parser actually emits. Anything unrecognised stays <c>Other</c> so a
/// drifted or sensitive message can never leak into an artifact or a verdict.
/// </summary>
internal static class AssetAuthoringDiagnosticClassifier
{
    public static string Classify(string? code, string? message)
    {
        return code switch
        {
            DescriptorAuthoringDiagnosticCodes.IntentUnsupportedValue => "ExplicitUnsupportedIntent",
            DescriptorAuthoringDiagnosticCodes.IntentNeedsClarificationValue => "ExplicitNeedsClarification",
            DescriptorAuthoringDiagnosticCodes.PromptHashMismatchValue => "PromptHashMismatch",
            DescriptorAuthoringDiagnosticCodes.UnknownDescriptorKindValue => "UnknownDescriptorKind",
            DescriptorAuthoringDiagnosticCodes.UnsupportedDraftOperationValue => "UnsupportedDraftOperation",
            DescriptorAuthoringDiagnosticCodes.GovernanceBoundaryViolationValue => "GovernanceBoundaryViolation",
            DescriptorAuthoringDiagnosticCodes.InvalidProviderOutputValue => ClassifyRejectedOutput(message),
            _ => "Other"
        };
    }

    private static string ClassifyRejectedOutput(string? message)
    {
        if (message is null)
        {
            return "Other";
        }

        return message switch
        {
            "Provider output deserialized to null." => "NullOutput",
            "Provider output contains no items." => "NoItems",
            "Provider output has no usable 'promptInputHash' string member." => "PromptHashMemberMissing",
            "Provider output is missing the required 'outcome' discriminator." => "OutcomeDiscriminatorMissing",
            _ when message.StartsWith("Failed to deserialize provider output as JSON", StringComparison.Ordinal)
                => "JsonDeserializationFailed",
            _ when message.StartsWith("Unsupported contract version", StringComparison.Ordinal)
                => "ContractVersionMismatch",
            _ when message.StartsWith("Unsupported outcome", StringComparison.Ordinal)
                => "OutcomeOutsideDiscriminator",
            _ when message.StartsWith("Envelope contains unknown member", StringComparison.Ordinal)
                => "EnvelopeUnknownMember",
            _ when message.StartsWith("Envelope contains duplicate member", StringComparison.Ordinal)
                => "EnvelopeDuplicateMember",
            _ when message.Contains("cannot be decoded as UTF-16 text", StringComparison.Ordinal)
                => "EnvelopeUndecodableMember",
            _ when message.Contains("is outside the closed code table", StringComparison.Ordinal)
                => "ClosedCodeTableViolation",
            _ when message.StartsWith("Provider output with outcome", StringComparison.Ordinal)
                || message.StartsWith("Provider output item", StringComparison.Ordinal)
                => "OutcomeShapeConflict",
            _ when message.StartsWith("'", StringComparison.Ordinal)
                && (message.Contains("must hold", StringComparison.Ordinal)
                    || message.Contains("must be an array", StringComparison.Ordinal)
                    || message.Contains("requires string", StringComparison.Ordinal)
                    || message.Contains("repeats", StringComparison.Ordinal))
                => "OutcomeEntryBoundViolation",
            _ => "Other"
        };
    }
}
