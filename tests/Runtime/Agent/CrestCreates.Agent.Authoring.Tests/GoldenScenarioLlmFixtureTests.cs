using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Abstractions.Prompting;
using CrestCreates.Agent.Authoring.Authoring;
using CrestCreates.Agent.Authoring.Clients;
using CrestCreates.Agent.Authoring.Tests.Fixtures;
using CrestCreates.Agent.Authoring.Parsing;
using CrestCreates.Agent.Authoring.Prompting;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Prompting;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Metadata.ContextPack.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace CrestCreates.Agent.Authoring.Tests;

public sealed class GoldenScenarioLlmFixtureTests
{
    [Fact]
    public async Task LlmFixture_ProducesDraftSet_WithHumanTaskAndWorkflow()
    {
        // 1. Set up service provider for prompting services
        var services = new ServiceCollection();
        services.AddSingleton<ICanonicalHashComputer, DefaultCanonicalHashComputer>();
        services.AddAgentPrompting();
        services.AddSingleton<IAgentPromptCanonicalPayloadProjector<DescriptorAuthoringPromptInput>, DescriptorAuthoringPromptInputProjector>();
        services.AddSingleton<IAgentPromptCanonicalPayloadProjector<DescriptorAuthoringModelResponseEvidenceProjection>, DescriptorAuthoringModelResponseEvidenceProjector>();
        var provider = services.BuildServiceProvider();
        var promptHashService = provider.GetRequiredService<IAgentPromptHashService>();
        var promptEvidenceFactory = provider.GetRequiredService<IAgentPromptEvidenceFactory>();

        var factory = new DefaultDescriptorAuthoringPromptInputFactory();
        var builder = new DefaultDescriptorAuthoringPromptBuilder();
        var parser = new JsonDescriptorAuthoringOutputParser();

        // 2. Create context
        var context = CreateCompanyCertificationContext();

        // 3. Compute prompt input hash to use as fixture key
        var rawPromptInput = factory.Create(context);
        var hashService = new DefaultDescriptorAuthoringPromptInputHashService(promptHashService);
        var hashValue = hashService.ComputeHash(rawPromptInput).Value;

        // 4. Create recorded client with fixture
        var fixtureJson = CompanyCertificationLlmFixture.GetRecordedOutput(hashValue);
        var fixtures = new Dictionary<string, string> { [hashValue] = fixtureJson };
        var recordedClient = new RecordedDescriptorAuthoringModelClient(fixtures);

        // 5. Create agent
        var options = Options.Create(new LlmDescriptorAuthoringAgentOptions());
        var timeProvider = TimeProvider.System;
        var agent = new LlmDescriptorAuthoringAgent(factory, builder, recordedClient, parser, promptEvidenceFactory, options, timeProvider);

        // 6. Run authoring
        var result = await agent.AuthorAsync(context);

        // 7. Verify
        result.Status.Should().Be(DescriptorAuthoringStatus.Succeeded);
        result.DraftSet.Drafts.Should().HaveCount(2);
        result.Plan.PlannedDescriptorRefs.Should().HaveCount(2);
        result.Plan.IntentText.Should().Contain("finance review");
    }

    [Fact]
    public async Task LlmFixture_DoesNotActivate_OrMutateRuntime()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICanonicalHashComputer, DefaultCanonicalHashComputer>();
        services.AddAgentPrompting();
        services.AddSingleton<IAgentPromptCanonicalPayloadProjector<DescriptorAuthoringPromptInput>, DescriptorAuthoringPromptInputProjector>();
        services.AddSingleton<IAgentPromptCanonicalPayloadProjector<DescriptorAuthoringModelResponseEvidenceProjection>, DescriptorAuthoringModelResponseEvidenceProjector>();
        var provider = services.BuildServiceProvider();
        var promptHashService = provider.GetRequiredService<IAgentPromptHashService>();
        var promptEvidenceFactory = provider.GetRequiredService<IAgentPromptEvidenceFactory>();

        var factory = new DefaultDescriptorAuthoringPromptInputFactory();
        var builder = new DefaultDescriptorAuthoringPromptBuilder();
        var parser = new JsonDescriptorAuthoringOutputParser();
        var context = CreateCompanyCertificationContext();

        var rawPromptInput = factory.Create(context);
        var hashService = new DefaultDescriptorAuthoringPromptInputHashService(promptHashService);
        var hashValue = hashService.ComputeHash(rawPromptInput).Value;

        var fixtureJson = CompanyCertificationLlmFixture.GetRecordedOutput(hashValue);
        var fixtures = new Dictionary<string, string> { [hashValue] = fixtureJson };
        var recordedClient = new RecordedDescriptorAuthoringModelClient(fixtures);
        var options = Options.Create(new LlmDescriptorAuthoringAgentOptions());
        var timeProvider = TimeProvider.System;
        var agent = new LlmDescriptorAuthoringAgent(factory, builder, recordedClient, parser, promptEvidenceFactory, options, timeProvider);

        var result = await agent.AuthorAsync(context);

        // The agent only produces draft proposals.
        // It does not activate, approve, mutate registries, or execute handlers.
        result.DraftSet.Should().NotBeNull();
        result.Plan.Should().NotBeNull();
        // No activation gate, runtime registry mutation, or handler execution is performed
        result.Status.Should().NotBe(DescriptorAuthoringStatus.Failed);
    }

    [Fact]
    public async Task LlmFixture_HumanTaskDraft_HasCorrectDescriptorId()
    {
        var agent = CreateAgentWithFixture();
        var context = CreateCompanyCertificationContext();

        var result = await agent.AuthorAsync(context);

        result.Status.Should().Be(DescriptorAuthoringStatus.Succeeded);
        var humanTaskDraft = result.DraftSet.Drafts.FirstOrDefault(d =>
            d.DescriptorKind == DescriptorKind.HumanTask);
        humanTaskDraft.Should().NotBeNull();
        humanTaskDraft!.DescriptorId.Should().Be("ht_finance_review_company_certification");
        humanTaskDraft.Operation.Should().Be(DescriptorDraftOperation.Create);
    }

    [Fact]
    public async Task LlmFixture_WorkflowDraft_HasCorrectDescriptorId()
    {
        var agent = CreateAgentWithFixture();
        var context = CreateCompanyCertificationContext();

        var result = await agent.AuthorAsync(context);

        result.Status.Should().Be(DescriptorAuthoringStatus.Succeeded);
        var workflowDraft = result.DraftSet.Drafts.FirstOrDefault(d =>
            d.DescriptorKind == DescriptorKind.Workflow);
        workflowDraft.Should().NotBeNull();
        workflowDraft!.DescriptorId.Should().Be("wf_company_certification");
        workflowDraft.Operation.Should().Be(DescriptorDraftOperation.Update);
    }

    private static LlmDescriptorAuthoringAgent CreateAgentWithFixture()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICanonicalHashComputer, DefaultCanonicalHashComputer>();
        services.AddAgentPrompting();
        services.AddSingleton<IAgentPromptCanonicalPayloadProjector<DescriptorAuthoringPromptInput>, DescriptorAuthoringPromptInputProjector>();
        services.AddSingleton<IAgentPromptCanonicalPayloadProjector<DescriptorAuthoringModelResponseEvidenceProjection>, DescriptorAuthoringModelResponseEvidenceProjector>();
        var provider = services.BuildServiceProvider();
        var promptHashService = provider.GetRequiredService<IAgentPromptHashService>();
        var promptEvidenceFactory = provider.GetRequiredService<IAgentPromptEvidenceFactory>();

        var factory = new DefaultDescriptorAuthoringPromptInputFactory();
        var builder = new DefaultDescriptorAuthoringPromptBuilder();
        var parser = new JsonDescriptorAuthoringOutputParser();
        var context = CreateCompanyCertificationContext();

        var rawPromptInput = factory.Create(context);
        var hashService = new DefaultDescriptorAuthoringPromptInputHashService(promptHashService);
        var hashValue = hashService.ComputeHash(rawPromptInput).Value;

        var fixtureJson = CompanyCertificationLlmFixture.GetRecordedOutput(hashValue);
        var fixtures = new Dictionary<string, string> { [hashValue] = fixtureJson };
        var recordedClient = new RecordedDescriptorAuthoringModelClient(fixtures);
        var options = Options.Create(new LlmDescriptorAuthoringAgentOptions());
        var timeProvider = TimeProvider.System;
        return new LlmDescriptorAuthoringAgent(factory, builder, recordedClient, parser, promptEvidenceFactory, options, timeProvider);
    }

    private static AgentAuthoringContext CreateCompanyCertificationContext()
    {
        return new AgentAuthoringContext
        {
            Request = new AgentAuthoringRequest
            {
                TenantId = "tenant-company-certification",
                IntentText = "Add second-level finance review before approving company certification."
            },
            MetadataContextPack = new MetadataContextPack
            {
                Request = new MetadataContextPackRequest
                {
                    Scope = MetadataContextPackScope.FocusOnly,
                    FocusDescriptors = Array.Empty<DescriptorRef>(),
                    TenantId = "tenant-company-certification"
                },
                Descriptors = Array.Empty<MetadataContextPackDescriptorEntry>(),
                Relationships = Array.Empty<MetadataContextPackRelationshipEntry>(),
                Summary = CreateEmptySummary(),
                Diagnostics = Array.Empty<MetadataContextPackDiagnostic>()
            },
            MemoryPack = new AgentMemoryPack
            {
                TenantId = "tenant-company-certification",
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
}
