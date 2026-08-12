using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.ReadCore.Accountability;
using CrestCreates.Capability.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Agent.Memory.ReadCore.Tests;

/// <summary>
/// §8.5 — the single shared mapping from an authoritative Capability execution
/// (or direct trusted-host call) into the Memory Accountability causal envelope.
/// First-party Agent Tool / MCP operations fail closed when the Capability
/// context or its matching ambient Accountability scope is missing or disagrees;
/// direct trusted-host calls adopt an ambient ParentAuditId only on a full match.
/// </summary>
public sealed class AgentMemoryCapabilityCausalityMapperTests
{
    private const string Correlation = "correlation-1";
    private const string Execution = "execution-1";
    private const string Tenant = "tenant-a";
    private const string EnclosingAuditId = "audit-1";
    private const string OpId = "operation-1";

    // FromCapability — fail-closed codes --------------------------------------

    [Fact]
    public void FromCapability_CorrelationMissing_ShouldThrowCapabilityCorrelationMissing()
    {
        var context = MakeCapabilityContext(correlationId: null);
        var ambient = MakeAmbient();

        var act = () => AgentMemoryCapabilityCausalityMapper.FromCapability(context, ambient);

        var exception = act.Should().Throw<AgentMemoryCapabilityCausalityException>().Which;
        exception.Code.Should().Be("capability-correlation-missing");
    }

    [Fact]
    public void FromCapability_ExecutionMissing_ShouldThrowCapabilityExecutionMissing()
    {
        // ExecutionId has an internal setter and ReadCore.Tests has no
        // InternalsVisibleTo, so leave the default null to simulate the missing
        // Capability execution id.
        var context = MakeCapabilityContext(correlationId: Correlation, executionId: null);
        var ambient = MakeAmbient();

        var act = () => AgentMemoryCapabilityCausalityMapper.FromCapability(context, ambient);

        var exception = act.Should().Throw<AgentMemoryCapabilityCausalityException>().Which;
        exception.Code.Should().Be("capability-execution-missing");
    }

    [Fact]
    public void FromCapability_AmbientAuditMissing_ShouldThrowCapabilityAmbientAuditMissing()
    {
        var context = MakeCapabilityContext(correlationId: Correlation, executionId: Execution);

        var act = () => AgentMemoryCapabilityCausalityMapper.FromCapability(context, ambient: null);

        var exception = act.Should().Throw<AgentMemoryCapabilityCausalityException>().Which;
        exception.Code.Should().Be("capability-ambient-audit-missing");
    }

    [Fact]
    public void FromCapability_CorrelationMismatch_ShouldThrowCapabilityAmbientCorrelationMismatch()
    {
        var context = MakeCapabilityContext(correlationId: Correlation, executionId: Execution);
        var ambient = MakeAmbient(correlationId: "different-correlation");

        var act = () => AgentMemoryCapabilityCausalityMapper.FromCapability(context, ambient);

        var exception = act.Should().Throw<AgentMemoryCapabilityCausalityException>().Which;
        exception.Code.Should().Be("capability-ambient-correlation-mismatch");
    }

    [Fact]
    public void FromCapability_OperationMismatch_ShouldThrowCapabilityAmbientOperationMismatch()
    {
        var context = MakeCapabilityContext(correlationId: Correlation, executionId: Execution);
        var ambient = MakeAmbient(operationId: "different-operation");

        var act = () => AgentMemoryCapabilityCausalityMapper.FromCapability(context, ambient);

        var exception = act.Should().Throw<AgentMemoryCapabilityCausalityException>().Which;
        exception.Code.Should().Be("capability-ambient-operation-mismatch");
    }

    [Fact]
    public void FromCapability_TenantMismatch_ShouldThrowCapabilityAmbientTenantMismatch()
    {
        var context = MakeCapabilityContext(correlationId: Correlation, executionId: Execution);
        var ambient = MakeAmbient(tenantId: "other-tenant");

        var act = () => AgentMemoryCapabilityCausalityMapper.FromCapability(context, ambient);

        var exception = act.Should().Throw<AgentMemoryCapabilityCausalityException>().Which;
        exception.Code.Should().Be("capability-ambient-tenant-mismatch");
    }

    [Fact]
    public void FromCapability_ActorMismatch_ShouldThrowCapabilityAmbientActorMismatch()
    {
        var context = MakeCapabilityContext(correlationId: Correlation, executionId: Execution);
        context.AccountabilityActor = new AuditActor { Kind = "agent", Id = "actor-1" };
        var ambient = MakeAmbient(actorKind: "agent", actorId: "actor-2");

        var act = () => AgentMemoryCapabilityCausalityMapper.FromCapability(context, ambient);

        var exception = act.Should().Throw<AgentMemoryCapabilityCausalityException>().Which;
        exception.Code.Should().Be("capability-ambient-actor-mismatch");
    }

    [Fact]
    public void FromCapability_ActorNullOnContext_ShouldNotRequireAmbientActor()
    {
        var context = MakeCapabilityContext(correlationId: Correlation, executionId: Execution);
        context.AccountabilityActor = null;
        var ambient = MakeAmbient();

        var result = AgentMemoryCapabilityCausalityMapper.FromCapability(context, ambient);

        result.CorrelationId.Should().Be(Correlation);
        result.CausationId.Should().Be(Execution);
        result.ParentAuditId.Should().Be(EnclosingAuditId);
    }

    [Fact]
    public void FromCapability_AllAgree_ShouldMapCorrelationCausationAndParent()
    {
        var context = MakeCapabilityContext(correlationId: Correlation, executionId: Execution);
        context.AccountabilityActor = new AuditActor { Kind = "agent", Id = "actor-1" };
        var ambient = MakeAmbient(actorKind: "agent", actorId: "actor-1");

        var result = AgentMemoryCapabilityCausalityMapper.FromCapability(context, ambient);

        result.CorrelationId.Should().Be(Correlation);
        result.CausationId.Should().Be(Execution);
        result.ParentAuditId.Should().Be(EnclosingAuditId);
    }

    // FromDirectHost — lenient, parent adopted only on full match -------------

    [Fact]
    public void FromDirectHost_NoAmbient_ShouldPreserveSuppliedIdentityAndNullParent()
    {
        var context = MakeInvocationContext(correlationId: Correlation, causationId: OpId);

        var result = AgentMemoryCapabilityCausalityMapper.FromDirectHost(context, ambient: null);

        result.CorrelationId.Should().Be(Correlation);
        result.CausationId.Should().Be(OpId);
        result.ParentAuditId.Should().BeNull();
    }

    [Fact]
    public void FromDirectHost_NullCorrelationAndCausation_ShouldMapToEmptyStrings()
    {
        var context = MakeInvocationContext(correlationId: null, causationId: null);

        var result = AgentMemoryCapabilityCausalityMapper.FromDirectHost(context, ambient: null);

        result.CorrelationId.Should().BeEmpty();
        result.CausationId.Should().BeEmpty();
        result.ParentAuditId.Should().BeNull();
    }

    [Fact]
    public void FromDirectHost_TenantMismatch_ShouldNotAdoptParent()
    {
        var context = MakeInvocationContext(correlationId: Correlation, causationId: OpId);
        var ambient = MakeAmbient(tenantId: "other-tenant", operationId: OpId);

        var result = AgentMemoryCapabilityCausalityMapper.FromDirectHost(context, ambient);

        result.CorrelationId.Should().Be(Correlation);
        result.CausationId.Should().Be(OpId);
        result.ParentAuditId.Should().BeNull();
    }

    [Fact]
    public void FromDirectHost_CorrelationMismatch_ShouldNotAdoptParent()
    {
        var context = MakeInvocationContext(correlationId: Correlation, causationId: OpId);
        var ambient = MakeAmbient(correlationId: "different-correlation", operationId: OpId);

        var result = AgentMemoryCapabilityCausalityMapper.FromDirectHost(context, ambient);

        result.ParentAuditId.Should().BeNull();
    }

    [Fact]
    public void FromDirectHost_OperationMismatch_ShouldNotAdoptParent()
    {
        var context = MakeInvocationContext(correlationId: Correlation, causationId: OpId);
        var ambient = MakeAmbient(operationId: "unrelated-operation");

        var result = AgentMemoryCapabilityCausalityMapper.FromDirectHost(context, ambient);

        result.ParentAuditId.Should().BeNull();
    }

    [Fact]
    public void FromDirectHost_FullMatch_ShouldAdoptAmbientParentAuditId()
    {
        var context = MakeInvocationContext(correlationId: Correlation, causationId: OpId);
        var ambient = MakeAmbient(operationId: OpId);

        var result = AgentMemoryCapabilityCausalityMapper.FromDirectHost(context, ambient);

        result.CorrelationId.Should().Be(Correlation);
        result.CausationId.Should().Be(OpId);
        result.ParentAuditId.Should().Be(EnclosingAuditId);
    }

    // Helpers -----------------------------------------------------------------

    private static CapabilityExecutionContext MakeCapabilityContext(
        string? correlationId,
        string? executionId = null)
    {
        var context = new CapabilityExecutionContext
        {
            CapabilityId = "memory-recall",
            CapabilityName = "AgentMemoryRecall",
            CapabilityVersion = 1,
            CapabilityContractHash = "contract",
            CorrelationId = correlationId ?? string.Empty,
            TenantId = Tenant,
            AccountabilityActor = null,
            ServiceProvider = new ServiceCollection().BuildServiceProvider()
        };

        if (executionId is not null)
        {
            // ExecutionId has an internal setter and ReadCore.Tests is not in the
            // InternalsVisibleTo list for Capability.Abstractions, so set via reflection.
            var property = typeof(CapabilityExecutionContext).GetProperty("ExecutionId")
                ?? throw new InvalidOperationException("ExecutionId property not found");
            property.SetValue(context, executionId);
        }

        return context;
    }

    private static AuditOperationContext MakeAmbient(
        string? correlationId = Correlation,
        string? operationId = Execution,
        string? tenantId = Tenant,
        string? actorKind = "agent",
        string? actorId = "actor-1")
        => new()
        {
            CorrelationId = correlationId!,
            OperationId = operationId!,
            EnclosingAuditId = EnclosingAuditId,
            TenantId = tenantId,
            Actor = new AuditActor { Kind = actorKind!, Id = actorId! },
            InvocationSource = "agent"
        };

    private static AgentMemoryInvocationContext MakeInvocationContext(
        string? correlationId,
        string? causationId)
        => new()
        {
            TenantId = Tenant,
            ActorId = "actor-1",
            ActorKind = "agent",
            CorrelationId = correlationId,
            CausationId = causationId,
            InvocationSource = "agent"
        };
}
