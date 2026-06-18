namespace CrestCreates.Event.Abstractions;

public interface IRegistryBootstrapper
{
    Task BootstrapAsync(CancellationToken ct);
}
