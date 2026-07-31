using System.Collections.Immutable;

namespace CrestCreates.Accountability.Abstractions.Contracts;

public sealed record AuditSanitizationStamp
{
    public required string PolicyId { get; init; }
    public required int PolicyVersion { get; init; }
    public ImmutableArray<string> AppliedRuleIds { get; init; } = [];
}
