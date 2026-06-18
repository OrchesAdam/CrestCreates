namespace CrestCreates.Organization.Abstractions;

public static class DataPermissionAction
{
    public const string None = nameof(None);
    public const string Read = nameof(Read);
    public const string Create = nameof(Create);
    public const string Update = nameof(Update);
    public const string Delete = nameof(Delete);
    public const string Query = nameof(Query); // alias for Read — CRUD-compatible search/list operations
}
