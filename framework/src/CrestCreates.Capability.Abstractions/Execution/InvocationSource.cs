namespace CrestCreates.Capability.Abstractions;

/// <summary>
/// Identifies the source of a capability invocation.
/// No Unknown value — callers must explicitly set the source.
/// </summary>
public enum InvocationSource
{
    Http,
    Workflow,
    HumanTask,
    Agent,
    Mcp,
    Event,
    BackgroundJob,
    Internal
}
