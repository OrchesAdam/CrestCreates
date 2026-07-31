using CrestCreates.Accountability.Abstractions.Contracts;

namespace CrestCreates.Accountability.Sanitization;

internal static class AuditSanitizerOutputComparer
{
    public static bool IsAllowed(AuditEnvelope candidate, AuditEnvelope sanitized)
    {
        if (!PayloadIsAllowed(candidate.Payload, sanitized.Payload)
            || !ArtifactsAreAllowed(candidate.DataSnapshot, sanitized.DataSnapshot))
            return false;

        return true;
    }

    private static bool PayloadIsAllowed(AuditPayload? candidate, AuditPayload? sanitized)
    {
        if (candidate is null) return sanitized is null;
        if (sanitized is null) return true;
        return string.Equals(candidate.Kind, sanitized.Kind, StringComparison.Ordinal)
            && candidate.Version == sanitized.Version;
    }

    private static bool ArtifactsAreAllowed(AuditDataSnapshot? candidate, AuditDataSnapshot? sanitized)
    {
        if (candidate is null) return sanitized is null;
        if (sanitized is null) return true;

        var available = candidate.Artifacts
            .GroupBy(x => x.Kind, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
        foreach (var artifact in sanitized.Artifacts)
        {
            if (!available.TryGetValue(artifact.Kind, out var remaining) || remaining == 0)
                return false;
            available[artifact.Kind] = remaining - 1;
        }
        return true;
    }
}
