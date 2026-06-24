namespace CrestCreates.Authorization.Abstractions;

public interface IPermissionDefinitionProvider
{
    void Define(IPermissionDefinitionContext context);
}
