using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Parsing;
using CrestCreates.DescriptorDraft.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Authoring.Tests;

/// <summary>
/// S1-R1-01: <c>JsonDocument</c> accepts an unpaired surrogate escape and only fails when the raw text is
/// decoded, and that failure is an <see cref="InvalidOperationException"/> rather than a JsonException. The
/// reviewer reproduced it on five raw reads, so each one needs a classified result here — together with the
/// controls that keep legal escapes working and the established stage precedence intact. These literals are
/// raw strings, so <c>\uD800</c> reaches the parser as the escape sequence itself.
/// </summary>
public sealed class OutputParserRawStringDecodeTests
{
    private const string Hash = "abc123hash";

    private const string DraftsHead =
        """
        {"contractVersion":"8a.v1","promptInputHash":"abc123hash","outcome":"drafts",
        "plan":{"planId":"plan_rd","intentText":"Add finance review"},
        "items":[
        """;

    private const string ValidItem =
        """
        {"descriptorKind":"HumanTask","descriptorId":"ht_1","operation":"Create",
        "payload":{"id":"ht_1","name":"Finance Review","version":1}}
        """;

    private const string EnvelopeTail =
        """
        ]}
        """;

    private readonly JsonDescriptorAuthoringOutputParser _parser = new();

    private static readonly DescriptorAuthoringParseContext Context = new()
    {
        TenantId = "tenant-raw-decode",
        AuthorId = "llm-descriptor-authoring-agent",
        AuthorKind = DescriptorDraftAuthorKind.Agent,
        CreatedAt = DateTimeOffset.UnixEpoch,
        IntentText = "Add finance review",
        ExpectedPromptInputHash = Hash
    };

    [Fact]
    public void RawDecode_ContractVersionValue_IsClassifiedNotThrown()
    {
        var json =
            """
            {"contractVersion":"\uD800","promptInputHash":"abc123hash","outcome":"drafts",
            "plan":{"planId":"plan_rd","intentText":"Add finance review"},
            "items":[
            """
            + ValidItem + EnvelopeTail;

        var result = _parser.Parse(json, Context);

        result.Status.Should().Be(DescriptorAuthoringStatus.InvalidProviderOutput);
        FailedMember(result, "contractVersion");
    }

    [Fact]
    public void RawDecode_PromptInputHashValue_IsClassifiedNotThrown()
    {
        var json =
            """
            {"contractVersion":"8a.v1","promptInputHash":"\uD800","outcome":"drafts",
            "plan":{"planId":"plan_rd","intentText":"Add finance review"},
            "items":[
            """
            + ValidItem + EnvelopeTail;

        var result = _parser.Parse(json, Context);

        result.Status.Should().Be(DescriptorAuthoringStatus.InvalidProviderOutput);
        FailedMember(result, "promptInputHash");
    }

    [Fact]
    public void RawDecode_OutcomeValue_IsClassifiedNotThrown()
    {
        var json =
            """
            {"contractVersion":"8a.v1","promptInputHash":"abc123hash","outcome":"\uD800",
            "plan":{"planId":"plan_rd","intentText":"Add finance review"},
            "items":[
            """
            + ValidItem + EnvelopeTail;

        var result = _parser.Parse(json, Context);

        result.Status.Should().Be(DescriptorAuthoringStatus.InvalidProviderOutput);
        FailedMember(result, "outcome");
    }

    [Fact]
    public void RawDecode_RootMemberName_IsClassifiedNotThrown()
    {
        var json =
            """
            {"contractVersion":"8a.v1","promptInputHash":"abc123hash","outcome":"unsupported","\uD800":"value",
            "unsupported":{"reasons":[{"code":"CAPABILITY_UNSUPPORTED","message":"No domain algorithm exists."}]}}
            """;

        var result = _parser.Parse(json, Context);

        result.Status.Should().Be(DescriptorAuthoringStatus.InvalidProviderOutput);
        FailedName(result, "envelope");
    }

    [Fact]
    public void RawDecode_ItemMemberName_IsClassifiedNotThrown()
    {
        var json = DraftsHead
            + """
              {"descriptorKind":"HumanTask","\uD800":"ht_1","operation":"Create",
              "payload":{"id":"ht_1","name":"Finance Review","version":1}}
              """
            + EnvelopeTail;

        var result = _parser.Parse(json, Context);

        result.Status.Should().Be(DescriptorAuthoringStatus.InvalidProviderOutput);
        FailedName(result, "envelope.items[0]");
    }

    [Fact]
    public void RawDecode_PayloadMemberName_IsClassifiedNotThrown()
    {
        var json = DraftsHead
            + """
              {"descriptorKind":"HumanTask","descriptorId":"ht_1","operation":"Create",
              "payload":{"id":"ht_1","name":"Finance Review","\uD800":"nested","version":1}}
              """
            + EnvelopeTail;

        var result = _parser.Parse(json, Context);

        result.Status.Should().Be(DescriptorAuthoringStatus.InvalidProviderOutput);
        FailedName(result, "envelope.items[0].payload");
    }

    [Fact]
    public void RawDecode_NestedPayloadMemberName_IsClassifiedNotThrown()
    {
        var json = DraftsHead
            + """
              {"descriptorKind":"HumanTask","descriptorId":"ht_1","operation":"Create",
              "payload":{"id":"ht_1","version":1,"target":{"\uD800":1}}}
              """
            + EnvelopeTail;

        var result = _parser.Parse(json, Context);

        result.Status.Should().Be(DescriptorAuthoringStatus.InvalidProviderOutput);
        FailedName(result, "envelope.items[0].payload.target");
    }

    [Fact]
    public void RawDecode_UndecodableNameWithWrongHash_FailsAtTheEnvelopeStage()
    {
        var json =
            """
            {"contractVersion":"8a.v1","promptInputHash":"WRONG","outcome":"drafts","\uD800":"value",
            "plan":{"planId":"plan_rd","intentText":"Add finance review"},
            "items":[
            """
            + ValidItem + EnvelopeTail;

        var result = _parser.Parse(json, Context);

        // The envelope name inventory is decided before the hash comparison, so the decode failure keeps
        // that precedence: InvalidProviderOutput, not the hash-mismatch Blocked result.
        result.Status.Should().Be(DescriptorAuthoringStatus.InvalidProviderOutput);
        FailedName(result, "envelope");
    }

    [Fact]
    public void RawDecode_UndecodableNameAfterAnUnknownMember_KeepsTheVocabularyFactFirst()
    {
        var json =
            """
            {"contractVersion":"8a.v1","promptInputHash":"abc123hash","outcome":"drafts","confidence":"high","\uD800":"value",
            "plan":{"planId":"plan_rd","intentText":"Add finance review"},
            "items":[
            """
            + ValidItem + EnvelopeTail;

        var result = _parser.Parse(json, Context);

        // Both facts come from the same enumeration pass in document order, so the earlier declared-name
        // violation must stay the reported cause: the decode guard must not preempt the vocabulary check.
        result.Status.Should().Be(DescriptorAuthoringStatus.InvalidProviderOutput);
        var diagnostic = result.Diagnostics.Should().ContainSingle().Which;
        diagnostic.Code.Should().Be(DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput);
        diagnostic.Message.Should().Be("Envelope contains unknown member 'confidence' at envelope.");
    }

    [Fact]
    public void RawDecode_LegalSurrogatePairInUnsupportedMessage_DecodesAndTerminates()
    {
        var json =
            """
            {"contractVersion":"8a.v1","promptInputHash":"abc123hash","outcome":"unsupported",
            "unsupported":{"reasons":[{"code":"CAPABILITY_UNSUPPORTED","message":"No domain algorithm exists. \uD83D\uDE00"}]}}
            """;

        var result = _parser.Parse(json, Context);

        result.Status.Should().Be(DescriptorAuthoringStatus.UnsupportedIntent);
        result.Unsupported!.Reasons[0].Message.Should().Be("No domain algorithm exists. \U0001F600");
        result.DraftSet.Drafts.Should().BeEmpty();
    }

    [Fact]
    public void RawDecode_LegalSurrogatePairInPayloadName_DraftsStillSucceed()
    {
        var json = DraftsHead
            + """
              {"descriptorKind":"HumanTask","descriptorId":"ht_1","operation":"Create",
              "payload":{"id":"ht_1","name":"Finance Review \uD83D\uDE00","version":1}}
              """
            + EnvelopeTail;

        var result = _parser.Parse(json, Context);

        result.Status.Should().Be(DescriptorAuthoringStatus.Succeeded);
        result.DraftSet.Drafts.Should().ContainSingle();
    }

    [Fact]
    public void RawDecode_UndecodablePayloadInnerValue_KeepsTheExistingPerItemBoundary()
    {
        var json = DraftsHead
            + """
              {"descriptorKind":"HumanTask","descriptorId":"ht_1","operation":"Create",
              "payload":{"id":"ht_1","name":"Finance \uD800","version":1}}
              """
            + EnvelopeTail;

        var result = _parser.Parse(json, Context);

        // Payload content is not an envelope fact: it is materialized per item, and that pre-existing
        // boundary already classifies the failure instead of throwing.
        result.Status.Should().Be(DescriptorAuthoringStatus.Blocked);
        result.Diagnostics.Should().Contain(d =>
            d.Code == DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput
            && d.Message.Contains("Failed to materialize payload for item 'ht_1'"));
    }

    [Fact]
    public void RawDecode_UndecodableEntryMessage_IsClassifiedByTheStrictProjection()
    {
        var json =
            """
            {"contractVersion":"8a.v1","promptInputHash":"abc123hash","outcome":"unsupported",
            "unsupported":{"reasons":[{"code":"CAPABILITY_UNSUPPORTED","message":"No domain algorithm \uD800 exists."}]}}
            """;

        var result = _parser.Parse(json, Context);

        // Entry text is a projected value, so the Stage B boundary owns it; the decode failure is reported
        // as a deserialization failure rather than escaping as an exception.
        result.Status.Should().Be(DescriptorAuthoringStatus.InvalidProviderOutput);
        result.Diagnostics.Should().ContainSingle()
            .Which.Message.Should().StartWith("Failed to deserialize provider output as JSON:");
    }

    private static void FailedMember(DescriptorAuthoringResult result, string memberName)
    {
        var diagnostic = result.Diagnostics.Should().ContainSingle().Which;
        diagnostic.Code.Should().Be(DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput);
        diagnostic.Message.Should().Be($"Provider output member '{memberName}' cannot be decoded as UTF-16 text.");
    }

    private static void FailedName(DescriptorAuthoringResult result, string path)
    {
        var diagnostic = result.Diagnostics.Should().ContainSingle().Which;
        diagnostic.Code.Should().Be(DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput);
        diagnostic.Message.Should().Be($"Envelope contains a member name at {path} that cannot be decoded as UTF-16 text.");
    }
}
