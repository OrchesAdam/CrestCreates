namespace CrestCreates.Capability.Abstractions;

public enum CapabilityExecutionStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    TimedOut,
    Compensated
}
