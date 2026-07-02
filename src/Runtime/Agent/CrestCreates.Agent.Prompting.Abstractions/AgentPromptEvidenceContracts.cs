namespace CrestCreates.Agent.Prompting.Abstractions;

public sealed record AgentPromptEvidenceCreationRequest<TPayload>
{
    public required AgentPromptTemplateId TemplateId { get; init; }
    public required AgentPromptVersion TemplateVersion { get; init; }
    public required AgentPromptPurpose Purpose { get; init; }
    public required AgentPromptContractVersion ContractVersion { get; init; }
    public required AgentPromptModelProfileRef ModelProfileRef { get; init; }
    public required AgentPromptProviderProfileRef ProviderProfileRef { get; init; }
    public required TPayload Payload { get; init; }
    public string? TenantId { get; init; }
    public string? ActorId { get; init; }
    public string? CorrelationId { get; init; }
}

public sealed record AgentPromptProviderObservation
{
    public string? ProviderName { get; init; }
    public string? ModelName { get; init; }
    public string? ResponseId { get; init; }
    public string? FinishReason { get; init; }
    public long? LatencyMs { get; init; }
}

public sealed record AgentPromptInputEvidence<TInput>
{
    public required AgentPromptTemplateId TemplateId { get; init; }
    public required AgentPromptVersion TemplateVersion { get; init; }
    public required AgentPromptPurpose Purpose { get; init; }
    public required AgentPromptContractVersion ContractVersion { get; init; }
    public required AgentPromptModelProfileRef ModelProfileRef { get; init; }
    public required AgentPromptProviderProfileRef ProviderProfileRef { get; init; }
    public required TInput Input { get; init; }
    public required CanonicalHash InputHash { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? TenantId { get; init; }
    public string? ActorId { get; init; }
    public string? CorrelationId { get; init; }
    public IReadOnlyList<AgentPromptDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentPromptDiagnostic>();
}

public sealed record AgentPromptOutputEvidence<TOutput>
{
    public required AgentPromptTemplateId TemplateId { get; init; }
    public required AgentPromptVersion TemplateVersion { get; init; }
    public required AgentPromptPurpose Purpose { get; init; }
    public required AgentPromptContractVersion ContractVersion { get; init; }
    public required AgentPromptModelProfileRef ModelProfileRef { get; init; }
    public required AgentPromptProviderProfileRef ProviderProfileRef { get; init; }
    public required CanonicalHash InputHash { get; init; }
    public CanonicalHash? OutputHash { get; init; }
    public required TOutput Output { get; init; }
    public AgentPromptProviderObservation? ProviderObservation { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public IReadOnlyList<AgentPromptDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentPromptDiagnostic>();
}

public sealed record AgentPromptInputEvidenceSummary
{
    public required AgentPromptTemplateId TemplateId { get; init; }
    public required AgentPromptVersion TemplateVersion { get; init; }
    public required AgentPromptPurpose Purpose { get; init; }
    public required AgentPromptContractVersion ContractVersion { get; init; }
    public required AgentPromptModelProfileRef ModelProfileRef { get; init; }
    public required AgentPromptProviderProfileRef ProviderProfileRef { get; init; }
    public required CanonicalHash InputHash { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public IReadOnlyList<AgentPromptDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentPromptDiagnostic>();
}

public sealed record AgentPromptOutputEvidenceSummary
{
    public required AgentPromptTemplateId TemplateId { get; init; }
    public required AgentPromptVersion TemplateVersion { get; init; }
    public required AgentPromptPurpose Purpose { get; init; }
    public required AgentPromptContractVersion ContractVersion { get; init; }
    public required AgentPromptModelProfileRef ModelProfileRef { get; init; }
    public required AgentPromptProviderProfileRef ProviderProfileRef { get; init; }
    public required CanonicalHash InputHash { get; init; }
    public CanonicalHash? OutputHash { get; init; }
    public AgentPromptProviderObservation? ProviderObservation { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public IReadOnlyList<AgentPromptDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentPromptDiagnostic>();
}
