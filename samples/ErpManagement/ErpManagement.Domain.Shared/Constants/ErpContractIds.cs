namespace ErpManagement.Domain.Shared.Constants;

public static class ErpContractIds
{
    public const string RequisitionApprovalHumanTask = "ht_erp_requisition_approval";
    public const string RequisitionApprovalWorkflow = "wf_erp_requisition_approval";
    public const string RequisitionApprovalForm = "form_erp_requisition_approval";
    public const string RequisitionDecisionSchema = "schema_erp_requisition_decision";

    public const string RequisitionDecisionConsumerId = "crest.erp.requisition-decision/v1";
    public const string RequisitionDecisionTypeId = "crest.erp.requisition-decision/v1";

    public const string StockPostedContractId = "crest.erp.stock-posted/v1";
    public const string StockPostedConsumerId = "crest.erp.stock-posted-inbox/v1";

    public const string SalesApprovalHumanTask = "ht_erp_sales_approval";
    public const string SalesApprovalWorkflow = "wf_erp_sales_approval";
    public const string SalesApprovalForm = "form_erp_sales_approval";
    public const string SalesDecisionSchema = "schema_erp_sales_decision";
    public const string SalesDecisionConsumerId = "crest.erp.sales-decision/v1";
    public const string SalesDecisionTypeId = "crest.erp.sales-decision/v1";
    public const string SalesManagerRole = "sales-manager";

    public const string ProcurementManagerRole = "procurement-manager";
}