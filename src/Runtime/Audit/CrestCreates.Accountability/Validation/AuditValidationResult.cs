using System.Collections.Immutable;
using CrestCreates.Accountability.Abstractions.Recording;

namespace CrestCreates.Accountability.Validation;

public sealed record AuditValidationResult
{
    public ImmutableArray<AuditRecordIssue> Issues { get; init; } = [];

    public bool IsValid => Issues.IsDefaultOrEmpty;

    public static AuditValidationResult Valid { get; } = new();

    public static AuditValidationResult Invalid(params AuditRecordIssue[] issues)
        => new() { Issues = [.. issues] };
}
