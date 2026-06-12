using CrestCreates.HumanTask.Abstractions;

namespace CrestCreates.HumanTask;

public sealed class DefaultHumanTaskAssigneeResolver : IHumanTaskAssigneeResolver
{
    public Task<HumanTaskAssigneeResolution> ResolveAsync(
        HumanTaskDescriptor descriptor,
        HumanTaskCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        string? assigneeUserId = null;
        string? assigneeRoleId = null;
        string[] candidateRoleIds = Array.Empty<string>();
        bool hasAssignee = false;

        // Priority 1: explicit user
        if (!string.IsNullOrWhiteSpace(request.AssigneeUserId))
        {
            assigneeUserId = request.AssigneeUserId;
            hasAssignee = true;
            if (!string.IsNullOrWhiteSpace(request.AssigneeRoleId))
            {
                candidateRoleIds = new[] { request.AssigneeRoleId };
            }
        }
        // Priority 2: explicit role (no user)
        else if (!string.IsNullOrWhiteSpace(request.AssigneeRoleId))
        {
            assigneeRoleId = request.AssigneeRoleId;
            hasAssignee = true;
        }

        // Priority 3: auxiliary context (additive — applied regardless of priority 1/2)
        var organizationUnitId = !string.IsNullOrWhiteSpace(request.RequestedOrganizationUnitId)
            ? request.RequestedOrganizationUnitId : null;
        var positionId = !string.IsNullOrWhiteSpace(request.RequestedPositionId)
            ? request.RequestedPositionId : null;

        // Priority 4: strategy fallback (only when nothing resolved)
        string? reason = null;
        if (!hasAssignee
            && organizationUnitId == null
            && positionId == null)
        {
            reason = GetStrategyReason(descriptor);
        }

        return Task.FromResult(new HumanTaskAssigneeResolution
        {
            AssigneeUserId = assigneeUserId,
            AssigneeRoleId = assigneeRoleId,
            CandidateRoleIds = candidateRoleIds,
            OrganizationUnitId = organizationUnitId,
            PositionId = positionId,
            AssigneeResolutionReason = reason
        });
    }

    private static string? GetStrategyReason(HumanTaskDescriptor descriptor)
    {
        return descriptor.AssigneeStrategy switch
        {
            AssigneeStrategy.RoundRobin => "RoundRobin strategy is not yet implemented",
            AssigneeStrategy.LeastLoaded => "LeastLoaded strategy is not yet implemented",
            _ => null
        };
    }
}
