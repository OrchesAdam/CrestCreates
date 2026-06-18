namespace CrestCreates.Authorization.Abstractions;

public static class PermissionNames
{
    public const string Create = "Create";
    public const string Update = "Update";
    public const string Delete = "Delete";
    public const string View = "View";
    public const string Manage = "Manage";

    public static string[] GetCrudPermissions(string entityName)
    {
        return new[]
        {
            $"{entityName}.{Create}",
            $"{entityName}.{Update}",
            $"{entityName}.{Delete}",
            $"{entityName}.{View}"
        };
    }
}
