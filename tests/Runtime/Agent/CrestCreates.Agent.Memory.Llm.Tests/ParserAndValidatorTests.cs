using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Llm.Compression;
using CrestCreates.Agent.Memory.Llm.Extraction;
using CrestCreates.Agent.Memory.Llm.Prompting;
using CrestCreates.Agent.Memory.Llm.Validation;
using CrestCreates.Core.Abstractions.Identity;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Llm.Tests;

public class ParserAndValidatorTests
{
    [Fact]
    public void CompressionParser_ValidJson_ReturnsBlocks()
    {
        var parser = new JsonAgentMemoryCompressionOutputParser();
        var json = """{"blocks":[{"blockId":"b1","content":"summary","sourceRefIds":["s1"],"redactionKinds":null}]}""";

        var result = parser.Parse(json, ["s1"]);

        result.IsValid.Should().BeTrue();
        result.Blocks.Should().HaveCount(1);
        result.Blocks[0].BlockId.Should().Be("b1");
        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void CompressionParser_InvalidSourceRef_IsNotValid()
    {
        var parser = new JsonAgentMemoryCompressionOutputParser();
        var json = """{"blocks":[{"blockId":"b1","content":"summary","sourceRefIds":["s-unknown"],"redactionKinds":null}]}""";

        var result = parser.Parse(json, ["s1"]);

        result.IsValid.Should().BeFalse();
        result.Blocks.Should().HaveCount(1);
        result.Diagnostics.Should().Contain(d => d.Code == AgentMemoryLlmDiagnosticCodes.InvalidSourceRef);
    }

    [Fact]
    public void CompressionParser_EmptyInput_ReturnsError()
    {
        var parser = new JsonAgentMemoryCompressionOutputParser();

        var result = parser.Parse("", []);

        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == AgentMemoryLlmDiagnosticCodes.ProviderReturnedEmptyOutput);
    }

    [Fact]
    public void CompressionParser_InvalidJson_ReturnsParseFailed()
    {
        var parser = new JsonAgentMemoryCompressionOutputParser();

        var result = parser.Parse("not json", []);

        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == AgentMemoryLlmDiagnosticCodes.ParseFailed);
    }

    [Fact]
    public void ExtractionParser_ValidJson_ReturnsCandidates()
    {
        var parser = new JsonAgentMemoryExtractionOutputParser();
        var json = """{"candidates":[{"candidateId":"c1","content":"fact","sourceRefIds":["s1"],"kind":"Fact","confidence":"Medium","reasoning":"observed"}]}""";

        var result = parser.Parse(json, ["s1"]);

        result.IsValid.Should().BeTrue();
        result.Candidates.Should().HaveCount(1);
        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void ExtractionParser_AuthoritativeFlag_IsRejected()
    {
        var parser = new JsonAgentMemoryExtractionOutputParser();
        var json = """{"candidates":[{"candidateId":"c1","content":"fact","sourceRefIds":["s1"],"kind":"Fact","confidence":"Medium","reasoning":"observed","isAuthoritative":true}]}""";

        var result = parser.Parse(json, ["s1"]);

        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == AgentMemoryLlmDiagnosticCodes.NonAuthoritativeOutputEnforced);
    }

    [Fact]
    public void ExtractionParser_ActiveStatus_IsRejected()
    {
        var parser = new JsonAgentMemoryExtractionOutputParser();
        var json = """{"candidates":[{"candidateId":"c1","content":"fact","sourceRefIds":["s1"],"kind":"Fact","confidence":"Medium","reasoning":"observed","status":"Active"}]}""";

        var result = parser.Parse(json, ["s1"]);

        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == AgentMemoryLlmDiagnosticCodes.NonAuthoritativeOutputEnforced);
    }

    [Fact]
    public void ConfidenceCap_HighToMedium_CapsAndWarns()
    {
        var diagnostics = new List<AgentMemoryDiagnostic>();
        var result = AgentMemoryLlmOutputValidators.CapConfidence(
            AgentMemoryConfidence.High,
            AgentMemoryConfidence.Medium,
            diagnostics);

        result.Should().Be(AgentMemoryConfidence.Medium);
        diagnostics.Should().Contain(d => d.Code == AgentMemoryLlmDiagnosticCodes.CandidateConfidenceCapped);
    }

    [Fact]
    public void ConfidenceCap_LowToHigh_NoChange()
    {
        var diagnostics = new List<AgentMemoryDiagnostic>();
        var result = AgentMemoryLlmOutputValidators.CapConfidence(
            AgentMemoryConfidence.Low,
            AgentMemoryConfidence.High,
            diagnostics);

        result.Should().Be(AgentMemoryConfidence.Low);
        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void PromptBuilder_Compression_ProducesNonNullPrompt()
    {
        var builder = new DefaultAgentMemoryCompressionPromptBuilder();
        var input = new AgentMemoryCompressionPromptInput
        {
            TenantId = "t1",
            Sources = [new AgentMemoryCompressionPromptSource { SourceRefId = "s1", SanitizedContent = "sanitized content" }],
            MaxOutputCharacters = 1000,
            MaxOutputBlocks = 32
        };

        var prompt = builder.Build(input);

        prompt.Should().NotBeNullOrEmpty();
        prompt.Should().Contain("s1");
        prompt.Should().Contain("1000");
    }

    [Fact]
    public void PromptBuilder_Extraction_ProducesNonNullPrompt()
    {
        var builder = new DefaultAgentMemoryExtractionPromptBuilder();
        var input = new AgentMemoryExtractionPromptInput
        {
            TenantId = "t1",
            Blocks = [],
            MaxCandidateCount = 5
        };

        var prompt = builder.Build(input);

        prompt.Should().NotBeNullOrEmpty();
        prompt.Should().Contain("5");
    }
}
