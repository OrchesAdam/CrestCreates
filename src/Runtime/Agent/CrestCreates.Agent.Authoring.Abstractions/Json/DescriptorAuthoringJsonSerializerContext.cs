using System.Text.Json.Serialization;
using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Abstractions.Model;
using CrestCreates.Agent.Authoring.Abstractions.Prompting;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Agent.Prompting.Abstractions.Json;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Authoring.Abstractions.Json;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(DescriptorAuthoringResult))]
[JsonSerializable(typeof(DescriptorAuthoringPlan))]
[JsonSerializable(typeof(DescriptorDraftSet))]
[JsonSerializable(typeof(DescriptorAuthoringDiagnostic))]
[JsonSerializable(typeof(DescriptorAuthoringPromptInput))]
[JsonSerializable(typeof(DescriptorAuthoringPromptOutput))]
[JsonSerializable(typeof(DescriptorAuthoringMetadataContextProjection))]
[JsonSerializable(typeof(DescriptorAuthoringMemoryProjection))]
[JsonSerializable(typeof(DescriptorAuthoringModelRequest))]
[JsonSerializable(typeof(DescriptorAuthoringModelResponse))]
[JsonSerializable(typeof(DescriptorAuthoringModelProfile))]
[JsonSerializable(typeof(DescriptorAuthoringProviderProfile))]
[JsonSerializable(typeof(CanonicalHash))]
[JsonSerializable(typeof(DescriptorAuthoringProviderFailureKind))]
[JsonSerializable(typeof(CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft))]
[JsonSerializable(typeof(AgentPromptInputEvidenceSummary))]
[JsonSerializable(typeof(AgentPromptOutputEvidenceSummary))]
[JsonSerializable(typeof(AgentPromptProviderObservation))]
[JsonSerializable(typeof(AgentPromptDiagnostic))]
[JsonSerializable(typeof(AgentPromptPurpose))]
public sealed partial class DescriptorAuthoringJsonSerializerContext : JsonSerializerContext;
