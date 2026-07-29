using System.Text.Json;

namespace CrestCreates.Accountability.Abstractions.Contracts;

public sealed record AuditPayload
{
    public required string Kind { get; init; }
    public required int Version { get; init; }
    public required JsonElement Data { get; init; }
}
