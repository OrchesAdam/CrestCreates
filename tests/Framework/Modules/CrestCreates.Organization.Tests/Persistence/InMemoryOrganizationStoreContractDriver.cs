using CrestCreates.ControlPlane.ReferenceData.Persistence.Testing;
using CrestCreates.Organization;
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization.Tests.Persistence;

internal sealed class InMemoryOrganizationStoreContractDriver : IOrganizationStoreContractDriver
{
    public InMemoryOrganizationStoreContractDriver()
        => Store = new InMemoryOrganizationStore();

    public IOrganizationStore Store { get; }

    public ValueTask ResetAsync() => ValueTask.CompletedTask;
}
