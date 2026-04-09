namespace CrestCreates.DynamicApi;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Method, AllowMultiple = false)]
public sealed class DynamicApiIgnoreAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Method, AllowMultiple = false)]
public sealed class DynamicApiRouteAttribute : Attribute
{
    public DynamicApiRouteAttribute(string template)
    {
        Template = template;
    }

    public string Template { get; }
}
