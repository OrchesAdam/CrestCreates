using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestCreates.Agent.Authoring.Parsing;

public sealed class DescriptorAuthoringProviderOutputDto
{
    [JsonPropertyName("contractVersion")]
    public string? ContractVersion { get; set; }

    [JsonPropertyName("promptInputHash")]
    public string? PromptInputHash { get; set; }

    [JsonPropertyName("outcome")]
    public string? Outcome { get; set; }

    [JsonPropertyName("plan")]
    public DescriptorAuthoringProviderPlanDto? Plan { get; set; }

    [JsonPropertyName("items")]
    public List<DescriptorAuthoringProviderItemDto>? Items { get; set; }

    [JsonPropertyName("unsupported")]
    public DescriptorAuthoringProviderUnsupportedDto? Unsupported { get; set; }

    [JsonPropertyName("needsClarification")]
    public DescriptorAuthoringProviderClarificationDto? Clarification { get; set; }
}

public sealed class DescriptorAuthoringProviderUnsupportedDto
{
    [JsonPropertyName("reasons")]
    public List<DescriptorAuthoringProviderUnsupportedReasonDto>? Reasons { get; set; }
}

public sealed class DescriptorAuthoringProviderUnsupportedReasonDto
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("evidenceRefs")]
    public List<string>? EvidenceRefs { get; set; }
}

public sealed class DescriptorAuthoringProviderClarificationDto
{
    [JsonPropertyName("questions")]
    public List<DescriptorAuthoringProviderClarificationQuestionDto>? Questions { get; set; }
}

public sealed class DescriptorAuthoringProviderClarificationQuestionDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("reasonCode")]
    public string? ReasonCode { get; set; }

    [JsonPropertyName("question")]
    public string? Question { get; set; }

    [JsonPropertyName("missingInputs")]
    public List<string>? MissingInputs { get; set; }

    [JsonPropertyName("candidateValues")]
    public List<string>? CandidateValues { get; set; }
}

public sealed class DescriptorAuthoringProviderPlanDto
{
    [JsonPropertyName("planId")]
    public string? PlanId { get; set; }

    [JsonPropertyName("intentText")]
    public string? IntentText { get; set; }

    [JsonPropertyName("assumptions")]
    public List<string>? Assumptions { get; set; }

    [JsonPropertyName("plannedDescriptorRefs")]
    public List<DescriptorAuthoringProviderDescriptorRefDto>? PlannedDescriptorRefs { get; set; }
}

public sealed class DescriptorAuthoringProviderDescriptorRefDto
{
    [JsonPropertyName("namespace")]
    public string? Namespace { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("version")]
    public int? Version { get; set; }
}

public sealed class DescriptorAuthoringProviderItemDto
{
    [JsonPropertyName("descriptorKind")]
    public string? DescriptorKind { get; set; }

    [JsonPropertyName("descriptorId")]
    public string? DescriptorId { get; set; }

    [JsonPropertyName("operation")]
    public string? Operation { get; set; }

    /// <summary>
    /// Optional existing descriptor version selected by an Update item.
    /// When omitted, Update retains the existing same-version behavior.
    /// </summary>
    [JsonPropertyName("baseVersion")]
    public string? BaseVersion { get; set; }

    [JsonPropertyName("rationale")]
    public string? Rationale { get; set; }

    // JsonElement is a struct; null payload from JSON deserialization yields HasValue=false.
    // Non-null with ValueKind!=Object is caught by TryParseItem's validation.
    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; set; }

    [JsonPropertyName("evidenceRefs")]
    public List<string>? EvidenceRefs { get; set; }

    [JsonPropertyName("memoryRefs")]
    public List<string>? MemoryRefs { get; set; }

    [JsonPropertyName("assumptions")]
    public List<string>? Assumptions { get; set; }
}
