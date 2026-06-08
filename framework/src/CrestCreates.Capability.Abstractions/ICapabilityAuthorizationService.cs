namespace CrestCreates.Capability.Abstractions;

public interface ICapabilityAuthorizationService
{
    Task<bool> AuthorizeAsync(string capabilityName, string? userId, CancellationToken ct);
}
