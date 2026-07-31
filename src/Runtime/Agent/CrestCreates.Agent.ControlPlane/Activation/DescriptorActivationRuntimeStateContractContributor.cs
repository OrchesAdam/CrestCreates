using CrestCreates.Runtime.Persistence.Abstractions.State;

namespace CrestCreates.Agent.ControlPlane.Activation;

public sealed class DescriptorActivationRuntimeStateContractContributor : IRuntimeStateContractContributor
{
    public void Contribute(IRuntimeStateContractBuilder builder)
    {
        var taskInputRoots = new HashSet<Type>
        {
            typeof(CrestCreates.Agent.ControlPlane.Abstractions.Activation.DescriptorActivationReviewTaskInput)
        };
        builder.Add(
            "crest.agent-control-plane/descriptor-activation-review-task-input/v1",
            DescriptorActivationRuntimeStateJsonSerializerContext.Default.DescriptorActivationReviewTaskInput,
            taskInputRoots);

        var decisionRoots = CrestCreates.Agent.ControlPlane.Abstractions.Json.AgentControlPlaneToolJsonSerializerContext
            .AgentControlPlaneToolJsonSerializerContextRootManifest.AllDirectRootTypes;
        builder.Add(
            "crest.agent-control-plane/descriptor-activation-review-decision/v1",
            CrestCreates.Agent.ControlPlane.Abstractions.Json.AgentControlPlaneToolJsonSerializerContext.Default.DescriptorActivationReviewDecision,
            decisionRoots);
    }
}
