namespace CrestCreates.DynamicApi;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ApiOverrideAttribute : Attribute
{
    public ApiOverrideAttribute(CrudAction action)
    {
        Action = action;
    }

    public CrudAction Action { get; }
}
