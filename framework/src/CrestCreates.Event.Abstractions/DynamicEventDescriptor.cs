using System.Security.Cryptography;
using System.Text;

namespace CrestCreates.Event.Abstractions;

public sealed record DynamicEventDescriptor : IEventDescriptor
{
    public string Id { get; init; } = string.Empty;        // SHA256(Name) — unversioned
    public string Name { get; init; } = string.Empty;
    public string Namespace { get; init; } = "event";
    public EventScope Scope { get; init; }
    public EventImportance Importance { get; init; } = EventImportance.Operational;
    public bool IsAuditable { get; init; }
    public bool IsReplayable { get; init; }
    public bool IsPublic { get; init; }
    public string? Description { get; init; }
    public Type? PayloadType { get; init; }                 // Optional — no schema enforcement

    public static string GenerateId(string name)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(name));
        return "dyn_" + Convert.ToHexString(hash)[..12];
    }
}
