namespace CrestCreates.Agent.Authoring.Abstractions.Authoring;

public enum DescriptorAuthoringStatus
{
    Succeeded = 0,
    SucceededWithDiagnostics = 1,
    Blocked = 2,
    InvalidProviderOutput = 3,
    ProviderUnavailable = 4,
    Failed = 5,

    /// <summary>The intent needs something outside the supported authoring surface; zero drafts, structured reasons.</summary>
    UnsupportedIntent = 6,

    /// <summary>Required information is missing; zero drafts, structured questions.</summary>
    NeedsClarification = 7
}
