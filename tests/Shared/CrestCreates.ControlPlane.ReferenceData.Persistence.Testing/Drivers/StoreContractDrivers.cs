using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Organization.Abstractions;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.ControlPlane.ReferenceData.Persistence.Testing;

public interface IDescriptorDraftStoreContractDriver
{
    IDescriptorDraftStore Store { get; }
    IDescriptorDraftValidator Validator { get; }
    Draft CreatePayloadVariant(DescriptorPayloadVariant variant);
    DescriptorPayloadObservation ObservePayload(Draft draft, DescriptorPayloadVariant variant);
    Draft CreateValidatorOwnedInvalid(DraftValidatorOwnedInvalidVariant variant);
    ValueTask ResetAsync();
}

public interface IOrganizationStoreContractDriver
{
    IOrganizationStore Store { get; }
    ValueTask ResetAsync();
}

public interface IDataPermissionScopeRuleStoreContractDriver
{
    IDataPermissionScopeRuleStore Store { get; }
    ValueTask ResetAsync();
}

public interface IDurableStoreContractDriver
{
    ValueTask ReconstructProviderAsync();
    ValueTask<ProcessScenarioResult> RunProcessScenarioAsync(SaveSurface surface, DurableScenario scenario);
}
