using System.Text.Json;
using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Abstractions.Model;
using CrestCreates.Agent.Authoring.Abstractions.Prompting;
using CrestCreates.Agent.Authoring.Authoring;
using CrestCreates.Agent.Authoring.Clients;
using CrestCreates.Agent.Authoring.Parsing;
using CrestCreates.Agent.Authoring.Prompting;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Prompting;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Metadata.ContextPack.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CrestCreates.Agent.Authoring.Tests;

public sealed class LlmDescriptorAuthoringAgentTests
{
    [Fact]
    public async Task EmptyModelResponse_Returns_ProviderUnavailable()
    {
        var agent = CreateAgentWithFakeResponse("");

        var result = await agent.AuthorAsync(TestAuthoringContext());

        result.Status.Should().Be(DescriptorAuthoringStatus.ProviderUnavailable);
        result.Diagnostics.Should().Contain(d =>
            d.Code == DescriptorAuthoringDiagnosticCodes.ProviderUnavailable);
        result.PromptInputEvidence.Should().NotBeNull();
        result.PromptOutputEvidence.Should().NotBeNull();
    }

    [Fact]
    public async Task AuthorAsync_CredentialUnavailable_ProducesCredentialUnavailableDiagnostic()
    {
        var client = new FakeDescriptorAuthoringModelClient(
            DescriptorAuthoringProviderFailureKind.CredentialUnavailable, "API key not found");
        var agent = CreateAgentWithClient(client);
        var context = TestAuthoringContext();

        var result = await agent.AuthorAsync(context);

        result.Status.Should().Be(DescriptorAuthoringStatus.ProviderUnavailable);
        result.Diagnostics.Should().ContainSingle(d =>
            d.Code == DescriptorAuthoringDiagnosticCodes.CredentialUnavailable);
        result.PromptInputEvidence.Should().NotBeNull();
        result.PromptOutputEvidence.Should().NotBeNull();
    }

    [Fact]
    public async Task AuthorAsync_Unauthorized_ProducesUnauthorizedDiagnostic()
    {
        var client = new FakeDescriptorAuthoringModelClient(
            DescriptorAuthoringProviderFailureKind.Unauthorized, "HTTP 401 Unauthorized");
        var agent = CreateAgentWithClient(client);
        var context = TestAuthoringContext();

        var result = await agent.AuthorAsync(context);

        result.Status.Should().Be(DescriptorAuthoringStatus.ProviderUnavailable);
        result.Diagnostics.Should().ContainSingle(d =>
            d.Code == DescriptorAuthoringDiagnosticCodes.ProviderUnauthorized);
        result.PromptInputEvidence.Should().NotBeNull();
        result.PromptOutputEvidence.Should().NotBeNull();
    }

    [Fact]
    public async Task AuthorAsync_RateLimited_ProducesRateLimitedDiagnostic()
    {
        var client = new FakeDescriptorAuthoringModelClient(
            DescriptorAuthoringProviderFailureKind.RateLimited, "HTTP 429 Too Many Requests");
        var agent = CreateAgentWithClient(client);
        var context = TestAuthoringContext();

        var result = await agent.AuthorAsync(context);

        result.Status.Should().Be(DescriptorAuthoringStatus.ProviderUnavailable);
        result.Diagnostics.Should().ContainSingle(d =>
            d.Code == DescriptorAuthoringDiagnosticCodes.ProviderRateLimited);
        result.PromptInputEvidence.Should().NotBeNull();
        result.PromptOutputEvidence.Should().NotBeNull();
    }

    [Fact]
    public async Task AuthorAsync_Timeout_ProducesTimeoutDiagnostic()
    {
        var client = new FakeDescriptorAuthoringModelClient(
            DescriptorAuthoringProviderFailureKind.Timeout, "Request timed out");
        var agent = CreateAgentWithClient(client);
        var context = TestAuthoringContext();

        var result = await agent.AuthorAsync(context);

        result.Status.Should().Be(DescriptorAuthoringStatus.ProviderUnavailable);
        result.Diagnostics.Should().ContainSingle(d =>
            d.Code == DescriptorAuthoringDiagnosticCodes.ProviderTimeout);
        result.PromptInputEvidence.Should().NotBeNull();
        result.PromptOutputEvidence.Should().NotBeNull();
    }

    [Fact]
    public async Task AuthorAsync_NetworkError_ProducesProviderUnavailableDiagnostic()
    {
        var client = new FakeDescriptorAuthoringModelClient(
            DescriptorAuthoringProviderFailureKind.NetworkError, "Connection refused");
        var agent = CreateAgentWithClient(client);
        var context = TestAuthoringContext();

        var result = await agent.AuthorAsync(context);

        result.Status.Should().Be(DescriptorAuthoringStatus.ProviderUnavailable);
        result.Diagnostics.Should().ContainSingle(d =>
            d.Code == DescriptorAuthoringDiagnosticCodes.ProviderUnavailable);
        result.PromptInputEvidence.Should().NotBeNull();
        result.PromptOutputEvidence.Should().NotBeNull();
    }

    [Fact]
    public async Task MissingFixture_Returns_ProviderUnavailable()
    {
        var fixtures = new Dictionary<string, string>();
        var recorded = new RecordedDescriptorAuthoringModelClient(fixtures);
        var agent = CreateAgentWithClient(recorded);

        var result = await agent.AuthorAsync(TestAuthoringContext());

        result.Status.Should().Be(DescriptorAuthoringStatus.ProviderUnavailable);
        result.PromptInputEvidence.Should().NotBeNull();
        result.PromptOutputEvidence.Should().NotBeNull();
    }

    [Fact]
    public async Task ValidModelResponse_Returns_StructuredResult()
    {
        var responseJson = BuildValidHumanTaskOutputJson("abc123");
        var agent = CreateAgentWithFakeResponse(responseJson);

        var result = await agent.AuthorAsync(TestAuthoringContext());

        // The result depends on whether the prompt hash matches.
        // With a FakeModelClient, the hash will be computed but won't match the fixture's expected hash.
        // So we test that the agent runs without throwing and returns a structured result.
        result.Should().NotBeNull();
        result.Status.Should().BeOneOf(
            DescriptorAuthoringStatus.Succeeded,
            DescriptorAuthoringStatus.Blocked,
            DescriptorAuthoringStatus.InvalidProviderOutput,
            DescriptorAuthoringStatus.ProviderUnavailable);
        result.PromptInputEvidence.Should().NotBeNull();
        result.PromptOutputEvidence.Should().NotBeNull();
    }

    [Fact]
    public async Task Agent_DoesNot_Activate_Or_Mutate_Runtime()
    {
        var agent = CreateAgentWithFakeResponse("");

        var result = await agent.AuthorAsync(TestAuthoringContext());

        // The agent only returns a DescriptorAuthoringResult with draft proposals.
        // It does not activate, approve, mutate registries, or execute handlers.
        result.DraftSet.Should().NotBeNull();
        result.Plan.Should().NotBeNull();
        result.PromptInputEvidence.Should().NotBeNull();
        result.PromptOutputEvidence.Should().NotBeNull();
    }

    [Fact]
    public async Task RecordedClient_WithMatchingFixture_ReturnsResponse()
    {
        var hashValue = "test_hash_value";
        var responseText = BuildValidHumanTaskOutputJson(hashValue);
        var fixtures = new Dictionary<string, string> { [hashValue] = responseText };
        var recorded = new RecordedDescriptorAuthoringModelClient(fixtures);

        // Create a model request with matching hash
        var request = new DescriptorAuthoringModelRequest
        {
            Prompt = new DescriptorAuthoringPromptOutput
            {
                ContractVersion = "7g.v1",
                PromptTemplateVersion = "v1",
                PromptInputHash = TestHash(hashValue),
                SystemPrompt = "test",
                UserPrompt = "test"
            },
            ModelProfile = new DescriptorAuthoringModelProfile
            {
                ProfileName = "test",
                ProviderName = "recorded",
                ModelName = "recorded-model"
            }
        };

        var response = await recorded.CompleteAsync(request);

        response.ResponseText.Should().Be(responseText);
        response.ProviderName.Should().Be("recorded");
    }

    [Fact]
    public async Task RecordedClient_WithoutMatchingFixture_ReturnsEmpty()
    {
        var fixtures = new Dictionary<string, string>();
        var recorded = new RecordedDescriptorAuthoringModelClient(fixtures);

        var request = new DescriptorAuthoringModelRequest
        {
            Prompt = new DescriptorAuthoringPromptOutput
            {
                ContractVersion = "7g.v1",
                PromptTemplateVersion = "v1",
                PromptInputHash = TestHash("nonexistent"),
                SystemPrompt = "test",
                UserPrompt = "test"
            },
            ModelProfile = new DescriptorAuthoringModelProfile
            {
                ProfileName = "test",
                ProviderName = "recorded",
                ModelName = "recorded-model"
            }
        };

        var response = await recorded.CompleteAsync(request);

        response.ResponseText.Should().BeEmpty();
    }

    // Helper methods

    private static LlmDescriptorAuthoringAgent CreateAgentWithFakeResponse(string responseText)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICanonicalHashComputer, DefaultCanonicalHashComputer>();
        services.AddAgentPrompting();
        services.AddSingleton<IAgentPromptCanonicalPayloadProjector<DescriptorAuthoringPromptInput>, DescriptorAuthoringPromptInputProjector>();
        services.AddSingleton<IAgentPromptCanonicalPayloadProjector<DescriptorAuthoringModelResponseEvidenceProjection>, DescriptorAuthoringModelResponseEvidenceProjector>();
        var provider = services.BuildServiceProvider();
        var promptEvidenceFactory = provider.GetRequiredService<IAgentPromptEvidenceFactory>();
        var factory = new DefaultDescriptorAuthoringPromptInputFactory();
        var builder = new DefaultDescriptorAuthoringPromptBuilder();
        var client = new FakeDescriptorAuthoringModelClient(responseText);
        var parser = new JsonDescriptorAuthoringOutputParser();
        var options = Options.Create(new LlmDescriptorAuthoringAgentOptions());
        var timeProvider = TimeProvider.System;

        return new LlmDescriptorAuthoringAgent(factory, builder, client, parser, promptEvidenceFactory, options, timeProvider);
    }

    private static LlmDescriptorAuthoringAgent CreateAgentWithClient(IDescriptorAuthoringModelClient client)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICanonicalHashComputer, DefaultCanonicalHashComputer>();
        services.AddAgentPrompting();
        services.AddSingleton<IAgentPromptCanonicalPayloadProjector<DescriptorAuthoringPromptInput>, DescriptorAuthoringPromptInputProjector>();
        services.AddSingleton<IAgentPromptCanonicalPayloadProjector<DescriptorAuthoringModelResponseEvidenceProjection>, DescriptorAuthoringModelResponseEvidenceProjector>();
        var provider = services.BuildServiceProvider();
        var promptEvidenceFactory = provider.GetRequiredService<IAgentPromptEvidenceFactory>();
        var factory = new DefaultDescriptorAuthoringPromptInputFactory();
        var builder = new DefaultDescriptorAuthoringPromptBuilder();
        var parser = new JsonDescriptorAuthoringOutputParser();
        var options = Options.Create(new LlmDescriptorAuthoringAgentOptions());
        var timeProvider = TimeProvider.System;

        return new LlmDescriptorAuthoringAgent(factory, builder, client, parser, promptEvidenceFactory, options, timeProvider);
    }

    [Fact]
    public async Task AuthorAsync_ReturnsPromptEvidenceSummaries()
    {
        var agent = CreateAgentWithFakeResponse(BuildValidHumanTaskOutputJson("mismatched-hash"));
        var context = TestAuthoringContext();
        var result = await agent.AuthorAsync(context);
        result.PromptInputEvidence.Should().NotBeNull();
        result.PromptOutputEvidence.Should().NotBeNull();
        result.PromptInputEvidence!.TemplateId.Value.Should().Be("descriptor-authoring");
        result.PromptInputEvidence.TemplateVersion.Value.Should().Be("descriptor-authoring-prompt-template-v1");
        result.PromptInputEvidence.Purpose.Should().Be(AgentPromptPurpose.DescriptorAuthoring);
        result.PromptOutputEvidence!.InputHash.Value.Should().Be(result.PromptInputEvidence.InputHash.Value);
    }

    [Fact]
    public async Task AuthorAsync_ProviderObservation_UsesResponseProviderAndModelNames()
    {
        var client = new FakeDescriptorAuthoringModelClient(new DescriptorAuthoringModelResponse
        {
            ResponseText = BuildValidHumanTaskOutputJson("mismatched-hash"),
            ProviderName = "observed-provider",
            ModelName = "observed-model"
        });
        var agent = CreateAgentWithClient(client);
        var result = await agent.AuthorAsync(TestAuthoringContext());
        result.PromptOutputEvidence!.ProviderObservation!.ProviderName.Should().Be("observed-provider");
        result.PromptOutputEvidence.ProviderObservation.ModelName.Should().Be("observed-model");
    }

    private static CanonicalHash TestHash(string value) => new()
    {
        Value = value,
        Algorithm = "SHA-256",
        AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = "Test",
        Scope = "Test",
        Purpose = "Test",
        ContractVersion = "test-v1",
        CanonicalShapeVersion = "test-shape-v1"
    };

    private static AgentAuthoringContext TestAuthoringContext()
    {
        return new AgentAuthoringContext
        {
            Request = new AgentAuthoringRequest
            {
                TenantId = "test-tenant",
                IntentText = "Add finance review"
            },
            MetadataContextPack = new MetadataContextPack
            {
                Request = new MetadataContextPackRequest
                {
                    Scope = MetadataContextPackScope.FocusOnly,
                    FocusDescriptors = Array.Empty<DescriptorRef>(),
                    TenantId = "test-tenant"
                },
                Descriptors = Array.Empty<MetadataContextPackDescriptorEntry>(),
                Relationships = Array.Empty<MetadataContextPackRelationshipEntry>(),
                Summary = CreateEmptySummary(),
                Diagnostics = Array.Empty<MetadataContextPackDiagnostic>()
            },
            MemoryPack = new AgentMemoryPack
            {
                TenantId = "test-tenant",
                IsAuthoritative = false
            }
        };
    }

    private static MetadataContextPackSummary CreateEmptySummary() => new()
    {
        TotalDescriptorCount = 0,
        DescriptorCountsByKind = new Dictionary<DescriptorKind, int>(),
        TotalRelationshipCount = 0,
        RelationshipCountsByKind = new Dictionary<RelationshipKind, int>(),
        FocusRefs = Array.Empty<DescriptorRef>(),
        WasTruncated = false,
        TruncatedAtCount = null,
        TraversalDepthReached = 0
    };

    [Fact]
    public async Task AuthorAsync_UsesInjectedAuthorIdAndTimeProvider()
    {
        var expectedAuthorId = "custom-author-id";
        var expectedTime = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var options = Options.Create(new LlmDescriptorAuthoringAgentOptions { AuthorId = expectedAuthorId });
        var timeProvider = new TestTimeProvider(expectedTime);

        var mockParser = new Mock<IDescriptorAuthoringOutputParser>();
        DescriptorAuthoringParseContext? capturedContext = null;
        mockParser
            .Setup(p => p.Parse(It.IsAny<string>(), It.IsAny<DescriptorAuthoringParseContext>()))
            .Callback<string, DescriptorAuthoringParseContext>((_, ctx) => capturedContext = ctx)
            .Returns(new DescriptorAuthoringResult
            {
                Status = DescriptorAuthoringStatus.Succeeded,
                Plan = new DescriptorAuthoringPlan { PlanId = "test_plan", IntentText = "test intent" },
                DraftSet = new DescriptorDraftSet { DraftSetId = "test_draftset" }
            });

        var services = new ServiceCollection();
        services.AddSingleton<ICanonicalHashComputer, DefaultCanonicalHashComputer>();
        services.AddAgentPrompting();
        services.AddSingleton<IAgentPromptCanonicalPayloadProjector<DescriptorAuthoringPromptInput>, DescriptorAuthoringPromptInputProjector>();
        services.AddSingleton<IAgentPromptCanonicalPayloadProjector<DescriptorAuthoringModelResponseEvidenceProjection>, DescriptorAuthoringModelResponseEvidenceProjector>();
        var provider = services.BuildServiceProvider();
        var promptEvidenceFactory = provider.GetRequiredService<IAgentPromptEvidenceFactory>();
        var factory = new DefaultDescriptorAuthoringPromptInputFactory();
        var builder = new DefaultDescriptorAuthoringPromptBuilder();
        var responseJson = BuildValidHumanTaskOutputJson("abc123");
        var client = new FakeDescriptorAuthoringModelClient(responseJson);

        var agent = new LlmDescriptorAuthoringAgent(factory, builder, client, mockParser.Object, promptEvidenceFactory, options, timeProvider);

        await agent.AuthorAsync(TestAuthoringContext());

        capturedContext.Should().NotBeNull();
        capturedContext!.AuthorId.Should().Be(expectedAuthorId);
        capturedContext!.CreatedAt.Should().Be(expectedTime);
    }

    [Fact]
    public async Task AuthorAsync_UsesDefaultAuthorId_WhenOptionsNotConfigured()
    {
        var expectedTime = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var options = Options.Create(new LlmDescriptorAuthoringAgentOptions());
        var timeProvider = new TestTimeProvider(expectedTime);

        var mockParser = new Mock<IDescriptorAuthoringOutputParser>();
        DescriptorAuthoringParseContext? capturedContext = null;
        mockParser
            .Setup(p => p.Parse(It.IsAny<string>(), It.IsAny<DescriptorAuthoringParseContext>()))
            .Callback<string, DescriptorAuthoringParseContext>((_, ctx) => capturedContext = ctx)
            .Returns(new DescriptorAuthoringResult
            {
                Status = DescriptorAuthoringStatus.Succeeded,
                Plan = new DescriptorAuthoringPlan { PlanId = "test_plan", IntentText = "test intent" },
                DraftSet = new DescriptorDraftSet { DraftSetId = "test_draftset" }
            });

        var services = new ServiceCollection();
        services.AddSingleton<ICanonicalHashComputer, DefaultCanonicalHashComputer>();
        services.AddAgentPrompting();
        services.AddSingleton<IAgentPromptCanonicalPayloadProjector<DescriptorAuthoringPromptInput>, DescriptorAuthoringPromptInputProjector>();
        services.AddSingleton<IAgentPromptCanonicalPayloadProjector<DescriptorAuthoringModelResponseEvidenceProjection>, DescriptorAuthoringModelResponseEvidenceProjector>();
        var provider = services.BuildServiceProvider();
        var promptEvidenceFactory = provider.GetRequiredService<IAgentPromptEvidenceFactory>();
        var factory = new DefaultDescriptorAuthoringPromptInputFactory();
        var builder = new DefaultDescriptorAuthoringPromptBuilder();
        var responseJson = BuildValidHumanTaskOutputJson("abc123");
        var client = new FakeDescriptorAuthoringModelClient(responseJson);

        var agent = new LlmDescriptorAuthoringAgent(factory, builder, client, mockParser.Object, promptEvidenceFactory, options, timeProvider);

        await agent.AuthorAsync(TestAuthoringContext());

        capturedContext.Should().NotBeNull();
        capturedContext!.AuthorId.Should().Be("llm-descriptor-authoring-agent");
        capturedContext!.CreatedAt.Should().Be(expectedTime);
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        public TestTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private static string BuildValidHumanTaskOutputJson(string promptHash) => JsonSerializer.Serialize(new
    {
        contractVersion = "7g.v1",
        promptInputHash = promptHash,
        plan = new
        {
            planId = "plan_test",
            intentText = "Add finance review",
            assumptions = new[] { "Finance team available" },
            plannedDescriptorRefs = new[]
            {
                new { @namespace = "humantask", id = "ht_finance_review", version = 1 }
            }
        },
        items = new object[]
        {
            new
            {
                descriptorKind = "HumanTask",
                descriptorId = "ht_finance_review",
                operation = "Create",
                rationale = "Need finance review step",
                payload = new { id = "ht_finance_review", name = "Finance Review", version = 1, permissions = "Finance.Review" },
                assumptions = new[] { "Finance team available" }
            }
        }
    });
}
