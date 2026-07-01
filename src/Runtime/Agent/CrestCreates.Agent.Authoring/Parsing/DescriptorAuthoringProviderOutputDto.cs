using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestCreates.Agent.Authoring.Parsing;

public sealed class DescriptorAuthoringProviderOutputDto
{
    [JsonPropertyName("contractVersion")]
    public string? ContractVersion { get; set; }

    [JsonPropertyName("promptInputHash")]
    public string? PromptInputHash { get; set; }

    [JsonPropertyName("plan")]
    public DescriptorAuthoringProviderPlanDto? Plan { get; set; }

    [JsonPropertyName("items")]
    public List<DescriptorAuthoringProviderItemDto>? Items { get; set; }
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
