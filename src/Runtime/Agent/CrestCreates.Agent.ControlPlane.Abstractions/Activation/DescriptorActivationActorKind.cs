using CrestCreates.Agent.ControlPlane.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Abstractions.Activation;

/// <summary>
/// Actor kind for descriptor activation operations.
/// Subset of AgentToolActorKind — Import/Generator cannot approve activation.
/// </summary>
public enum DescriptorActivationActorKind
{
    Human,
    Agent,
    System
}

/// <summary>
/// Conversion helpers between DescriptorActivationActorKind and AgentToolActorKind.
/// </summary>
public static class DescriptorActivationActorKindExtensions
{
    public static DescriptorActivationActorKind? FromAgentToolActorKind(AgentToolActorKind kind)
        => kind switch
        {
            AgentToolActorKind.Human => DescriptorActivationActorKind.Human,
            AgentToolActorKind.Agent => DescriptorActivationActorKind.Agent,
            AgentToolActorKind.System => DescriptorActivationActorKind.System,
            _ => null // Import, Generator → not eligible for activation approval
        };

    public static DescriptorActivationActorKind FromAgentToolActorKindOrThrow(AgentToolActorKind kind)
        => FromAgentToolActorKind(kind)
            ?? throw new InvalidOperationException(
                $"Actor kind '{kind}' is not eligible for descriptor activation operations.");
}
