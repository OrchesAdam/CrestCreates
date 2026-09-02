namespace CrestCreates.Organization.Abstractions;

public enum OrganizationHierarchyFreshnessFailureKind
{
    Unknown = 0,
    InvalidGenerationOutcome = 1,
    GenerationRegression = 2,
    QuarantinedGenerationUnavailable = 3
}
