namespace CrestCreates.Sample.AssetManagement.E2E.Tests;

internal static class AssetAuthoringDiagnosticClassifier
{
    public static string Classify(string? message)
    {
        return message switch
        {
            "Failed to deserialize provider output as JSON." => "JsonDeserializationFailed",
            "Provider output deserialized to null." => "NullOutput",
            "Provider output is missing required 'plan' section." => "MissingPlan",
            "Provider output contains no items." => "NoItems",
            _ when message?.StartsWith("Unsupported contract version", StringComparison.Ordinal) == true
                => "ContractVersionMismatch",
            _ => "Other"
        };
    }
}
