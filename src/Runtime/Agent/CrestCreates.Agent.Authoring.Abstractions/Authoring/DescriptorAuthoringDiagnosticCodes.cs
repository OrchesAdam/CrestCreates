using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.Authoring.Abstractions.Authoring;

public static class DescriptorAuthoringDiagnosticCodes
{
    public const string ProviderTimeoutValue = "AUTHORING_PROVIDER_TIMEOUT";
    public static DiagnosticCode ProviderTimeout { get; } = new(ProviderTimeoutValue);

    public const string ProviderRateLimitedValue = "AUTHORING_PROVIDER_RATE_LIMITED";
    public static DiagnosticCode ProviderRateLimited { get; } = new(ProviderRateLimitedValue);

    public const string ProviderUnauthorizedValue = "AUTHORING_PROVIDER_UNAUTHORIZED";
    public static DiagnosticCode ProviderUnauthorized { get; } = new(ProviderUnauthorizedValue);

    public const string CredentialUnavailableValue = "AUTHORING_CREDENTIAL_UNAVAILABLE";
    public static DiagnosticCode CredentialUnavailable { get; } = new(CredentialUnavailableValue);

    public const string CredentialRejectedValue = "AUTHORING_CREDENTIAL_REJECTED";
    public static DiagnosticCode CredentialRejected { get; } = new(CredentialRejectedValue);

    public const string InvalidProviderOutputValue = "AUTHORING_INVALID_PROVIDER_OUTPUT";
    public static DiagnosticCode InvalidProviderOutput { get; } = new(InvalidProviderOutputValue);

    public const string PromptHashMismatchValue = "AUTHORING_PROMPT_HASH_MISMATCH";
    public static DiagnosticCode PromptHashMismatch { get; } = new(PromptHashMismatchValue);

    public const string UnknownDescriptorKindValue = "AUTHORING_UNKNOWN_DESCRIPTOR_KIND";
    public static DiagnosticCode UnknownDescriptorKind { get; } = new(UnknownDescriptorKindValue);

    public const string UnsupportedDraftOperationValue = "AUTHORING_UNSUPPORTED_DRAFT_OPERATION";
    public static DiagnosticCode UnsupportedDraftOperation { get; } = new(UnsupportedDraftOperationValue);

    public const string GovernanceBoundaryViolationValue = "AUTHORING_GOVERNANCE_BOUNDARY_VIOLATION";
    public static DiagnosticCode GovernanceBoundaryViolation { get; } = new(GovernanceBoundaryViolationValue);

    public const string MemoryAuthorityClaimRejectedValue = "AUTHORING_MEMORY_AUTHORITY_CLAIM_REJECTED";
    public static DiagnosticCode MemoryAuthorityClaimRejected { get; } = new(MemoryAuthorityClaimRejectedValue);

    public const string ProviderUnavailableValue = "AUTHORING_PROVIDER_UNAVAILABLE";
    public static DiagnosticCode ProviderUnavailable { get; } = new(ProviderUnavailableValue);
}
