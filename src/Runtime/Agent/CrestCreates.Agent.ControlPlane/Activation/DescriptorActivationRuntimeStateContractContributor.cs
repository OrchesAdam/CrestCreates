using CrestCreates.Agent.ControlPlane.Abstractions.Json;
using CrestCreates.Runtime.Persistence.Abstractions.State;

namespace CrestCreates.Agent.ControlPlane.Activation;

public sealed class DescriptorActivationRuntimeStateContractContributor : IRuntimeStateContractContributor
{
    public void Contribute(IRuntimeStateContractBuilder builder)
    {
        var roots = AgentControlPlaneToolJsonSerializerContext
            .AgentControlPlaneToolJsonSerializerContextRootManifest.AllDirectRootTypes;
        builder.Add(
            "crest.agent-control-plane/descriptor-activation-review-task-input/v1",
            AgentControlPlaneToolJsonSerializerContext.Default.DescriptorActivationReviewTaskInput,
            roots);
        builder.Add(
            "crest.agent-control-plane/descriptor-activation-review-decision/v1",
            AgentControlPlaneToolJsonSerializerContext.Default.DescriptorActivationReviewDecision,
            roots);
    }
}
