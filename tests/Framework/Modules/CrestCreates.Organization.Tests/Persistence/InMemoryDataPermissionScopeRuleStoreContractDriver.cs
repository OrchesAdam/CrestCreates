using CrestCreates.ControlPlane.ReferenceData.Persistence.Testing;
using CrestCreates.Organization;
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization.Tests.Persistence;

internal sealed class InMemoryDataPermissionScopeRuleStoreContractDriver : IDataPermissionScopeRuleStoreContractDriver
{
    public InMemoryDataPermissionScopeRuleStoreContractDriver()
        => Store = new InMemoryDataPermissionScopeRuleStore();

    public IDataPermissionScopeRuleStore Store { get; }

    public ValueTask ResetAsync() => ValueTask.CompletedTask;
}
