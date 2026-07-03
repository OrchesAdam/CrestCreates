using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.Memory.Llm.Validation;

public static class AgentMemoryLlmDiagnosticCodes
{
    private const string ProviderUnavailableValue = "AGENT_MEMORY_LLM_PROVIDER_UNAVAILABLE";
    public static DiagnosticCode ProviderUnavailable { get; } = new(ProviderUnavailableValue);

    private const string CredentialUnavailableValue = "AGENT_MEMORY_LLM_CREDENTIAL_UNAVAILABLE";
    public static DiagnosticCode CredentialUnavailable { get; } = new(CredentialUnavailableValue);

    private const string UnauthorizedValue = "AGENT_MEMORY_LLM_UNAUTHORIZED";
    public static DiagnosticCode Unauthorized { get; } = new(UnauthorizedValue);

    private const string RateLimitedValue = "AGENT_MEMORY_LLM_RATE_LIMITED";
    public static DiagnosticCode RateLimited { get; } = new(RateLimitedValue);

    private const string TimeoutValue = "AGENT_MEMORY_LLM_TIMEOUT";
    public static DiagnosticCode Timeout { get; } = new(TimeoutValue);

    private const string NetworkErrorValue = "AGENT_MEMORY_LLM_NETWORK_ERROR";
    public static DiagnosticCode NetworkError { get; } = new(NetworkErrorValue);

    private const string ProviderReturnedEmptyOutputValue = "AGENT_MEMORY_LLM_PROVIDER_RETURNED_EMPTY_OUTPUT";
    public static DiagnosticCode ProviderReturnedEmptyOutput { get; } = new(ProviderReturnedEmptyOutputValue);

    private const string ParseFailedValue = "AGENT_MEMORY_LLM_PARSE_FAILED";
    public static DiagnosticCode ParseFailed { get; } = new(ParseFailedValue);

    private const string InvalidSourceRefValue = "AGENT_MEMORY_LLM_INVALID_SOURCE_REF";
    public static DiagnosticCode InvalidSourceRef { get; } = new(InvalidSourceRefValue);

    private const string FallbackToDeterministicCompressorValue = "AGENT_MEMORY_LLM_FALLBACK_TO_DETERMINISTIC_COMPRESSOR";
    public static DiagnosticCode FallbackToDeterministicCompressor { get; } = new(FallbackToDeterministicCompressorValue);

    private const string FallbackToDeterministicExtractorValue = "AGENT_MEMORY_LLM_FALLBACK_TO_DETERMINISTIC_EXTRACTOR";
    public static DiagnosticCode FallbackToDeterministicExtractor { get; } = new(FallbackToDeterministicExtractorValue);

    private const string NonAuthoritativeOutputEnforcedValue = "AGENT_MEMORY_LLM_NON_AUTHORITATIVE_OUTPUT_ENFORCED";
    public static DiagnosticCode NonAuthoritativeOutputEnforced { get; } = new(NonAuthoritativeOutputEnforcedValue);

    private const string CandidateConfidenceCappedValue = "AGENT_MEMORY_LLM_CANDIDATE_CONFIDENCE_CAPPED";
    public static DiagnosticCode CandidateConfidenceCapped { get; } = new(CandidateConfidenceCappedValue);

    private const string CompressionParseErrorValue = "AGENT_MEMORY_LLM_COMPRESSION_PARSE_ERROR";
    public static DiagnosticCode CompressionParseError { get; } = new(CompressionParseErrorValue);

    private const string ExtractionParseErrorValue = "AGENT_MEMORY_LLM_EXTRACTION_PARSE_ERROR";
    public static DiagnosticCode ExtractionParseError { get; } = new(ExtractionParseErrorValue);

    private const string ContentRejectedValue = "AGENT_MEMORY_LLM_CONTENT_REJECTED";
    public static DiagnosticCode ContentRejected { get; } = new(ContentRejectedValue);
}
