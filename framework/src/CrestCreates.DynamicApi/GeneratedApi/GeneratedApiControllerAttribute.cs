namespace CrestCreates.DynamicApi;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class GeneratedApiControllerAttribute : Attribute
{
    public GeneratedApiControllerAttribute()
    {
    }

    public GeneratedApiControllerAttribute(string routeTemplate)
    {
        RouteTemplate = routeTemplate;
    }

    public string? RouteTemplate { get; }
}