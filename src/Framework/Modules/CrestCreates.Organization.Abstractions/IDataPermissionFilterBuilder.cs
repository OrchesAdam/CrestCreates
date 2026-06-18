namespace CrestCreates.Organization.Abstractions;

public interface IDataPermissionFilterBuilder
{
    DataPermissionFilter Build(DataPermissionScope scope, DataPermissionFieldMapping mapping);
}
