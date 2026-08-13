using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Accountability.Bootstrap;
using CrestCreates.Accountability.InMemory;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Accountability;
using CrestCreates.Agent.Memory.Accountability.Bootstrap;
using CrestCreates.Agent.Memory.Accountability.Production;
using CrestCreates.Metadata.Abstractions.Bootstrap;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CrestCreates.Agent.Memory.Accountability.Tests.Composition;

/// <summary>
/// §14.5 / §15.5 — the Accountability write bridge fails closed at startup when
/// the surrounding composition is incomplete (null producer, missing
/// Accountability runtime), the standalone read runtime stays on the explicit
/// null producer, and identical established facts reach Duplicate while changed
/// facts reach Conflict through the real DI container.
/// </summary>
public sealed class AgentMemoryAccountabilityCompositionTests
{
    private const string ValidatorTypeName = "AgentMemoryAccountabilityCompositionValidator";

    [Fact]
    public async Task EnabledBridge_Should_RejectNullProducerAtStartup()
    {
        using var provider = BuildEnabledBridgeProvider(null, services =>
        {
            // A host that smuggles the null producer back in after the bridge has
            // replaced it must still fail closed at startup.
            services.AddSingleton<IAgentMemoryAccountabilityProducer>(_ => new NullAgentMemoryAccountabilityProducer());
        });

        var validator = ResolveValidator(provider);

        await validator.Invoking(service => service.StartAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*AGENT_MEMORY_ACCOUNTABILITY_COMPOSITION_INVALID*")
            .WithMessage("*null producer*");
    }

    [Fact]
    public async Task EnabledBridge_Should_RejectMissingAccountabilityRuntimeAtStartup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAgentMemoryReadRuntime();
        services.AddAgentMemoryAccountability();
        using var provider = services.BuildServiceProvider();

        var validator = ResolveValidator(provider);

        await validator.Invoking(service => service.StartAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*AGENT_MEMORY_ACCOUNTABILITY_COMPOSITION_INVALID*")
            .WithMessage("*Accountability runtime marker*");
    }

    [Fact]
    public async Task EnabledBridge_WithCompleteComposition_Should_StartSuccessfully()
    {
        using var provider = BuildEnabledBridgeProvider(null);

        var validator = ResolveValidator(provider);

        await validator.Invoking(service => service.StartAsync(CancellationToken.None))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task Validator_Should_FailClosed_WithSameResult_ThroughBothEntryPoints()
    {
        using var provider = BuildEnabledBridgeProvider(null, services =>
        {
            services.AddSingleton<IAgentMemoryAccountabilityProducer>(_ => new NullAgentMemoryAccountabilityProducer());
        });

        var bootstrapValidator = provider.GetServices<IBootstrapValidator>()
            .Single(service => service.GetType().Name == ValidatorTypeName);
        var hostedValidator = ResolveValidator(provider);

        var report = bootstrapValidator.Validate();

        report.HasErrors.Should().BeTrue();
        var issue = report.Issues.Should().ContainSingle().Subject;
        issue.Code!.Value.RequireValue().Should().Be("AGENT_MEMORY_ACCOUNTABILITY_COMPOSITION_INVALID");
        issue.Message.Should().Contain("null producer");

        await hostedValidator.Invoking(service => service.StartAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*AGENT_MEMORY_ACCOUNTABILITY_COMPOSITION_INVALID*")
            .WithMessage("*null producer*");
    }

    [Fact]
    public void StandaloneRuntime_Should_UseExplicitNullProducer()
    {
        var services = new ServiceCollection();
        services.AddAgentMemoryReadRuntime();
        using var provider = services.BuildServiceProvider();

        var producer = provider.GetRequiredService<IAgentMemoryAccountabilityProducer>();

        producer.Should().BeAssignableTo<INullAgentMemoryAccountabilityProducer>();
        producer.GetType().Should().Be(typeof(NullAgentMemoryAccountabilityProducer));
    }

    [Fact]
    public async Task EstablishedFactRepublish_Should_ProduceDuplicate()
    {
        var logger = new AccountabilityTestFixture.RecordingLogger<AgentMemoryAccountabilityProducer>();
        using var provider = BuildEnabledBridgeProvider(logger);
        var producer = provider.GetRequiredService<IAgentMemoryAccountabilityProducer>();
        var sink = provider.GetRequiredService<IAuditSink>().Should().BeOfType<InMemoryAuditSink>().Subject;

        var identity = AccountabilityTestFixture.CreateIdentity();
        var context = AccountabilityTestFixture.CreateContext();
        var payload = AccountabilityTestFixture.CreateRecallPayload();

        await producer.PublishRecallAsync(identity, context, payload);
        await producer.PublishRecallAsync(identity, context, payload);

        sink.GetRecords().Should().HaveCount(1);
        logger.HasMessage("AGENT_MEMORY_ACCOUNTABILITY_RECORDED").Should().BeTrue();
        logger.HasMessage("AGENT_MEMORY_ACCOUNTABILITY_DUPLICATE").Should().BeTrue();
        logger.HasMessage("AGENT_MEMORY_ACCOUNTABILITY_CONFLICT").Should().BeFalse();
    }

    [Fact]
    public async Task ChangedEstablishedFactRepublish_Should_ProduceConflict()
    {
        var logger = new AccountabilityTestFixture.RecordingLogger<AgentMemoryAccountabilityProducer>();
        using var provider = BuildEnabledBridgeProvider(logger);
        var producer = provider.GetRequiredService<IAgentMemoryAccountabilityProducer>();
        var sink = provider.GetRequiredService<IAuditSink>().Should().BeOfType<InMemoryAuditSink>().Subject;

        var identity = AccountabilityTestFixture.CreateIdentity();
        var context = AccountabilityTestFixture.CreateContext();
        var first = AccountabilityTestFixture.CreateRecallPayload(returnedCount: 2);
        var changed = AccountabilityTestFixture.CreateRecallPayload(returnedCount: 7);

        await producer.PublishRecallAsync(identity, context, first);
        await producer.PublishRecallAsync(identity, context, changed);

        sink.GetRecords().Should().HaveCount(1);
        logger.HasMessage("AGENT_MEMORY_ACCOUNTABILITY_RECORDED").Should().BeTrue();
        logger.HasMessage("AGENT_MEMORY_ACCOUNTABILITY_CONFLICT").Should().BeTrue();
    }

    private static ServiceProvider BuildEnabledBridgeProvider(
        AccountabilityTestFixture.RecordingLogger<AgentMemoryAccountabilityProducer>? logger,
        Action<ServiceCollection>? mutate = null)
    {
        var services = new ServiceCollection();
        if (logger is not null)
            services.AddSingleton<ILogger<AgentMemoryAccountabilityProducer>>(logger);
        services.AddLogging();
        services.AddAccountability();
        services.AddSingleton<IAuditSink>(new InMemoryAuditSink());
        services.AddAgentMemoryReadRuntime();
        services.AddAgentMemoryAccountability();
        mutate?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static IHostedService ResolveValidator(ServiceProvider provider)
        => provider.GetServices<IHostedService>()
            .Single(service => service.GetType().Name == ValidatorTypeName);
}
