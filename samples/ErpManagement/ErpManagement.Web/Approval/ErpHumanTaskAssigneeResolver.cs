using CrestCreates.HumanTask;
using CrestCreates.HumanTask.Abstractions;
using ErpManagement.Domain.Shared.Constants;

namespace ErpManagement.Web.Approval;

/// <summary>
/// The framework default resolver projects no candidate roles for CandidateGroup tasks, so the
/// approval-center candidate-role query would never see ERP tasks. Route the ERP approval
/// descriptor to the procurement-manager role and defer everything else to the framework default.
/// </summary>
public sealed class ErpHumanTaskAssigneeResolver : IHumanTaskAssigneeResolver
{
    private static readonly DefaultHumanTaskAssigneeResolver Default = new();

    public Task<HumanTaskAssigneeResolution> ResolveAsync(
        HumanTaskDescriptor descriptor,
        HumanTaskCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(descriptor.Id, ErpContractIds.RequisitionApprovalHumanTask, StringComparison.Ordinal))
        {
            return Task.FromResult(new HumanTaskAssigneeResolution
            {
                CandidateRoleIds = new[] { ErpContractIds.ProcurementManagerRole },
                AssigneeResolutionReason = "erp.requisition-approval-candidate-role"
            });
        }

        if (descriptor.Id == ErpContractIds.SalesApprovalHumanTask)
        {
            return Task.FromResult(new HumanTaskAssigneeResolution
            {
                CandidateRoleIds = new[] { ErpContractIds.SalesManagerRole },
                AssigneeResolutionReason = "erp.sales-approval-candidate-role"
            });
        }

        return Default.ResolveAsync(descriptor, request, cancellationToken);
    }
}