using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Accountability.Bootstrap;
using CrestCreates.Accountability.InMemory;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Accountability;
using CrestCreates.Agent.Memory.Accountability.Bootstrap;
using CrestCreates.Agent.Memory.Accountability.Production;
using CrestCreates.Agent.Memory.ReadCore.Accountability;
using CrestCreates.Capability.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CrestCreates.Agent.Memory.Accountability.Tests.Composition;

/// <summary>
/// §8.5 / §14.5 / §15.5 — first-party Agent Tool and MCP Memory facts derive
/// correlation / causation / parent from the matching Capability execution via
/// the shared <see cref="AgentMemoryCapabilityCausalityMapper"/>, never from a
/// second Agent/MCP-derived chain. Fresh Capability executions allocate fresh
/// Memory operation identities; a reused Memory OperationId under a different
/// Capability execution reaches the durable sink as Conflict.
/// </summary>
public sealed class AgentMemoryCapabilityCompositionTests
{
    private const string Correlation = "correlation-1";
    private const string Execution1 = "capability-execution-1";
    private const string Execution2 = "capability-execution-2";
    private const string Tenant = "tenant-a";
    private const string ActorKind = "agent";
    private const string ActorId = "actor-1";
    private const string EnclosingAuditId = "audit-parent-1";

    [Fact]
    public async Task AgentToolCapabilityMemoryFacts_ShouldRemainDistinct()
    {
        var logger = new AccountabilityTestFixture.RecordingLogger<AgentMemoryAccountabilityProducer>();
        using var provider = BuildBridgeProvider(logger);
        var producer = provider.GetRequiredService<IAgentMemoryAccountabilityProducer>();
        var sink = provider.GetRequiredService<IAuditSink>().Should().BeOfType<InMemoryAuditSink>().Subject;
        var identities = provider.GetRequiredService<IAgentMemoryOperationIdentityFactory>();

        // Two Agent Tool operations entering the SAME Capability execution but
        // each allocating a fresh Memory operation identity (C10 independence).
        var first = await ComposeAndPublishAsync(producer, sink, identities, Correlation, Execution1);
        var second = await ComposeAndPublishAsync(producer, sink, identities, Correlation, Execution1);

        sink.GetRecords().Should().HaveCount(2);
        first.OperationId.Should().NotBe(second.OperationId);
        first.AuditId.Should().NotBe(second.AuditId);
    }

    [Fact]
    public async Task AgentToolCapabilityMemoryFacts_ShouldShareExactCausality()
    {
        var logger = new AccountabilityTestFixture.RecordingLogger<AgentMemoryAccountabilityProducer>();
        using var provider = BuildBridgeProvider(logger);
        var producer = provider.GetRequiredService<IAgentMemoryAccountabilityProducer>();
        var sink = provider.GetRequiredService<IAuditSink>().Should().BeOfType<InMemoryAuditSink>().Subject;
        var identities = provider.GetRequiredService<IAgentMemoryOperationIdentityFactory>();

        var published = await ComposeAndPublishAsync(producer, sink, identities, Correlation, Execution1);

        // Memory correlation = Capability correlation; Memory causation =
        // Capability execution id; Memory parent = ambient enclosing audit id.
        published.Envelope.CorrelationId.Should().Be(Correlation);
        published.Envelope.CausationId.Should().Be(Execution1);
        published.Envelope.ParentAuditId.Should().Be(EnclosingAuditId);
        published.Envelope.Runtime.ExecutionId.Should().Be(published.OperationId);
    }

    [Fact]
    public async Task AgentToolCapabilityExecution_ShouldAllocateFreshMemoryOperationIdentity()
    {
        var logger = new AccountabilityTestFixture.RecordingLogger<AgentMemoryAccountabilityProducer>();
        using var provider = BuildBridgeProvider(logger);
        var producer = provider.GetRequiredService<IAgentMemoryAccountabilityProducer>();
        var sink = provider.GetRequiredService<IAuditSink>().Should().BeOfType<InMemoryAuditSink>().Subject;
        var identities = provider.GetRequiredService<IAgentMemoryOperationIdentityFactory>();

        // A fresh Capability execution allocates a fresh Memory operation id, so
        // the fact is independently Accepted and never a prior-fact Duplicate.
        var first = await ComposeAndPublishAsync(producer, sink, identities, Correlation, Execution1);
        var second = await ComposeAndPublishAsync(producer, sink, identities, Correlation, Execution2);

        first.OperationId.Should().NotBe(second.OperationId);
        sink.GetRecords().Should().HaveCount(2);
        logger.HasMessage("AGENT_MEMORY_ACCOUNTABILITY_RECORDED").Should().BeTrue();
        logger.HasMessage("AGENT_MEMORY_ACCOUNTABILITY_DUPLICATE").Should().BeFalse();
        logger.HasMessage("AGENT_MEMORY_ACCOUNTABILITY_CONFLICT").Should().BeFalse();
    }

    [Fact]
    public async Task McpCapabilityMemoryFacts_ShouldShareExactCausality()
    {
        var logger = new AccountabilityTestFixture.RecordingLogger<AgentMemoryAccountabilityProducer>();
        using var provider = BuildBridgeProvider(logger);
        var producer = provider.GetRequiredService<IAgentMemoryAccountabilityProducer>();
        var sink = provider.GetRequiredService<IAuditSink>().Should().BeOfType<InMemoryAuditSink>().Subject;
        var identities = provider.GetRequiredService<IAgentMemoryOperationIdentityFactory>();

        var published = await ComposeAndPublishAsync(producer, sink, identities, Correlation, Execution1);

        // Same authoritative mapping regardless of first-party source: MCP facts
        // carry the Capability correlation / execution id / parent audit id.
        published.Envelope.CorrelationId.Should().Be(Correlation);
        published.Envelope.CausationId.Should().Be(Execution1);
        published.Envelope.ParentAuditId.Should().Be(EnclosingAuditId);
        published.Envelope.Runtime.ExecutionId.Should().Be(published.OperationId);
    }

    [Fact]
    public async Task McpCapabilityExecution_ShouldAllocateFreshMemoryOperationIdentity()
    {
        var logger = new AccountabilityTestFixture.RecordingLogger<AgentMemoryAccountabilityProducer>();
        using var provider = BuildBridgeProvider(logger);
        var producer = provider.GetRequiredService<IAgentMemoryAccountabilityProducer>();
        var sink = provider.GetRequiredService<IAuditSink>().Should().BeOfType<InMemoryAuditSink>().Subject;
        var identities = provider.GetRequiredService<IAgentMemoryOperationIdentityFactory>();

        var first = await ComposeAndPublishAsync(producer, sink, identities, Correlation, Execution1);
        var second = await ComposeAndPublishAsync(producer, sink, identities, Correlation, Execution2);

        first.OperationId.Should().NotBe(second.OperationId);
        sink.GetRecords().Should().HaveCount(2);
        logger.HasMessage("AGENT_MEMORY_ACCOUNTABILITY_RECORDED").Should().BeTrue();
        logger.HasMessage("AGENT_MEMORY_ACCOUNTABILITY_DUPLICATE").Should().BeFalse();
    }

    [Fact]
    public void UpstreamOriginMismatch_ShouldFailBeforeMemoryExecution()
    {
        // F12: Agent/MCP logical origin disagrees with Capability binding
        // metadata — the mapper fails closed before any Memory domain execution.
        var context = MakeCapabilityContext(Correlation, Execution1);
        var ambient = MakeAmbient(correlationId: "unrelated-correlation");

        var act = () => AgentMemoryCapabilityCausalityMapper.FromCapability(context, ambient);

        var exception = act.Should().Throw<AgentMemoryCapabilityCausalityException>().Which;
        exception.Code.Should().Be("capability-ambient-correlation-mismatch");
    }

    [Fact]
    public async Task SameOperationIdUnderDifferentCapabilityExecution_ShouldConflict()
    {
        var logger = new AccountabilityTestFixture.RecordingLogger<AgentMemoryAccountabilityProducer>();
        using var provider = BuildBridgeProvider(logger);
        var producer = provider.GetRequiredService<IAgentMemoryAccountabilityProducer>();
        var sink = provider.GetRequiredService<IAuditSink>().Should().BeOfType<InMemoryAuditSink>().Subject;

        // F13: the SAME Memory OperationId is reused under a different Capability
        // execution — same Memory AuditId, changed complete RecordHash, Conflict.
        var identity = AccountabilityTestFixture.CreateIdentity();
        var firstContext = ComposeInvocationContext(Correlation, Execution1);
        var secondContext = ComposeInvocationContext("correlation-2", Execution2);

        await producer.PublishRecallAsync(identity, firstContext, AccountabilityTestFixture.CreateRecallPayload());
        await producer.PublishRecallAsync(identity, secondContext, AccountabilityTestFixture.CreateRecallPayload());

        sink.GetRecords().Should().HaveCount(1);
        logger.HasMessage("AGENT_MEMORY_ACCOUNTABILITY_RECORDED").Should().BeTrue();
        logger.HasMessage("AGENT_MEMORY_ACCOUNTABILITY_CONFLICT").Should().BeTrue();
        logger.HasMessage("AGENT_MEMORY_ACCOUNTABILITY_DUPLICATE").Should().BeFalse();
    }

    private static ServiceProvider BuildBridgeProvider(
        AccountabilityTestFixture.RecordingLogger<AgentMemoryAccountabilityProducer>? logger)
    {
        var services = new ServiceCollection();
        if (logger is not null)
            services.AddSingleton<ILogger<AgentMemoryAccountabilityProducer>>(logger);
        services.AddLogging();
        services.AddAccountability();
        services.AddSingleton<IAuditSink>(new InMemoryAuditSink());
        services.AddAgentMemoryReadRuntime();
        services.AddAgentMemoryAccountability();
        return services.BuildServiceProvider();
    }

    private static async Task<PublishedFact> ComposeAndPublishAsync(
        IAgentMemoryAccountabilityProducer producer,
        InMemoryAuditSink sink,
        IAgentMemoryOperationIdentityFactory identities,
        string correlation,
        string executionId)
    {
        var identity = identities.Create();
        var context = ComposeInvocationContext(correlation, executionId);
        await producer.PublishRecallAsync(
            identity,
            context,
            AccountabilityTestFixture.CreateRecallPayload(operationId: identity.OperationId));

        var envelope = sink.GetRecords()
            .Single(record => record.Runtime.ExecutionId == identity.OperationId);
        return new PublishedFact(identity.OperationId, envelope.AuditId, envelope);
    }

    private static AgentMemoryInvocationContext ComposeInvocationContext(string correlation, string executionId)
    {
        var capability = MakeCapabilityContext(correlation, executionId);
        var ambient = MakeAmbient(correlationId: correlation, operationId: executionId);
        var causality = AgentMemoryCapabilityCausalityMapper.FromCapability(capability, ambient);
        return new AgentMemoryInvocationContext
        {
            TenantId = Tenant,
            ActorId = ActorId,
            ActorKind = ActorKind,
            CorrelationId = causality.CorrelationId,
            CausationId = causality.CausationId,
            ParentAuditId = causality.ParentAuditId,
            InvocationSource = "agent"
        };
    }

    private static CapabilityExecutionContext MakeCapabilityContext(string correlationId, string executionId)
    {
        var context = new CapabilityExecutionContext
        {
            CapabilityId = "memory-recall",
            CapabilityName = "AgentMemoryRecall",
            CapabilityVersion = 1,
            CapabilityContractHash = "contract",
            CorrelationId = correlationId,
            TenantId = Tenant,
            AccountabilityActor = new AuditActor { Kind = ActorKind, Id = ActorId },
            ServiceProvider = new ServiceCollection().BuildServiceProvider()
        };

        var property = typeof(CapabilityExecutionContext).GetProperty("ExecutionId")
            ?? throw new InvalidOperationException("ExecutionId property not found");
        property.SetValue(context, executionId);
        return context;
    }

    private static AuditOperationContext MakeAmbient(string? correlationId = null, string? operationId = null)
        => new()
        {
            CorrelationId = correlationId!,
            OperationId = operationId!,
            EnclosingAuditId = EnclosingAuditId,
            TenantId = Tenant,
            Actor = new AuditActor { Kind = ActorKind, Id = ActorId },
            InvocationSource = ActorKind
        };

    private sealed record PublishedFact(string OperationId, string AuditId, AuditEnvelope Envelope);
}
