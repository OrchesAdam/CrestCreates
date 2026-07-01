namespace CrestCreates.Agent.Authoring.Abstractions.Authoring;

public enum DescriptorAuthoringStatus
{
    Succeeded = 0,
    SucceededWithDiagnostics = 1,
    Blocked = 2,
    InvalidProviderOutput = 3,
    ProviderUnavailable = 4,
    Failed = 5
}
