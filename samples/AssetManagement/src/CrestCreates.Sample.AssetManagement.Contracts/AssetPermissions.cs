namespace CrestCreates.Sample.AssetManagement.Contracts;

public static class AssetPermissions
{
    public const string GroupName = "AssetManagement";

    public static class Assets
    {
        public const string Read = GroupName + ".Assets.Read";
        public const string Search = GroupName + ".Assets.Search";
        public const string Register = GroupName + ".Assets.Register";
        public const string Update = GroupName + ".Assets.Update";
        public const string Assign = GroupName + ".Assets.Assign";
        public const string Return = GroupName + ".Assets.Return";
        public const string Transfer = GroupName + ".Assets.Transfer";
        public const string RequestMaintenance = GroupName + ".Assets.RequestMaintenance";
        public const string CompleteMaintenance = GroupName + ".Assets.CompleteMaintenance";
    }
}
