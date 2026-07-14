namespace CrestCreates.CapabilityEndpoint.AotFixture;

public sealed class GreetingRequest
{
    public string Name { get; set; } = string.Empty;
}

public sealed class GreetingResponse
{
    public string Message { get; set; } = string.Empty;
}
