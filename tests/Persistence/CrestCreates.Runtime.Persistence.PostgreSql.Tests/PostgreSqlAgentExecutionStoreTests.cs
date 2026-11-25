using System.Collections.Immutable;
using System.Text.Json;
using CrestCreates.Agent.Orchestration;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

[Collection(PostgreSqlRuntimeCollection.Name)]
public sealed class PostgreSqlAgentExecutionStoreTests(PostgreSqlRuntimeCollectionFixture fixture)
{
    [Fact]
    public async Task ScopedCasRejectsDuplicateCreateStaleAndSkippedRevisions()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await using var first = BuildProvider(lease.Options);
        await using var second = BuildProvider(lease.Options);
        var store = first.GetRequiredService<IAgentExecutionStore>();
        var competitor = second.GetRequiredService<IAgentExecutionStore>();
        var key = new AgentExecutionKey("tenant-a", Guid.NewGuid());
        var otherTenant = key with { TenantId = "Tenant-A" };
        var host = key with { TenantId = null };
        var execution = CreateExecution(key);

        (await store.TryCreateAsync(execution with { Revision = 1 }, default)).Should().BeFalse();
        (await store.TryCreateAsync(execution, default)).Should().BeTrue();
        (await competitor.TryCreateAsync(execution, default)).Should().BeFalse();
        (await store.FindAsync(otherTenant, default)).Should().BeNull();
        (await store.FindAsync(host, default)).Should().BeNull();
        (await store.TryReplaceAsync(CreateExecution(otherTenant) with { Revision = 1 }, 0, default)).Should().BeFalse();
        (await store.TryReplaceAsync(CreateExecution(host) with { Revision = 1 }, 0, default)).Should().BeFalse();
        (await store.TryCreateAsync(CreateExecution(otherTenant), default)).Should().BeTrue();
        (await store.TryCreateAsync(CreateExecution(host), default)).Should().BeTrue();
        (await store.TryReplaceAsync(execution with { Revision = 2 }, 0, default)).Should().BeFalse();

        var owner = execution.Runs[key.RootRunId].Owner;
        var root = execution.Runs[key.RootRunId];
        var left = execution with { Revision = 1, Runs = execution.Runs.SetItem(owner.RunId, root with { Task = "left" }) };
        var right = execution with { Revision = 1, Runs = execution.Runs.SetItem(owner.RunId, root with { Task = "right" }) };
        var results = await Task.WhenAll(
            store.TryReplaceAsync(left, 0, default).AsTask(),
            competitor.TryReplaceAsync(right, 0, default).AsTask());
        results.Should().ContainSingle(written => written);
        (await store.TryReplaceAsync(execution with { Revision = 1 }, 0, default)).Should().BeFalse();
        var actual = await store.FindAsync(key, default);
        actual!.Revision.Should().Be(1);
        actual.Runs[key.RootRunId].Task.Should().Be(results[0] ? "left" : "right");
        (await store.FindAsync(otherTenant, default))!.Revision.Should().Be(0);
        (await store.FindAsync(host, default))!.Revision.Should().Be(0);
    }

    [Fact]
    public async Task FreshProviderReloadsTypedCoordinationState()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        var key = new AgentExecutionKey("tenant-a", Guid.NewGuid());
        var initial = CreateExecution(key);
        var root = initial.Runs[key.RootRunId];
        var artifact = new AgentArtifactReference("artifact", "sha256:123");
        var wait = new AgentEventWait(Guid.NewGuid(), new("workflow", "completed", "subject"));
        var delivery = new AgentEventDelivery("event", key.TenantId, wait.Selector, artifact);
        var resumption = new AgentEventResumption(wait, delivery, root.Owner.Epoch);
        var nextWait = new AgentEventWait(Guid.NewGuid(), new("workflow", "completed", "next"));
        var newOwner = root.Owner with { AgentId = "recipient", Epoch = root.Owner.Epoch + 1 };
        var offer = new AgentHandoffOffer(Guid.NewGuid(), newOwner.AgentId, ["lookup"], [artifact]);
        var handoff = new AgentHandoffRecord(offer, root.Owner, newOwner);
        var participant = new AgentRunAssignment(Guid.NewGuid(), "coordinator", "Discuss inventory", new(10, 1), ["lookup"], [artifact]);
        var discussion = new AgentDiscussionSnapshot(Guid.NewGuid(), newOwner.Epoch, 2, ["coordinator"], [])
        {
            Assignments = [participant]
        };
        var operationId = Guid.NewGuid();
        using var arguments = JsonDocument.Parse("""{"query":"inventory"}""");
        using var observation = JsonDocument.Parse("""{"text":"before\u0000after","available":true}""");
        var reservation = new AgentBudgetReservation(new(40, 1), new(28, 1))
        {
            ModelDecision = new(new("lookup", arguments.RootElement), null),
            ToolObservation = new("found", true, observation.RootElement)
        };
        var updatedRoot = root with
        {
            Owner = newOwner,
            Handoffs = [handoff],
            Discussion = discussion,
            Wait = nextWait,
            Resumptions = [resumption],
            RemainingBudget = new(60, 1),
            Reservations = root.Reservations.Add(operationId, reservation)
        };
        var childId = Guid.NewGuid();
        var child = CreateExecution(key with { RootRunId = childId }).Runs[childId] with
        {
            ChildCall = new(key.RootRunId, newOwner.Epoch, childId),
            Result = new(AgentResultKind.Returned, "ready", artifact)
        };
        var updated = initial with { Revision = 1, Runs = initial.Runs.SetItem(key.RootRunId, updatedRoot).Add(childId, child) };

        await using (var provider = BuildProvider(lease.Options))
        {
            var store = provider.GetRequiredService<IAgentExecutionStore>();
            (await store.TryCreateAsync(initial, default)).Should().BeTrue();
            (await store.TryReplaceAsync(updated, 0, default)).Should().BeTrue();
        }

        await using var rebuilt = BuildProvider(lease.Options);
        var read = await rebuilt.GetRequiredService<IAgentExecutionStore>().FindAsync(key, default);
        read.Should().NotBeNull();
        read!.Key.Should().Be(key);
        read.Revision.Should().Be(1);
        read.ActorId.Should().Be(initial.ActorId);
        read.Deadline.Should().Be(initial.Deadline);
        var reloadedRoot = read.Runs[key.RootRunId];
        reloadedRoot.Owner.Should().Be(newOwner);
        reloadedRoot.Handoffs.Should().BeEquivalentTo(updatedRoot.Handoffs);
        reloadedRoot.Discussion.Should().BeEquivalentTo(discussion);
        reloadedRoot.Wait.Should().Be(nextWait);
        reloadedRoot.Resumptions.Should().Equal(resumption);
        reloadedRoot.PendingHandoff.Should().BeNull();
        reloadedRoot.RemainingBudget.Should().Be(updatedRoot.RemainingBudget);
        var reloadedReservation = reloadedRoot.Reservations[operationId];
        reloadedReservation.Reserved.Should().Be(reservation.Reserved);
        reloadedReservation.Used.Should().Be(reservation.Used);
        reloadedReservation.ModelDecision!.Tool!.Name.Should().Be("lookup");
        reloadedReservation.ModelDecision.Tool.Arguments!.Value.GetProperty("query").GetString().Should().Be("inventory");
        reloadedReservation.ToolObservation!.Code.Should().Be("found");
        reloadedReservation.ToolObservation.StructuredOutput!.Value.GetProperty("text").GetString()
            .Should().Be("before\0after");
        reloadedReservation.ToolObservation.StructuredOutput!.Value.GetProperty("available").GetBoolean().Should().BeTrue();
        read.Runs[childId].ChildCall.Should().Be(child.ChildCall);
        read.Runs[childId].Result.Should().Be(child.Result);
    }

    [Fact]
    public async Task ReActPersistsEscapedNullObservationWithoutRepeatingTool()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await using var provider = BuildProvider(lease.Options);
        var store = provider.GetRequiredService<IAgentExecutionStore>();
        var clock = TimeProvider.System;
        var coordinator = new AgentRunCoordinator(store, clock);
        var key = new AgentExecutionKey("tenant-a", Guid.NewGuid());
        var owner = await coordinator.StartAsync(key, "engineer", clock.GetUtcNow().AddMinutes(1),
            new(key.RootRunId, "discovery", "Inspect inventory", new(100, 3), ["lookup"], []));
        var modelCalls = 0;
        var model = new PostgreSqlReActModel(request =>
        {
            modelCalls++;
            return request.Round == 1
                ? new(12, new(new("lookup", JsonSerializer.SerializeToElement(new { Query = "inventory" })), null))
                : new(13, new(null, new(AgentResultKind.Returned, "Found", new("report", "hash"))));
        });
        using var observation = JsonDocument.Parse("""{"text":"before\u0000after"}""");
        var toolCalls = 0;
        var tools = new PostgreSqlReActTools(_ =>
        {
            toolCalls++;
            return new AgentToolObservation("Success", true, observation.RootElement.Clone());
        });
        var runner = new AgentReActRunner(coordinator, clock);

        var result = await runner.RunAsync(key, owner, model, tools, 3, 30);
        Assert.Equal("Found", result.Code);
        var replay = await new AgentReActRunner(new AgentRunCoordinator(store, clock), clock)
            .RunAsync(key, owner, model, tools, 3, 30);
        Assert.Equal(result, replay);
        Assert.Equal(2, modelCalls);
        Assert.Equal(1, toolCalls);
        var persisted = (await store.FindAsync(key, default))!.Runs[owner.RunId];
        var receipt = Assert.Single(persisted.Reservations.Values, value => value.ToolObservation is not null);
        Assert.Equal("before\0after", receipt.ToolObservation!.StructuredOutput!.Value.GetProperty("text").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FindRejectsJsonIdentityOrRevisionMismatch(bool corruptIdentity)
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await using var provider = BuildProvider(lease.Options);
        var store = provider.GetRequiredService<IAgentExecutionStore>();
        var key = new AgentExecutionKey(null, Guid.NewGuid());
        (await store.TryCreateAsync(CreateExecution(key), default)).Should().BeTrue();

        var dataSource = provider.GetRequiredService<NpgsqlDataSource>();
        var mutation = corruptIdentity
            ? "state_json = to_jsonb(jsonb_set((state_json #>> '{}')::jsonb, '{key,tenantId}', '\"other-tenant\"'::jsonb)::text)"
            : "revision = 1";
        await using var command = dataSource.CreateCommand($"""
            update "{lease.Options.Schema}".agent_executions set {mutation} where root_run_id = @root;
            """);
        command.Parameters.AddWithValue("root", key.RootRunId);
        (await command.ExecuteNonQueryAsync()).Should().Be(1);
        var read = async () => await store.FindAsync(key, default);
        (await read.Should().ThrowAsync<RuntimePersistenceContractException>()).Which.Code
            .Should().Be(RuntimePersistenceContractErrorCode.PersistedInvariantViolation);
    }

    private sealed class PostgreSqlReActModel(Func<AgentModelTurnRequest, AgentModelTurnResponse> respond)
        : IAgentModelTurnExecutor
    {
        public ValueTask<AgentModelTurnResponse> ExecuteAsync(AgentModelTurnRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(respond(request));
    }

    private sealed class PostgreSqlReActTools(Func<AgentToolSelection, AgentToolObservation> invoke)
        : IAgentRunToolExecutor
    {
        public ValueTask<AgentToolObservation> InvokeAsync(AgentRunContext context, Guid operationId,
            AgentToolSelection selection, CancellationToken cancellationToken) =>
            ValueTask.FromResult(invoke(selection));
    }

    private static AgentExecutionSnapshot CreateExecution(AgentExecutionKey key)
    {
        var run = new AgentRunSnapshot(new(key.RootRunId, "coordinator", 1), "coordinate", new(100, 2),
            [], [], null, [], null, [], ImmutableDictionary<Guid, AgentBudgetReservation>.Empty, null);
        return new(key, 0, "actor", DateTimeOffset.UnixEpoch.AddDays(1),
            ImmutableDictionary<Guid, AgentRunSnapshot>.Empty.Add(key.RootRunId, run));
    }

    private static ServiceProvider BuildProvider(PostgreSqlRuntimePersistenceOptions options) => new ServiceCollection()
        .AddCrestCreatesPostgreSqlRuntimePersistence(options)
        .BuildServiceProvider();
}
