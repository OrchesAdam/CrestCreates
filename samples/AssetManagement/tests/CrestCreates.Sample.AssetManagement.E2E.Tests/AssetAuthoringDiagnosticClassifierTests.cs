using FluentAssertions;

namespace CrestCreates.Sample.AssetManagement.E2E.Tests;

public sealed class AssetAuthoringDiagnosticClassifierTests
{
    [Fact]
    public void ClassifiesKnownParserDiagnostics_AndRedactsUnknownMessageContent()
    {
        var knownMessages = new[]
        {
            "Failed to deserialize provider output as JSON.",
            "Provider output deserialized to null.",
            "Provider output is missing required 'plan' section.",
            "Provider output contains no items.",
            "Unsupported contract version 'legacy'. Expected '7g.v1'."
        };

        knownMessages
            .Select(AssetAuthoringDiagnosticClassifier.Classify)
            .Should()
            .Equal(
                "JsonDeserializationFailed",
                "NullOutput",
                "MissingPlan",
                "NoItems",
                "ContractVersionMismatch");

        const string sensitiveMessage =
            "Unexpected parser failure: bearer-secret=do-not-leak; tenant=private-tenant";
        var classified = new[] { AssetAuthoringDiagnosticClassifier.Classify(sensitiveMessage) };
        var safeOutput = string.Join(",", classified);

        classified.Should().Equal("Other");
        safeOutput.Should().NotContain(sensitiveMessage);
        safeOutput.Should().NotContain("bearer-secret");
        safeOutput.Should().NotContain("private-tenant");
    }
}
