namespace CrestCreates.HealthCheck.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class HealthCheckAttribute : Attribute
{
    public string? Name { get; set; }
    public string[] Tags { get; set; } = Array.Empty<string>();
    public string? Description { get; set; }
}