using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestCreates.Sample.AssetManagement.MicrosoftAgent;

internal sealed record CoordinationToolExchangePrompt(
    string Tool, JsonElement? Arguments, string Code, bool Succeeded, JsonElement? Output);

internal sealed record CoordinationInputPrompt(string Id, string ContentHash, string? Text);

internal sealed record CoordinationResumedPrompt(
    string EventId, string Source, string Kind, string SubjectId, string ContentHash, string? Text);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(CoordinationToolExchangePrompt[]))]
[JsonSerializable(typeof(CoordinationInputPrompt[]))]
[JsonSerializable(typeof(CoordinationResumedPrompt))]
internal partial class MicrosoftAgentCoordinationJsonContext : JsonSerializerContext;
