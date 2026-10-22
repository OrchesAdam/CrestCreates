namespace CrestCreates.DynamicApi;

public enum DynamicApiParameterSource
{
    Route = 1,
    Query = 2,
    Body = 3,
    Header = 5,
    CancellationToken = 4
}
