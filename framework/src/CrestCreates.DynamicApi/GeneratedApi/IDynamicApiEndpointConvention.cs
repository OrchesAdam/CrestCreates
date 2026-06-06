namespace CrestCreates.DynamicApi;

public interface IDynamicApiEndpointConvention
{
    void Apply(DynamicApiEndpointConventionContext context);
}
