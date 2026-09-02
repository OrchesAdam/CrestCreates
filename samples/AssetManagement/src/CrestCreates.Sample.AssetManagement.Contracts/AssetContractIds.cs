namespace CrestCreates.Sample.AssetManagement.Contracts;

public static class AssetContractIds
{
    public const string RegisterCapability = "asset-management.assets.register";
    public const string GetCapability = "asset-management.assets.get";
    public const string QueryCapability = "asset-management.assets.query";
    public const string UpdateCapability = "asset-management.assets.update";
    public const string AssignCapability = "asset-management.assets.assign";
    public const string ReturnCapability = "asset-management.assets.return";
    public const string TransferCapability = "asset-management.assets.transfer";
    public const string RequestMaintenanceCapability = "asset-management.assets.request-maintenance";
    public const string ApplyMaintenanceCapability = "asset-management.assets.apply-maintenance";

    public const string RegisterTool = "asset_register";
    public const string GetTool = "asset_get";
    public const string QueryTool = "asset_query";

    public const string MaintenanceWorkflow = "wf_asset_maintenance_review";
    public const string MaintenanceHumanTask = "ht_asset_maintenance_review";
    public const string MaintenanceForm = "form_asset_maintenance_review";
    public const string MaintenanceDecisionConsumer = "crest.sample.asset-management.maintenance-decision/v1";

    public const string AssetResource = "asset-management.asset";
}
