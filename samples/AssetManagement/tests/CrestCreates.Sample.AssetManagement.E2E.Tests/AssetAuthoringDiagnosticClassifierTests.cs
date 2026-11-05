using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Parsing;
using CrestCreates.DescriptorDraft.Abstractions;
using FluentAssertions;

namespace CrestCreates.Sample.AssetManagement.E2E.Tests;

/// <summary>
/// The classifier's categories are asserted against diagnostics the **formal parser actually produced**, so a
/// mapping can never drift into describing a message the contract no longer emits. Before `8a.v1` this file
/// pinned hand-written literals, and two of them ("Provider output is missing required 'plan' section." and
/// "Provider output deserialized to null.") are no longer emitted at all — the stale mapping stayed green
/// while attributing nothing.
/// </summary>
public sealed class AssetAuthoringDiagnosticClassifierTests
{
    private const string Hash = "classify-hash";

    private static readonly DescriptorAuthoringParseContext Context = new()
    {
        TenantId = "tenant-classifier",
        AuthorId = "llm-descriptor-authoring-agent",
        AuthorKind = DescriptorDraftAuthorKind.Agent,
        CreatedAt = DateTimeOffset.UnixEpoch,
        IntentText = "Classify the authoring diagnostics",
        ExpectedPromptInputHash = Hash
    };

    [Theory]
    // The two explicit terminal outcomes are named by their own codes, not by a message.
    [InlineData("unsupported", "ExplicitUnsupportedIntent")]
    [InlineData("clarification", "ExplicitNeedsClarification")]
    // Rejected envelopes: each family is a message the 8a.v1 parser really writes.
    [InlineData("malformed", "JsonDeserializationFailed")]
    [InlineData("previousVersion", "ContractVersionMismatch")]
    [InlineData("missingOutcome", "OutcomeDiscriminatorMissing")]
    [InlineData("unknownOutcome", "OutcomeOutsideDiscriminator")]
    [InlineData("unknownMember", "EnvelopeUnknownMember")]
    [InlineData("duplicateMember", "EnvelopeDuplicateMember")]
    [InlineData("undecodableName", "EnvelopeUndecodableMember")]
    [InlineData("undecodableValue", "EnvelopeUndecodableMember")]
    [InlineData("nullHashMember", "PromptHashMemberMissing")]
    [InlineData("unknownReasonCode", "ClosedCodeTableViolation")]
    [InlineData("forbiddenItemsMember", "OutcomeShapeConflict")]
    [InlineData("missingItemPayload", "OutcomeShapeConflict")]
    [InlineData("noItems", "NoItems")]
    public void RealParserDiagnostics_AreClassifiedIntoTheExpectedFamily(string form, string expectedCategory)
    {
        var diagnostics = new JsonDescriptorAuthoringOutputParser()
            .Parse(Envelope(form), Context)
            .Diagnostics;

        Categories(diagnostics).Should().Contain(expectedCategory);
    }

    [Fact]
    public void OverBoundEntryText_IsClassifiedAsABoundViolation()
    {
        var overBound = """
            {"contractVersion":"8a.v1","promptInputHash":"classify-hash","outcome":"unsupported","unsupported":{"reasons":[{"code":"CAPABILITY_UNSUPPORTED","message":"
            """
            + new string('a', 401)
            + """
            ","evidenceRefs":["visible:wf_asset:1"]}]}}
            """;

        var diagnostics = new JsonDescriptorAuthoringOutputParser()
            .Parse(overBound, Context)
            .Diagnostics;

        Categories(diagnostics).Should().Equal("OutcomeEntryBoundViolation");
    }

    [Fact]
    public void ExplicitUnsupportedDiagnostics_ClassifyAsTheirOwnCategory_NotAsARejectedEnvelope()
    {
        var result = new JsonDescriptorAuthoringOutputParser().Parse(Envelope("unsupported"), Context);

        result.Status.Should().Be(DescriptorAuthoringStatus.UnsupportedIntent);
        Categories(result.Diagnostics).Should().Equal("ExplicitUnsupportedIntent");
    }

    [Fact]
    public void HashMismatch_IsClassifiedByCode_NotByItsMessage()
    {
        var result = new JsonDescriptorAuthoringOutputParser().Parse(Envelope("wrongHash"), Context);

        result.Status.Should().Be(DescriptorAuthoringStatus.Blocked);
        Categories(result.Diagnostics).Should().Equal("PromptHashMismatch");
    }

    [Fact]
    public void IntentLevelBlock_StillClassifiesByItsCode()
    {
        var result = new JsonDescriptorAuthoringOutputParser().Parse(Envelope("unknownKind"), Context);

        result.Status.Should().Be(DescriptorAuthoringStatus.Blocked);
        Categories(result.Diagnostics).Should().Equal("UnknownDescriptorKind");
    }

    [Fact]
    public void UnknownDiagnostic_IsRedacted_AndNeverCarriesMessageContent()
    {
        const string sensitiveMessage =
            "Unexpected parser failure: bearer-secret=do-not-leak; tenant=private-tenant";

        var classified = AssetAuthoringDiagnosticClassifier.Classify(
            DescriptorAuthoringDiagnosticCodes.InvalidProviderOutputValue,
            sensitiveMessage);

        classified.Should().Be("Other");
        classified.Should().NotContain("bearer-secret");
        classified.Should().NotContain("private-tenant");

        AssetAuthoringDiagnosticClassifier.Classify(null, sensitiveMessage).Should().Be("Other");
        AssetAuthoringDiagnosticClassifier.Classify("SOME_FUTURE_CODE", sensitiveMessage).Should().Be("Other");
    }

    private static string Envelope(string form) => form switch
    {
        "unsupported" => """
            {"contractVersion":"8a.v1","promptInputHash":"classify-hash","outcome":"unsupported","unsupported":{"reasons":[{"code":"CAPABILITY_UNSUPPORTED","message":"No condition-scoring domain algorithm exists.","evidenceRefs":["visible:wf_asset_maintenance_review:1"]}]}}
            """,
        "clarification" => """
            {"contractVersion":"8a.v1","promptInputHash":"classify-hash","outcome":"needsClarification","needsClarification":{"questions":[{"id":"risk-threshold","reasonCode":"MISSING_POLICY_INPUT","question":"Which risk threshold makes the auto-approval acceptable?","missingInputs":["riskThreshold"],"candidateValues":[]}]}}
            """,
        "malformed" => "{not-json",
        "previousVersion" => """
            {"contractVersion":"7g.v1","promptInputHash":"classify-hash","outcome":"unsupported","unsupported":{"reasons":[{"code":"CAPABILITY_UNSUPPORTED","message":"No domain algorithm exists."}]}}
            """,
        "missingOutcome" => """
            {"contractVersion":"8a.v1","promptInputHash":"classify-hash","unsupported":{}}
            """,
        "unknownOutcome" => """
            {"contractVersion":"8a.v1","promptInputHash":"classify-hash","outcome":"maybe","unsupported":{"reasons":[{"code":"CAPABILITY_UNSUPPORTED","message":"No domain algorithm exists."}]}}
            """,
        "unknownMember" => """
            {"contractVersion":"8a.v1","promptInputHash":"classify-hash","confidence":"high","outcome":"unsupported","unsupported":{"reasons":[{"code":"CAPABILITY_UNSUPPORTED","message":"No domain algorithm exists."}]}}
            """,
        "duplicateMember" => """
            {"contractVersion":"8a.v1","promptInputHash":"classify-hash","promptInputHash":"classify-hash","outcome":"unsupported","unsupported":{"reasons":[{"code":"CAPABILITY_UNSUPPORTED","message":"No domain algorithm exists."}]}}
            """,
        "undecodableName" => """
            {"contractVersion":"8a.v1","promptInputHash":"classify-hash","outcome":"unsupported","\uD800":"value","unsupported":{"reasons":[{"code":"CAPABILITY_UNSUPPORTED","message":"No domain algorithm exists."}]}}
            """,
        "undecodableValue" => """
            {"contractVersion":"\uD800","promptInputHash":"classify-hash","outcome":"unsupported","unsupported":{"reasons":[{"code":"CAPABILITY_UNSUPPORTED","message":"No domain algorithm exists."}]}}
            """,
        "nullHashMember" => """
            {"contractVersion":"8a.v1","promptInputHash":null,"outcome":"unsupported","unsupported":{"reasons":[{"code":"CAPABILITY_UNSUPPORTED","message":"No domain algorithm exists."}]}}
            """,
        "unknownReasonCode" => """
            {"contractVersion":"8a.v1","promptInputHash":"classify-hash","outcome":"unsupported","unsupported":{"reasons":[{"code":"TELEPORTATION_UNSUPPORTED","message":"No domain algorithm exists."}]}}
            """,
        "forbiddenItemsMember" => """
            {"contractVersion":"8a.v1","promptInputHash":"classify-hash","outcome":"unsupported","items":[],"unsupported":{"reasons":[{"code":"CAPABILITY_UNSUPPORTED","message":"No domain algorithm exists."}]}}
            """,
        "missingItemPayload" => """
            {"contractVersion":"8a.v1","promptInputHash":"classify-hash","outcome":"drafts","plan":{"planId":"plan_classify","intentText":"Classify"},"items":[{"descriptorKind":"HumanTask","descriptorId":"ht_1","operation":"Create"}]}
            """,
        "noItems" => """
            {"contractVersion":"8a.v1","promptInputHash":"classify-hash","outcome":"drafts","plan":{"planId":"plan_classify","intentText":"Classify"},"items":[]}
            """,
        "wrongHash" => """
            {"contractVersion":"8a.v1","promptInputHash":"wrong-hash","outcome":"needsClarification","needsClarification":{"questions":[{"id":"risk-threshold","reasonCode":"MISSING_POLICY_INPUT","question":"Which risk threshold?","missingInputs":["riskThreshold"],"candidateValues":[]}]}}
            """,
        "unknownKind" => """
            {"contractVersion":"8a.v1","promptInputHash":"classify-hash","outcome":"drafts","plan":{"planId":"plan_classify","intentText":"Classify"},"items":[{"descriptorKind":"Capability","descriptorId":"cap_new","operation":"Create","payload":{"name":"Condition scoring","version":1}}]}
            """,
        _ => throw new ArgumentException($"Unknown envelope form '{form}'.", nameof(form))
    };

    private static string[] Categories(IReadOnlyList<DescriptorAuthoringDiagnostic> diagnostics)
        => diagnostics
            .Select(diagnostic => AssetAuthoringDiagnosticClassifier.Classify(
                diagnostic.Code.Value,
                diagnostic.Message))
            .ToArray();
}
