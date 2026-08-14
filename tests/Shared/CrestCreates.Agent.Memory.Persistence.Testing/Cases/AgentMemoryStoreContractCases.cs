using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Agent.Memory.Persistence.Testing.Assertions;
using CrestCreates.Agent.Memory.Persistence.Testing.Drivers;
using ContractAssertions = CrestCreates.Agent.Memory.Persistence.Testing.Assertions.AgentMemoryPersistenceContractAssertions;

namespace CrestCreates.Agent.Memory.Persistence.Testing.Cases;

/// <summary>
/// Provider-neutral Agent Memory Store contract cases. Each method carries the
/// exact Spec §18.1 skeleton name and is activated by concrete InMemory and
/// PostgreSQL runners. Cases never compute or hard-code state hashes and never
/// reproduce Candidate→Memory/Supersede projection; the driver prepares all
/// expectations through the real shared projectors.
/// </summary>
public static class AgentMemoryStoreContractCases
{
    // ── Conversation ─────────────────────────────────────────────────────────

    public static async Task Conversation_Should_Preserve_TenantIsolation(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        var first = Conversation("tenant-a", "conversation-1", Turn("tenant-a", "turn-1", "hello", 0));
        var second = Conversation("tenant-b", "conversation-1", Turn("tenant-b", "turn-1", "world", 0));

        await driver.ConversationStore.SaveConversationAsync(first, cancellationToken);
        await driver.ConversationStore.SaveConversationAsync(second, cancellationToken);

        var fromA = await driver.ConversationStore.GetConversationAsync("tenant-a", "conversation-1", cancellationToken);
        var fromB = await driver.ConversationStore.GetConversationAsync("tenant-b", "conversation-1", cancellationToken);

        ContractAssertions.NotNull(fromA, "tenant-a conversation must exist.");
        ContractAssertions.NotNull(fromB, "tenant-b conversation must exist.");
        ContractAssertions.Equal("hello", fromA!.Turns[0].Content, "tenant-a content must be preserved.");
        ContractAssertions.Equal("world", fromB!.Turns[0].Content, "tenant-b content must be preserved.");
        ContractAssertions.Equal(1, fromA.Turns.Count, "tenant-a must see exactly its own turn.");
    }

    public static async Task Conversation_Should_Return_Snapshot(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        var conversation = Conversation(
            "tenant-a",
            "conversation-snapshot",
            Turn("tenant-a", "turn-1", "first", 0),
            Turn("tenant-a", "turn-2", "second", 1));

        await driver.ConversationStore.SaveConversationAsync(conversation, cancellationToken);
        var firstRead = await driver.ConversationStore.GetConversationAsync("tenant-a", "conversation-snapshot", cancellationToken);
        ContractAssertions.NotNull(firstRead, "conversation must be readable after save.");
        ContractAssertions.Equal(2, firstRead!.Turns.Count, "both turns must be preserved.");

        var mutated = firstRead with
        {
            Turns = [firstRead.Turns[0] with { Content = "mutated" }, firstRead.Turns[1]]
        };

        var secondRead = await driver.ConversationStore.GetConversationAsync("tenant-a", "conversation-snapshot", cancellationToken);
        ContractAssertions.Equal("first", secondRead!.Turns[0].Content, "mutating a returned snapshot must not alter durable state.");
        ContractAssertions.Equal("second", secondRead.Turns[1].Content, "second turn must be unchanged.");
    }

    public static async Task Conversation_Should_Persist_Only_Sanitized_Turns(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        var sentinel = AgentMemoryPersistenceContractMarkers.RejectedContentSentinel;
        var rejected = driver.Sanitizer.Sanitize("tenant-a", sentinel, Array.Empty<AgentContextSourceRef>());
        ContractAssertions.True(rejected.Rejected, "fixture sanitizer must reject the contract rejection sentinel.");

        var conversation = Conversation(
            "tenant-a",
            "conversation-sanitized",
            Turn("tenant-a", "turn-1", "accepted content", 0),
            Turn("tenant-a", "turn-2", sentinel, 1));

        await driver.ConversationStore.SaveConversationAsync(conversation, cancellationToken);
        var read = await driver.ConversationStore.GetConversationAsync("tenant-a", "conversation-sanitized", cancellationToken);

        ContractAssertions.NotNull(read, "conversation must exist after save.");
        ContractAssertions.Equal(1, read!.Turns.Count, "rejected turns must be omitted.");
        ContractAssertions.Equal("turn-1", read.Turns[0].TurnId, "accepted turn must survive.");
        ContractAssertions.Equal("accepted content", read.Turns[0].Content, "accepted content must be preserved.");
        ContractAssertions.True(
            read.Turns[0].Content.Contains(sentinel, StringComparison.Ordinal) is false,
            "raw rejected content must never be persisted.");
        ContractAssertions.True(
            read.Diagnostics.Any(diagnostic => diagnostic.Code.Value == AgentMemoryDiagnosticCodes.ContentRejected.Value),
            "a safe rejection diagnostic must be recorded.");
    }

    public static async Task Conversation_Should_Preserve_TurnSequence(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        var conversation = Conversation(
            "tenant-a",
            "conversation-sequence",
            Turn("tenant-a", "turn-3", "third", 2),
            Turn("tenant-a", "turn-1", "first", 0),
            Turn("tenant-a", "turn-2", "second", 1));

        await driver.ConversationStore.SaveConversationAsync(conversation, cancellationToken);
        var read = await driver.ConversationStore.GetConversationAsync("tenant-a", "conversation-sequence", cancellationToken);

        ContractAssertions.NotNull(read, "conversation must exist after save.");
        ContractAssertions.SequenceEqual(
            ["turn-3", "turn-1", "turn-2"],
            read!.Turns.Select(turn => turn.TurnId).ToArray(),
            "submitted Turn sequence must be preserved, not sorted by ID or timestamp.");
    }

    // ── Task ─────────────────────────────────────────────────────────────────

    public static async Task Task_Should_Preserve_TenantIsolation(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        await driver.TaskStore.SaveTaskAsync(Task("tenant-a", "task-1", "title-a"), cancellationToken);
        await driver.TaskStore.SaveTaskAsync(Task("tenant-b", "task-1", "title-b"), cancellationToken);

        var fromA = await driver.TaskStore.GetTaskAsync("tenant-a", "task-1", cancellationToken);
        var fromB = await driver.TaskStore.GetTaskAsync("tenant-b", "task-1", cancellationToken);

        ContractAssertions.NotNull(fromA, "tenant-a task must exist.");
        ContractAssertions.NotNull(fromB, "tenant-b task must exist.");
        ContractAssertions.Equal("title-a", fromA!.Title, "tenant-a title must be preserved.");
        ContractAssertions.Equal("title-b", fromB!.Title, "tenant-b title must be preserved.");
    }

    public static async Task Task_Should_Return_Snapshot(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        var task = Task("tenant-a", "task-snapshot", "title") with
        {
            Events = [TaskEvent("tenant-a", "task-snapshot", "event-1", "content", 0)]
        };

        await driver.TaskStore.SaveTaskAsync(task, cancellationToken);
        var firstRead = await driver.TaskStore.GetTaskAsync("tenant-a", "task-snapshot", cancellationToken);
        ContractAssertions.NotNull(firstRead, "task must be readable after save.");
        ContractAssertions.Equal(1, firstRead!.Events.Count, "event must be preserved.");

        var mutated = firstRead with
        {
            Events = [firstRead.Events[0] with { Content = "mutated" }]
        };

        var secondRead = await driver.TaskStore.GetTaskAsync("tenant-a", "task-snapshot", cancellationToken);
        ContractAssertions.Equal("content", secondRead!.Events[0].Content, "mutating a returned snapshot must not alter durable state.");
    }

    public static async Task Task_Should_Persist_Only_Sanitized_Content(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        var sentinel = AgentMemoryPersistenceContractMarkers.RejectedContentSentinel;
        var rejected = driver.Sanitizer.Sanitize("tenant-a", sentinel, Array.Empty<AgentContextSourceRef>());
        ContractAssertions.True(rejected.Rejected, "fixture sanitizer must reject the contract rejection sentinel.");

        var task = Task("tenant-a", "task-sanitized", "title") with
        {
            Summary = sentinel,
            Events =
            [
                TaskEvent("tenant-a", "task-sanitized", "event-1", "accepted", 0),
                TaskEvent("tenant-a", "task-sanitized", "event-2", sentinel, 1)
            ]
        };

        await driver.TaskStore.SaveTaskAsync(task, cancellationToken);
        var read = await driver.TaskStore.GetTaskAsync("tenant-a", "task-sanitized", cancellationToken);

        ContractAssertions.NotNull(read, "task must exist after save.");
        ContractAssertions.Null(read!.Summary, "rejected Summary must become null.");
        ContractAssertions.Equal(1, read.Events.Count, "rejected Events must be omitted.");
        ContractAssertions.Equal("event-1", read.Events[0].EventId, "accepted event must survive.");
        ContractAssertions.True(
            read.Events[0].Content.Contains(sentinel, StringComparison.Ordinal) is false
            && (read.Summary?.Contains(sentinel, StringComparison.Ordinal) ?? false) is false,
            "raw rejected content must never be persisted.");
        ContractAssertions.True(
            read.Diagnostics.Any(diagnostic => diagnostic.Code.Value == AgentMemoryDiagnosticCodes.ContentRejected.Value),
            "a safe rejection diagnostic must be recorded.");
    }

    public static async Task Task_Should_Preserve_Deterministic_Order(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        await driver.TaskStore.SaveTaskAsync(Task("tenant-a", "task-2", "b"), cancellationToken);
        await driver.TaskStore.SaveTaskAsync(Task("tenant-a", "task-1", "a"), cancellationToken);
        await driver.TaskStore.SaveTaskAsync(Task("tenant-a", "task-3", "c"), cancellationToken);

        var tasks = await driver.TaskStore.ListTasksAsync("tenant-a", cancellationToken);
        ContractAssertions.SequenceEqual(
            ["task-1", "task-2", "task-3"],
            tasks.Select(task => task.TaskId).ToArray(),
            "ListTasksAsync must return TaskId order by StringComparer.Ordinal.");
    }

    public static async Task Concurrent_TaskAppend_Should_Not_Lose_Event(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        await driver.TaskStore.SaveTaskAsync(Task("tenant-a", "task-concurrent", "title"), cancellationToken);

        var first = TaskEvent("tenant-a", "task-concurrent", "event-1", "first", 0);
        var second = TaskEvent("tenant-a", "task-concurrent", "event-2", "second", 1);

        await System.Threading.Tasks.Task.WhenAll(
            driver.TaskStore.AppendEventAsync("tenant-a", "task-concurrent", first, cancellationToken).AsTask(),
            driver.TaskStore.AppendEventAsync("tenant-a", "task-concurrent", second, cancellationToken).AsTask());

        var read = await driver.TaskStore.GetTaskAsync("tenant-a", "task-concurrent", cancellationToken);
        ContractAssertions.NotNull(read, "task must exist after concurrent appends.");
        ContractAssertions.Equal(2, read!.Events.Count, "two committed appends must both be visible.");
        ContractAssertions.True(
            read.Events.Any(item => item.EventId == "event-1") && read.Events.Any(item => item.EventId == "event-2"),
            "both appended events must be present exactly once.");
    }

    public static async Task TaskAppend_MissingTask_Should_Return_ResourceUnavailable(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        var failure = await ContractAssertions.ThrowsAsync<AgentMemoryOperationException>(
            () => driver.TaskStore.AppendEventAsync(
                "tenant-a",
                "task-missing",
                TaskEvent("tenant-a", "task-missing", "event-1", "content", 0),
                cancellationToken).AsTask(),
            "Append to a missing Task must fail.");

        ContractAssertions.MemoryOperationFailure(
            AgentMemoryOperationFailureCode.ResourceUnavailable,
            failure,
            "Missing Task append must surface the intentional ResourceUnavailable cutover.");
    }

    // ── Compressed Context ───────────────────────────────────────────────────

    public static async Task CompressedContext_Should_Return_Snapshot(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        var context = CompressedContext(
            "tenant-a",
            "context-1",
            ContextBlock("tenant-a", "block-1", "first", 0),
            ContextBlock("tenant-a", "block-2", "second", 1));

        await driver.ContextStore.CreateCompressedContextAsync(context, cancellationToken);
        var firstRead = await driver.ContextStore.GetCompressedContextAsync("tenant-a", "context-1", cancellationToken);
        ContractAssertions.NotNull(firstRead, "context must be readable after create.");
        ContractAssertions.Equal(2, firstRead!.Blocks.Count, "both blocks must be preserved.");

        var mutated = firstRead with
        {
            Blocks = [firstRead.Blocks[0] with { Content = "mutated" }, firstRead.Blocks[1]]
        };

        var secondRead = await driver.ContextStore.GetCompressedContextAsync("tenant-a", "context-1", cancellationToken);
        ContractAssertions.Equal("first", secondRead!.Blocks[0].Content, "mutating a returned snapshot must not alter durable state.");
    }

    public static async Task CompressedContext_Should_Reject_CrossTenant_Block(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        var context = CompressedContext(
            "tenant-a",
            "context-cross-tenant",
            ContextBlock("tenant-a", "block-1", "first", 0),
            ContextBlock("tenant-b", "block-2", "foreign", 1));

        var failure = await ContractAssertions.ThrowsAsync<AgentMemoryOperationException>(
            () => driver.ContextStore.CreateCompressedContextAsync(context, cancellationToken).AsTask(),
            "A Context containing a cross-tenant Block must be rejected.");

        ContractAssertions.MemoryOperationFailure(
            AgentMemoryOperationFailureCode.TenantMismatch,
            failure,
            "Cross-tenant Block must surface TenantMismatch.");
    }

    public static async Task BlockIdentity_Should_Be_TenantWide_Unique(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        await driver.ContextStore.CreateCompressedContextAsync(
            CompressedContext("tenant-a", "context-1", ContextBlock("tenant-a", "block-shared", "first", 0)),
            cancellationToken);

        var conflict = await ContractAssertions.ThrowsAsync<AgentMemoryOperationException>(
            () => driver.ContextStore.CreateCompressedContextAsync(
                CompressedContext("tenant-a", "context-2", ContextBlock("tenant-a", "block-shared", "second", 0)),
                cancellationToken).AsTask(),
            "A second Context in the same Tenant claiming the same BlockId must conflict.");

        ContractAssertions.MemoryOperationFailure(
            AgentMemoryOperationFailureCode.IdentityConflict,
            conflict,
            "BlockId must be tenant-wide unique across Contexts.");
    }

    public static async Task ReplacingContext_Should_Remove_Old_BlockProjection(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        await driver.ContextStore.CreateCompressedContextAsync(
            CompressedContext("tenant-a", "context-replace", ContextBlock("tenant-a", "block-old", "old", 0)),
            cancellationToken);

        var replacement = CompressedContext(
            "tenant-a",
            "context-replace",
            ContextBlock("tenant-a", "block-new", "new", 0));

        await driver.ContextStore.SaveCompressedContextAsync(replacement, cancellationToken);

        var oldBlock = await driver.ContextStore.GetCompressedContextBlockAsync("tenant-a", "block-old", cancellationToken);
        ContractAssertions.Null(oldBlock, "replacement must remove the old Block projection.");
        var newBlock = await driver.ContextStore.GetCompressedContextBlockAsync("tenant-a", "block-new", cancellationToken);
        ContractAssertions.NotNull(newBlock, "replacement must expose the new Block projection.");
        ContractAssertions.Equal("new", newBlock!.Content, "new Block content must be preserved.");
    }

    // ── Candidate ────────────────────────────────────────────────────────────

    public static async Task Candidate_Should_Return_Snapshot(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        var candidate = Candidate("tenant-a", "candidate-1");
        await driver.MemoryStore.CreateCandidateAsync(candidate, cancellationToken);

        var read = await driver.MemoryStore.GetCandidateAsync("tenant-a", "candidate-1", cancellationToken);
        ContractAssertions.NotNull(read, "candidate must be readable after create.");
        ContractAssertions.Equal("candidate-1", read!.CandidateId, "candidate identity must be preserved.");
        ContractAssertions.Equal(AgentMemoryStatus.Candidate, read.Status, "new candidates must be in Candidate status.");

        var duplicate = await ContractAssertions.ThrowsAsync<AgentMemoryOperationException>(
            () => driver.MemoryStore.CreateCandidateAsync(candidate, cancellationToken).AsTask(),
            "Creating an existing candidate identity must conflict.");
        ContractAssertions.MemoryOperationFailure(
            AgentMemoryOperationFailureCode.IdentityConflict,
            duplicate,
            "Existing candidate identity must surface IdentityConflict.");
    }

    // ── Memory base ──────────────────────────────────────────────────────────

    public static async Task Memory_Should_Return_Snapshot(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        var memory = Memory("tenant-a", "memory-1");
        await driver.MemoryStore.SaveMemoryAsync(memory, cancellationToken);

        var read = await driver.MemoryStore.GetMemoryAsync("tenant-a", "memory-1", cancellationToken);
        ContractAssertions.NotNull(read, "memory must be readable after save.");
        ContractAssertions.Equal("memory-1", read!.MemoryId, "memory identity must be preserved.");
        ContractAssertions.Equal(AgentMemoryStatus.Active, read.Status, "created memories must be Active.");
        ContractAssertions.Equal(false, read.IsAuthoritative, "created memories must be non-authoritative.");

        var mutated = read with { Content = "mutated" };
        var secondRead = await driver.MemoryStore.GetMemoryAsync("tenant-a", "memory-1", cancellationToken);
        ContractAssertions.Equal("content-memory-1", secondRead!.Content, "mutating a returned snapshot must not alter durable state.");
    }

    public static async Task SaveMemory_Should_Be_CreateOrExactReplay(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        var memory = Memory("tenant-a", "memory-replay");
        await driver.MemoryStore.SaveMemoryAsync(memory, cancellationToken);

        var first = await driver.MemoryStore.GetMemoryAsync("tenant-a", "memory-replay", cancellationToken);
        ContractAssertions.NotNull(first, "memory must exist after first save.");

        await driver.MemoryStore.SaveMemoryAsync(memory, cancellationToken);
        var second = await driver.MemoryStore.GetMemoryAsync("tenant-a", "memory-replay", cancellationToken);
        ContractAssertions.NotNull(second, "memory must survive exact replay.");
        ContractAssertions.Equal(first!.MemoryId, second!.MemoryId, "exact replay must not change the identity.");
    }

    public static async Task SaveMemory_InvalidInitialLifecycleOrAuthority_Should_BeRejected(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        var active = Memory("tenant-a", "memory-shape");
        await driver.MemoryStore.SaveMemoryAsync(active, cancellationToken);

        var shapes = new (AgentMemoryItem Item, string Label)[]
        {
            (Memory("tenant-a", "memory-shape-candidate") with { Status = AgentMemoryStatus.Candidate }, "Candidate status"),
            (Memory("tenant-a", "memory-shape-rejected") with { Status = AgentMemoryStatus.Rejected }, "Rejected status"),
            (Memory("tenant-a", "memory-shape-superseded") with { Status = AgentMemoryStatus.Superseded }, "Superseded status"),
            (Memory("tenant-a", "memory-shape-archived") with { Status = AgentMemoryStatus.Archived }, "Archived status"),
            (Memory("tenant-a", "memory-shape-authoritative") with { IsAuthoritative = true }, "authoritative"),
            (Memory("tenant-a", "memory-shape-supersedes") with { SupersedesMemoryId = "memory-other" }, "SupersedesMemoryId set"),
            (Memory("tenant-a", "memory-shape-supersededby") with { SupersededByMemoryId = "memory-other" }, "SupersededByMemoryId set")
        };

        foreach (var (item, label) in shapes)
        {
            var failure = await ContractAssertions.ThrowsAsync<AgentMemoryOperationException>(
                () => driver.MemoryStore.SaveMemoryAsync(item, cancellationToken).AsTask(),
                $"{label} snapshot must be rejected on create.");
            ContractAssertions.MemoryOperationFailure(
                AgentMemoryOperationFailureCode.InvalidLifecycleState,
                failure,
                $"{label} new snapshot must surface InvalidLifecycleState with zero mutation.");
        }

        var missing = await driver.MemoryStore.GetMemoryAsync("tenant-a", "memory-shape-candidate", cancellationToken);
        ContractAssertions.Null(missing, "rejected create shapes must not create a row.");
    }

    public static async Task ListMemories_Should_Be_Ordinally_Deterministic(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        await driver.MemoryStore.SaveMemoryAsync(Memory("tenant-a", "memory-3"), cancellationToken);
        await driver.MemoryStore.SaveMemoryAsync(Memory("tenant-a", "memory-1"), cancellationToken);
        await driver.MemoryStore.SaveMemoryAsync(Memory("tenant-a", "memory-2"), cancellationToken);

        var query = new AgentMemoryQuery { TenantId = "tenant-a" };
        var memories = await driver.MemoryStore.ListMemoriesAsync(query, cancellationToken);
        ContractAssertions.SequenceEqual(
            ["memory-1", "memory-2", "memory-3"],
            memories.Select(memory => memory.MemoryId).ToArray(),
            "ListMemoriesAsync must return MemoryId order by StringComparer.Ordinal.");
    }

    public static async Task ListStores_NonBmpIdentifiers_Should_Match_StringComparerOrdinal(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        // UTF-16 ordinal order differs from UTF-8 byte order for these two IDs:
        //   "task-😀"  (U+1F600 surrogate pair D83D DE00)
        //   "task-｡"   (U+FF61 halfwidth, single UTF-16 unit)
        // UTF-16 ordinal: surrogate D83D < FF61 → "task-😀" first.
        // UTF-8 bytes:    EF BD A1 < F0 9F 98 80 → "task-｡" first.
        const string emojiId = "task-\uD83D\uDE00";
        const string halfWidthId = "task-\uFF61";

        await driver.TaskStore.SaveTaskAsync(Task("tenant-a", emojiId, "emoji"), cancellationToken);
        await driver.TaskStore.SaveTaskAsync(Task("tenant-a", halfWidthId, "halfwidth"), cancellationToken);
        await driver.MemoryStore.SaveMemoryAsync(Memory("tenant-a", "memory-\uD83D\uDE00"), cancellationToken);
        await driver.MemoryStore.SaveMemoryAsync(Memory("tenant-a", "memory-\uFF61"), cancellationToken);

        var tasks = await driver.TaskStore.ListTasksAsync("tenant-a", cancellationToken);
        ContractAssertions.SequenceEqual(
            [emojiId, halfWidthId],
            tasks.Select(task => task.TaskId).ToArray(),
            "Task list order must be final StringComparer.Ordinal, not database byte order.");

        var memories = await driver.MemoryStore.ListMemoriesAsync(
            new AgentMemoryQuery { TenantId = "tenant-a" },
            cancellationToken);
        ContractAssertions.SequenceEqual(
            ["memory-\uD83D\uDE00", "memory-\uFF61"],
            memories.Select(memory => memory.MemoryId).ToArray(),
            "Memory list order must be final StringComparer.Ordinal, not database byte order.");
    }

    public static async Task Memory_Query_Should_Match_InMemory_Contract(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        var conditional = driver.MemoryStore as IAgentMemoryConditionalCurationStore;
        ContractAssertions.NotNull(conditional, "the selected Memory Store must implement conditional curation to build lifecycle states.");

        var memory = Memory("tenant-a", "memory-query") with
        {
            Tags = ["tag-a"],
            DescriptorRefs = [new DescriptorRef("ns", "descriptor-1")]
        };
        await driver.MemoryStore.SaveMemoryAsync(memory, cancellationToken);

        // Superseded state is reachable only through conditional Supersede.
        var supersedeSource = Candidate("tenant-a", "candidate-query-supersede");
        await driver.MemoryStore.CreateCandidateAsync(supersedeSource, cancellationToken);
        var supersedePromote = driver.PreparePromotionPlan(supersedeSource, "memory-query-superseded", Operation("tenant-a", "op-query-1"));
        await conditional!.PromoteAsync("tenant-a", supersedePromote, cancellationToken);
        var supersededTarget = (await driver.MemoryStore.GetMemoryAsync("tenant-a", "memory-query-superseded", cancellationToken))!;
        var replacement = Candidate("tenant-a", "candidate-query-replacement", AgentMemoryKind.Decision);
        await driver.MemoryStore.CreateCandidateAsync(replacement, cancellationToken);
        var supersession = driver.PrepareSupersessionPlan(
            supersededTarget, replacement, "memory-query-replacement", Operation("tenant-a", "op-query-2"));
        await conditional.SupersedeAsync("tenant-a", supersession, cancellationToken);

        // Archived state is reachable only through conditional Archive.
        var archivedSource = Candidate("tenant-a", "candidate-query-archive");
        await driver.MemoryStore.CreateCandidateAsync(archivedSource, cancellationToken);
        var archivePromote = driver.PreparePromotionPlan(archivedSource, "memory-query-archived", Operation("tenant-a", "op-query-3"));
        var archivedMemory = await conditional.PromoteAsync("tenant-a", archivePromote, cancellationToken);
        var archivedExpectation = driver.PrepareMemoryExpectation(archivedMemory);
        await conditional.ArchiveAsync("tenant-a", archivedExpectation, Operation("tenant-a", "op-query-4"), cancellationToken);

        var activeOnly = await driver.MemoryStore.ListMemoriesAsync(
            new AgentMemoryQuery { TenantId = "tenant-a" },
            cancellationToken);
        ContractAssertions.SequenceEqual(
            ["memory-query", "memory-query-replacement"],
            activeOnly.Select(item => item.MemoryId).ToArray(),
            "default query must return Active memories only, in ordinal order.");

        var withSuperseded = await driver.MemoryStore.ListMemoriesAsync(
            new AgentMemoryQuery { TenantId = "tenant-a", IncludeSuperseded = true },
            cancellationToken);
        ContractAssertions.SequenceEqual(
            ["memory-query", "memory-query-replacement", "memory-query-superseded"],
            withSuperseded.Select(item => item.MemoryId).ToArray(),
            "IncludeSuperseded must add Superseded memories in ordinal order.");

        var withArchived = await driver.MemoryStore.ListMemoriesAsync(
            new AgentMemoryQuery { TenantId = "tenant-a", IncludeArchived = true },
            cancellationToken);
        ContractAssertions.SequenceEqual(
            ["memory-query", "memory-query-archived", "memory-query-replacement"],
            withArchived.Select(item => item.MemoryId).ToArray(),
            "IncludeArchived must add Archived memories in ordinal order.");

        var tagged = await driver.MemoryStore.ListMemoriesAsync(
            new AgentMemoryQuery { TenantId = "tenant-a", Tags = ["tag-a"] },
            cancellationToken);
        ContractAssertions.SequenceEqual(
            ["memory-query"],
            tagged.Select(item => item.MemoryId).ToArray(),
            "Tag filter must match the tagged memory only.");

        var descriptorFiltered = await driver.MemoryStore.ListMemoriesAsync(
            new AgentMemoryQuery
            {
                TenantId = "tenant-a",
                DescriptorRefs = [new DescriptorRef("ns", "descriptor-1")]
            },
            cancellationToken);
        ContractAssertions.SequenceEqual(
            ["memory-query"],
            descriptorFiltered.Select(item => item.MemoryId).ToArray(),
            "DescriptorRef filter must match the descriptor memory only.");

        var idFiltered = await driver.MemoryStore.ListMemoriesAsync(
            new AgentMemoryQuery { TenantId = "tenant-a", MemoryIds = ["memory-query-archived"] },
            cancellationToken);
        ContractAssertions.Equal(0, idFiltered.Count, "ID filter must still honor the Active-only status filter.");

        var stale = await driver.MemoryStore.ListMemoriesAsync(
            new AgentMemoryQuery { TenantId = "tenant-a", IncludeStale = true },
            cancellationToken);
        ContractAssertions.Equal(activeOnly.Count, stale.Count, "IncludeStale must remain a no-op.");
    }

    // ── Cross-store boundary helpers ──────────────────────────────────────────

    public static async Task AllStores_Should_IsolateSameIdentityAcrossTenants(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        await driver.ConversationStore.SaveConversationAsync(
            Conversation("tenant-a", "shared-1", Turn("tenant-a", "turn-1", "a", 0)), cancellationToken);
        await driver.ConversationStore.SaveConversationAsync(
            Conversation("tenant-b", "shared-1", Turn("tenant-b", "turn-1", "b", 0)), cancellationToken);
        await driver.TaskStore.SaveTaskAsync(Task("tenant-a", "shared-1", "a"), cancellationToken);
        await driver.TaskStore.SaveTaskAsync(Task("tenant-b", "shared-1", "b"), cancellationToken);
        await driver.ContextStore.CreateCompressedContextAsync(
            CompressedContext("tenant-a", "shared-1", ContextBlock("tenant-a", "block-a", "a", 0)), cancellationToken);
        await driver.ContextStore.CreateCompressedContextAsync(
            CompressedContext("tenant-b", "shared-1", ContextBlock("tenant-b", "block-b", "b", 0)), cancellationToken);
        await driver.MemoryStore.CreateCandidateAsync(Candidate("tenant-a", "shared-1"), cancellationToken);
        await driver.MemoryStore.CreateCandidateAsync(Candidate("tenant-b", "shared-1"), cancellationToken);
        await driver.MemoryStore.SaveMemoryAsync(Memory("tenant-a", "shared-1"), cancellationToken);
        await driver.MemoryStore.SaveMemoryAsync(Memory("tenant-b", "shared-1"), cancellationToken);

        var conversationA = await driver.ConversationStore.GetConversationAsync("tenant-a", "shared-1", cancellationToken);
        var conversationB = await driver.ConversationStore.GetConversationAsync("tenant-b", "shared-1", cancellationToken);
        ContractAssertions.Equal("a", conversationA!.Turns[0].Content, "tenant-a conversation must be independent.");
        ContractAssertions.Equal("b", conversationB!.Turns[0].Content, "tenant-b conversation must be independent.");

        var taskA = await driver.TaskStore.GetTaskAsync("tenant-a", "shared-1", cancellationToken);
        var taskB = await driver.TaskStore.GetTaskAsync("tenant-b", "shared-1", cancellationToken);
        ContractAssertions.Equal("a", taskA!.Title, "tenant-a task must be independent.");
        ContractAssertions.Equal("b", taskB!.Title, "tenant-b task must be independent.");

        var contextA = await driver.ContextStore.GetCompressedContextAsync("tenant-a", "shared-1", cancellationToken);
        var contextB = await driver.ContextStore.GetCompressedContextAsync("tenant-b", "shared-1", cancellationToken);
        ContractAssertions.Equal("block-a", contextA!.Blocks[0].BlockId, "tenant-a context must be independent.");
        ContractAssertions.Equal("block-b", contextB!.Blocks[0].BlockId, "tenant-b context must be independent.");

        var candidateA = await driver.MemoryStore.GetCandidateAsync("tenant-a", "shared-1", cancellationToken);
        var candidateB = await driver.MemoryStore.GetCandidateAsync("tenant-b", "shared-1", cancellationToken);
        ContractAssertions.NotNull(candidateA, "tenant-a candidate must exist.");
        ContractAssertions.NotNull(candidateB, "tenant-b candidate must exist.");

        var memoryA = await driver.MemoryStore.GetMemoryAsync("tenant-a", "shared-1", cancellationToken);
        var memoryB = await driver.MemoryStore.GetMemoryAsync("tenant-b", "shared-1", cancellationToken);
        ContractAssertions.NotNull(memoryA, "tenant-a memory must exist.");
        ContractAssertions.NotNull(memoryB, "tenant-b memory must exist.");
    }

    public static async Task AllCrossTenantLookups_Should_ReturnNullOrEmptyWithoutLeakage(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        await driver.ConversationStore.SaveConversationAsync(
            Conversation("tenant-a", "private-1", Turn("tenant-a", "turn-1", "a", 0)), cancellationToken);
        await driver.TaskStore.SaveTaskAsync(Task("tenant-a", "private-1", "a"), cancellationToken);
        await driver.ContextStore.CreateCompressedContextAsync(
            CompressedContext("tenant-a", "private-1", ContextBlock("tenant-a", "block-a", "a", 0)), cancellationToken);
        await driver.MemoryStore.CreateCandidateAsync(Candidate("tenant-a", "private-1"), cancellationToken);
        await driver.MemoryStore.SaveMemoryAsync(Memory("tenant-a", "private-1"), cancellationToken);

        ContractAssertions.Null(
            await driver.ConversationStore.GetConversationAsync("tenant-b", "private-1", cancellationToken),
            "cross-tenant conversation lookup must return null.");
        ContractAssertions.Null(
            await driver.TaskStore.GetTaskAsync("tenant-b", "private-1", cancellationToken),
            "cross-tenant task lookup must return null.");
        ContractAssertions.Null(
            await driver.ContextStore.GetCompressedContextAsync("tenant-b", "private-1", cancellationToken),
            "cross-tenant context lookup must return null.");
        ContractAssertions.Null(
            await driver.ContextStore.GetCompressedContextBlockAsync("tenant-b", "block-a", cancellationToken),
            "cross-tenant block lookup must return null.");
        ContractAssertions.Null(
            await driver.MemoryStore.GetCandidateAsync("tenant-b", "private-1", cancellationToken),
            "cross-tenant candidate lookup must return null.");
        ContractAssertions.Null(
            await driver.MemoryStore.GetMemoryAsync("tenant-b", "private-1", cancellationToken),
            "cross-tenant memory lookup must return null.");
        ContractAssertions.Equal(
            0,
            (await driver.TaskStore.ListTasksAsync("tenant-b", cancellationToken)).Count,
            "cross-tenant task list must be empty.");
        ContractAssertions.Equal(
            0,
            (await driver.MemoryStore.ListMemoriesAsync(new AgentMemoryQuery { TenantId = "tenant-b" }, cancellationToken)).Count,
            "cross-tenant memory list must be empty.");
    }

    public static async Task OrderedArtifacts_Should_PreserveSubmittedSequence_NotTimestampOrIdOrder(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        var conversation = Conversation(
            "tenant-a",
            "conversation-order",
            Turn("tenant-a", "turn-late", "late", 99),
            Turn("tenant-a", "turn-early", "early", 1));
        await driver.ConversationStore.SaveConversationAsync(conversation, cancellationToken);
        var read = await driver.ConversationStore.GetConversationAsync("tenant-a", "conversation-order", cancellationToken);
        ContractAssertions.SequenceEqual(
            ["turn-late", "turn-early"],
            read!.Turns.Select(turn => turn.TurnId).ToArray(),
            "submitted Turn order must survive, not CreatedAt order.");

        var task = Task("tenant-a", "task-order", "title") with
        {
            Events =
            [
                TaskEvent("tenant-a", "task-order", "event-late", "late", 99),
                TaskEvent("tenant-a", "task-order", "event-early", "early", 1)
            ]
        };
        await driver.TaskStore.SaveTaskAsync(task, cancellationToken);
        var taskRead = await driver.TaskStore.GetTaskAsync("tenant-a", "task-order", cancellationToken);
        ContractAssertions.SequenceEqual(
            ["event-late", "event-early"],
            taskRead!.Events.Select(item => item.EventId).ToArray(),
            "submitted Event order must survive, not CreatedAt order.");

        var context = CompressedContext(
            "tenant-a",
            "context-order",
            ContextBlock("tenant-a", "block-2", "second", 1),
            ContextBlock("tenant-a", "block-1", "first", 0));
        await driver.ContextStore.CreateCompressedContextAsync(context, cancellationToken);
        var contextRead = await driver.ContextStore.GetCompressedContextAsync("tenant-a", "context-order", cancellationToken);
        ContractAssertions.SequenceEqual(
            ["block-2", "block-1"],
            contextRead!.Blocks.Select(block => block.BlockId).ToArray(),
            "submitted Block order must survive, not ID order.");
    }

    public static async Task IncludeStale_Should_RemainNoOp_WithoutStaleSchema(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        await driver.MemoryStore.SaveMemoryAsync(Memory("tenant-a", "memory-stale"), cancellationToken);
        var baseline = await driver.MemoryStore.ListMemoriesAsync(
            new AgentMemoryQuery { TenantId = "tenant-a" }, cancellationToken);
        var stale = await driver.MemoryStore.ListMemoriesAsync(
            new AgentMemoryQuery { TenantId = "tenant-a", IncludeStale = true }, cancellationToken);

        ContractAssertions.Equal(baseline.Count, stale.Count, "IncludeStale must remain a no-op with no Stale state.");
        ContractAssertions.SequenceEqual(
            baseline.Select(item => item.MemoryId).ToArray(),
            stale.Select(item => item.MemoryId).ToArray(),
            "IncludeStale must not change the visible set.");
    }

    public static async Task AllStores_Should_ReturnDetachedSnapshots(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        await driver.ConversationStore.SaveConversationAsync(
            Conversation("tenant-a", "snapshot-conversation", Turn("tenant-a", "turn-1", "content", 0)), cancellationToken);
        await driver.TaskStore.SaveTaskAsync(Task("tenant-a", "snapshot-task", "title"), cancellationToken);
        await driver.ContextStore.CreateCompressedContextAsync(
            CompressedContext("tenant-a", "snapshot-context", ContextBlock("tenant-a", "snapshot-block", "content", 0)), cancellationToken);
        await driver.MemoryStore.CreateCandidateAsync(Candidate("tenant-a", "snapshot-candidate"), cancellationToken);
        await driver.MemoryStore.SaveMemoryAsync(Memory("tenant-a", "snapshot-memory"), cancellationToken);

        var conversation = await driver.ConversationStore.GetConversationAsync("tenant-a", "snapshot-conversation", cancellationToken);
        var task = await driver.TaskStore.GetTaskAsync("tenant-a", "snapshot-task", cancellationToken);
        var context = await driver.ContextStore.GetCompressedContextAsync("tenant-a", "snapshot-context", cancellationToken);
        var block = await driver.ContextStore.GetCompressedContextBlockAsync("tenant-a", "snapshot-block", cancellationToken);
        var candidate = await driver.MemoryStore.GetCandidateAsync("tenant-a", "snapshot-candidate", cancellationToken);
        var memory = await driver.MemoryStore.GetMemoryAsync("tenant-a", "snapshot-memory", cancellationToken);

        ContractAssertions.NotNull(conversation, "conversation must be readable.");
        ContractAssertions.NotNull(task, "task must be readable.");
        ContractAssertions.NotNull(context, "context must be readable.");
        ContractAssertions.NotNull(block, "block must be readable.");
        ContractAssertions.NotNull(candidate, "candidate must be readable.");
        ContractAssertions.NotNull(memory, "memory must be readable.");

        var mutatedConversation = conversation! with
        {
            Turns = [conversation.Turns[0] with { Content = "mutated" }]
        };
        var mutatedTask = task! with { Title = "mutated" };
        var mutatedContext = context! with
        {
            Blocks = [context.Blocks[0] with { Content = "mutated" }]
        };
        var mutatedBlock = block! with { Content = "mutated" };
        var mutatedCandidate = candidate! with { Content = "mutated" };
        var mutatedMemory = memory! with { Content = "mutated" };

        var conversation2 = await driver.ConversationStore.GetConversationAsync("tenant-a", "snapshot-conversation", cancellationToken);
        var task2 = await driver.TaskStore.GetTaskAsync("tenant-a", "snapshot-task", cancellationToken);
        var context2 = await driver.ContextStore.GetCompressedContextAsync("tenant-a", "snapshot-context", cancellationToken);
        var block2 = await driver.ContextStore.GetCompressedContextBlockAsync("tenant-a", "snapshot-block", cancellationToken);
        var candidate2 = await driver.MemoryStore.GetCandidateAsync("tenant-a", "snapshot-candidate", cancellationToken);
        var memory2 = await driver.MemoryStore.GetMemoryAsync("tenant-a", "snapshot-memory", cancellationToken);

        ContractAssertions.Equal("content", conversation2!.Turns[0].Content, "conversation snapshot must be detached.");
        ContractAssertions.Equal("title", task2!.Title, "task snapshot must be detached.");
        ContractAssertions.Equal("content", context2!.Blocks[0].Content, "context snapshot must be detached.");
        ContractAssertions.Equal("content", block2!.Content, "block snapshot must be detached.");
        ContractAssertions.Equal("content-snapshot-candidate", candidate2!.Content, "candidate snapshot must be detached.");
        ContractAssertions.Equal("content-snapshot-memory", memory2!.Content, "memory snapshot must be detached.");
    }

    public static async Task CandidateBatch_WithOneConflict_Should_WriteNone(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        await driver.MemoryStore.CreateCandidateAsync(Candidate("tenant-a", "batch-existing"), cancellationToken);

        var batch = new[]
        {
            Candidate("tenant-a", "batch-1"),
            Candidate("tenant-a", "batch-existing"),
            Candidate("tenant-a", "batch-3")
        };

        var failure = await ContractAssertions.ThrowsAsync<AgentMemoryOperationException>(
            () => driver.MemoryStore.CreateCandidatesAsync(batch, cancellationToken).AsTask(),
            "A batch containing an existing identity must fail as all-or-none.");

        ContractAssertions.MemoryOperationFailure(
            AgentMemoryOperationFailureCode.IdentityConflict,
            failure,
            "Existing batch identity must surface IdentityConflict.");

        ContractAssertions.Null(
            await driver.MemoryStore.GetCandidateAsync("tenant-a", "batch-1", cancellationToken),
            "no batch member may be written when one identity conflicts.");
        ContractAssertions.Null(
            await driver.MemoryStore.GetCandidateAsync("tenant-a", "batch-3", cancellationToken),
            "no batch member may be written when one identity conflicts.");
    }

    public static async Task SaveMemory_ExistingOneFieldDifference_Should_ReturnStateConflict(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        var memory = Memory("tenant-a", "memory-conflict");
        await driver.MemoryStore.SaveMemoryAsync(memory, cancellationToken);

        var variations = new (AgentMemoryItem Item, string Label)[]
        {
            (memory with { Kind = AgentMemoryKind.Decision }, "kind"),
            (memory with { Content = "different" }, "content"),
            (memory with { Confidence = AgentMemoryConfidence.Low }, "confidence"),
            (memory with { Status = AgentMemoryStatus.Archived }, "status"),
            (memory with { IsAuthoritative = true }, "authority"),
            (memory with { SupersedesMemoryId = "memory-other" }, "supersedes link"),
            (memory with { SupersededByMemoryId = "memory-other" }, "superseded-by link")
        };

        foreach (var (item, label) in variations)
        {
            var failure = await ContractAssertions.ThrowsAsync<AgentMemoryOperationException>(
                () => driver.MemoryStore.SaveMemoryAsync(item, cancellationToken).AsTask(),
                $"{label} difference on an existing identity must conflict.");
            ContractAssertions.MemoryOperationFailure(
                AgentMemoryOperationFailureCode.StateConflict,
                failure,
                $"{label} difference must surface StateConflict.");
        }

        var stored = await driver.MemoryStore.GetMemoryAsync("tenant-a", "memory-conflict", cancellationToken);
        ContractAssertions.Equal(memory, stored, "failed replays must not mutate the stored snapshot.");
    }

    public static async Task SaveMemory_Should_Not_CreateOneSidedSupersedeGraph(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        var oneSided = Memory("tenant-a", "memory-one-sided") with { SupersedesMemoryId = "memory-nonexistent" };
        var failure = await ContractAssertions.ThrowsAsync<AgentMemoryOperationException>(
            () => driver.MemoryStore.SaveMemoryAsync(oneSided, cancellationToken).AsTask(),
            "A one-sided Supersede graph cannot be assembled through SaveMemoryAsync.");

        ContractAssertions.MemoryOperationFailure(
            AgentMemoryOperationFailureCode.InvalidLifecycleState,
            failure,
            "One-sided graph creation must surface InvalidLifecycleState.");
        ContractAssertions.Null(
            await driver.MemoryStore.GetMemoryAsync("tenant-a", "memory-one-sided", cancellationToken),
            "one-sided graph creation must not write a row.");
    }

    public static async Task CancellationBeforeFirstWrite_Should_ProduceZeroMutation(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await ContractAssertions.ThrowsAsync<OperationCanceledException>(
            () => driver.TaskStore.SaveTaskAsync(Task("tenant-a", "task-cancelled", "title"), cancelled.Token).AsTask(),
            "cancelled Task save must fail before any write.");

        var task = await driver.TaskStore.GetTaskAsync("tenant-a", "task-cancelled", cancellationToken);
        ContractAssertions.Null(task, "cancelled Task save must not create a row.");

        await ContractAssertions.ThrowsAsync<OperationCanceledException>(
            () => driver.MemoryStore.SaveMemoryAsync(Memory("tenant-a", "memory-cancelled"), cancelled.Token).AsTask(),
            "cancelled Memory save must fail before any write.");

        var memory = await driver.MemoryStore.GetMemoryAsync("tenant-a", "memory-cancelled", cancellationToken);
        ContractAssertions.Null(memory, "cancelled Memory save must not create a row.");
    }

    // ── Cross-store boundary helpers ──────────────────────────────────────────

    private static AgentConversationRecord Conversation(string tenantId, string conversationId, params AgentConversationTurn[] turns)
        => new()
        {
            TenantId = tenantId,
            ConversationId = conversationId,
            Turns = turns
        };

    private static AgentConversationTurn Turn(string tenantId, string turnId, string content, int sequence)
        => new()
        {
            TurnId = turnId,
            TenantId = tenantId,
            Role = sequence % 2 == 0 ? AgentConversationRole.User : AgentConversationRole.Assistant,
            Content = content,
            CreatedAt = DateTimeOffset.UnixEpoch.AddSeconds(sequence)
        };

    private static AgentTaskRecord Task(string tenantId, string taskId, string title)
        => new()
        {
            TenantId = tenantId,
            TaskId = taskId,
            Title = title
        };

    private static AgentTaskEvent TaskEvent(string tenantId, string taskId, string eventId, string content, int sequence)
        => new()
        {
            EventId = eventId,
            TenantId = tenantId,
            TaskId = taskId,
            EventKind = "event",
            Content = content,
            CreatedAt = DateTimeOffset.UnixEpoch.AddSeconds(sequence)
        };

    private static AgentCompressedContextBlock ContextBlock(string tenantId, string blockId, string content, int ordinal)
        => new()
        {
            BlockId = blockId,
            TenantId = tenantId,
            Content = content,
            CanonicalContentHash = CanonicalHashStub.For($"block-{blockId}"),
            SourceRefs = [new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.ConversationTurn,
                TenantId = tenantId,
                SourceId = $"source-{ordinal}"
            }]
        };

    private static AgentCompressedContext CompressedContext(string tenantId, string contextId, params AgentCompressedContextBlock[] blocks)
        => new()
        {
            TenantId = tenantId,
            ContextId = contextId,
            Blocks = blocks
        };

    private static AgentMemoryCandidate Candidate(string tenantId, string candidateId, AgentMemoryKind kind = AgentMemoryKind.Preference)
        => new()
        {
            TenantId = tenantId,
            CandidateId = candidateId,
            Kind = kind,
            Content = $"content-{candidateId}",
            CanonicalContentHash = CanonicalHashStub.For($"candidate-{candidateId}"),
            Confidence = AgentMemoryConfidence.Medium
        };

    private static AgentMemoryItem Memory(string tenantId, string memoryId, AgentMemoryKind kind = AgentMemoryKind.Preference)
        => new()
        {
            TenantId = tenantId,
            MemoryId = memoryId,
            Kind = kind,
            Content = $"content-{memoryId}",
            CanonicalContentHash = CanonicalHashStub.For($"memory-{memoryId}"),
            Confidence = AgentMemoryConfidence.Medium,
            PromotedAt = DateTimeOffset.UnixEpoch
        };

    private static AgentMemoryOperationRequest Operation(string tenantId, string operationId)
        => new()
        {
            TenantId = tenantId,
            InvocationContext = new AgentMemoryInvocationContext
            {
                TenantId = tenantId,
                ActorId = "contract-runner",
                ActorKind = "system",
                CorrelationId = $"correlation-{operationId}",
                InvocationSource = "system"
            },
            Reason = "contract case",
            Identity = new AgentMemoryOperationIdentity
            {
                OperationId = operationId,
                OccurredAt = DateTimeOffset.UnixEpoch.AddSeconds(10)
            },
            Explanation = "contract case explanation"
        };
}
