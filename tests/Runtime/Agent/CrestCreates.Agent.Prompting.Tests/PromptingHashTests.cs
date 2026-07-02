using CrestCreates.Agent.Prompting;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.CanonicalHashing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Xunit;

namespace CrestCreates.Agent.Prompting.Tests;

public sealed class PromptingHashTests
{
    [Fact]
    public void SamePromptInput_ProducesStableHash()
    {
        var services = Services();
        var hashService = services.GetRequiredService<IAgentPromptHashService>();
        var request = Request(new TestPromptPayload("tenant-1", "intent"));

        var hash1 = hashService.ComputeInputHash(request);
        var hash2 = hashService.ComputeInputHash(request);

        hash1.Value.Should().Be(hash2.Value);
        hash1.ArtifactKind.Should().Be(CanonicalHashArtifactNames.AgentPromptInputEvidence);
        hash1.Purpose.Should().Be(CanonicalHashPurposeNames.SourceIdentity);
    }

    [Fact]
    public void TemplateVersionChange_ChangesInputHash()
    {
        var hashService = Services().GetRequiredService<IAgentPromptHashService>();

        var v1 = hashService.ComputeInputHash(Request(new TestPromptPayload("tenant-1", "intent"), version: "v1"));
        var v2 = hashService.ComputeInputHash(Request(new TestPromptPayload("tenant-1", "intent"), version: "v2"));

        v1.Value.Should().NotBe(v2.Value);
    }

    [Fact]
    public void ModelProfileRefChange_ChangesInputHash()
    {
        var hashService = Services().GetRequiredService<IAgentPromptHashService>();

        var hash1 = hashService.ComputeInputHash(Request(new TestPromptPayload("tenant-1", "intent"), modelRef: "model-a"));
        var hash2 = hashService.ComputeInputHash(Request(new TestPromptPayload("tenant-1", "intent"), modelRef: "model-b"));

        hash1.Value.Should().NotBe(hash2.Value);
    }

    [Fact]
    public void CorrelationAndActor_DoNotChangeInputHash()
    {
        var hashService = Services().GetRequiredService<IAgentPromptHashService>();

        var hash1 = hashService.ComputeInputHash(Request(new TestPromptPayload("tenant-1", "intent"), actorId: "actor-a", correlationId: "corr-a"));
        var hash2 = hashService.ComputeInputHash(Request(new TestPromptPayload("tenant-1", "intent"), actorId: "actor-b", correlationId: "corr-b"));

        hash1.Value.Should().Be(hash2.Value);
    }

    [Fact]
    public void OutputHash_UsesAuditEvidencePurpose()
    {
        var hashService = Services().GetRequiredService<IAgentPromptHashService>();
        var inputHash = hashService.ComputeInputHash(Request(new TestPromptPayload("tenant-1", "intent")));

        var outputHash = hashService.ComputeOutputHash(
            Request(new TestPromptPayload("tenant-1", "safe-output")),
            inputHash,
            new AgentPromptProviderObservation { ProviderName = "provider", ModelName = "model" });

        outputHash.Should().NotBeNull();
        outputHash!.ArtifactKind.Should().Be(CanonicalHashArtifactNames.AgentPromptOutputEvidence);
        outputHash.Purpose.Should().Be(CanonicalHashPurposeNames.AuditEvidence);
    }

    [Fact]
    public void MissingProjector_ThrowsInsteadOfUsingReflectionSerialization()
    {
        var hashService = Services(registerProjector: false).GetRequiredService<IAgentPromptHashService>();

        var act = () => hashService.ComputeInputHash(Request(new TestPromptPayload("tenant-1", "intent")));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IAgentPromptCanonicalPayloadProjector*");
    }

    private static ServiceProvider Services(bool registerProjector = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICanonicalHashComputer, DefaultCanonicalHashComputer>();
        services.AddAgentPrompting();
        if (registerProjector)
        {
            services.AddSingleton<IAgentPromptCanonicalPayloadProjector<TestPromptPayload>, TestPromptPayloadProjector>();
        }
        return services.BuildServiceProvider();
    }

    private static AgentPromptEvidenceCreationRequest<TestPromptPayload> Request(
        TestPromptPayload payload,
        string version = "v1",
        string modelRef = "model-default",
        string? actorId = null,
        string? correlationId = null) => new()
        {
            TemplateId = new AgentPromptTemplateId("descriptor-authoring"),
            TemplateVersion = new AgentPromptVersion(version),
            Purpose = AgentPromptPurpose.DescriptorAuthoring,
            ContractVersion = new AgentPromptContractVersion("7h.v1"),
            ModelProfileRef = new AgentPromptModelProfileRef(modelRef),
            ProviderProfileRef = new AgentPromptProviderProfileRef("provider-default"),
            Payload = payload,
            ActorId = actorId,
            CorrelationId = correlationId
        };

    private sealed record TestPromptPayload(string TenantId, string Intent);

    private sealed class TestPromptPayloadProjector : IAgentPromptCanonicalPayloadProjector<TestPromptPayload>
    {
        public void Write(Utf8JsonWriter writer, TestPromptPayload payload)
        {
            writer.WriteStartObject();
            writer.WriteString("tenantId", payload.TenantId);
            writer.WriteString("intent", payload.Intent);
            writer.WriteEndObject();
        }
    }
}
