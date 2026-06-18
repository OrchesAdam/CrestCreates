namespace CrestCreates.HumanTask.Abstractions;

public sealed class HumanTaskAssigneeResolution
{
    public string? AssigneeUserId { get; init; }
    public string? AssigneeRoleId { get; init; }
    public IReadOnlyList<string> CandidateUserIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CandidateRoleIds { get; init; } = Array.Empty<string>();
    public string? OrganizationUnitId { get; init; }
    public string? PositionId { get; init; }
    public string? AssigneeResolutionReason { get; init; }

    public bool IsAssigned => !string.IsNullOrWhiteSpace(AssigneeUserId)
                           || !string.IsNullOrWhiteSpace(AssigneeRoleId);

    public bool HasCandidates => CandidateUserIds.Count > 0 || CandidateRoleIds.Count > 0;

    public bool IsUnassigned => !IsAssigned && !HasCandidates
        && string.IsNullOrWhiteSpace(OrganizationUnitId)
        && string.IsNullOrWhiteSpace(PositionId);
}
