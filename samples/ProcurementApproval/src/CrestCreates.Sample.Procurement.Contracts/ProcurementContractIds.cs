namespace CrestCreates.Sample.Procurement.Contracts;

public static class ProcurementContractIds
{
    public const string SubmitCapability = "procurement.submit-request";
    public const string GetCapability = "procurement.get-request";
    public const string ApproveCapability = "procurement.approve-request";
    public const string RejectCapability = "procurement.reject-request";
    public const string ApplyApprovalDecisionCapability = "procurement.request.apply-approval";
    public const string ApplyRejectionDecisionCapability = "procurement.request.apply-rejection";

    public const string SubmitInputSchema = "procurement.schema.submit-input";
    public const string SubmitOutputSchema = "procurement.schema.submit-output";
    public const string GetInputSchema = "procurement.schema.get-input";
    public const string RequestOutputSchema = "procurement.schema.request-output";
    public const string ApproveInputSchema = "procurement.schema.approve-input";
    public const string RejectInputSchema = "procurement.schema.reject-input";

    public const string ApprovalWorkflow = "wf_procurement_approval";
    public const string ApprovalHumanTask = "ht_procurement_approval";
    public const string ApprovalForm = "form_procurement_approval";

    public const string GetTool = "procurement_get_request";
    public const string SubmitTool = "procurement_submit_request";
}
