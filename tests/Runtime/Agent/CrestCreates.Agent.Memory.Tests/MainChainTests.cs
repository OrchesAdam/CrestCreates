using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Authoring;
using CrestCreates.Agent.Memory.Compression;
using CrestCreates.Agent.Memory.Extraction;
using CrestCreates.Agent.Memory.Promotion;
using CrestCreates.Agent.Memory.Recall;
using CrestCreates.Agent.Memory.Sanitization;
using CrestCreates.Agent.Memory.Stores;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.ContextPack.Abstractions;
using FluentAssertions;
using CrestCreates.Core.Abstractions.Identity;
using Xunit;

namespace CrestCreates.Agent.Memory.Tests;

public sealed class MainChainTests
{
    [Fact]
    public async Task FullMainChain_ConversationToAuthoringContext()
    {
        // Arrange
        var sanitizer = new DefaultAgentMemoryContentSanitizer();
        var conversationStore = new InMemoryAgentConversationStore(sanitizer);
        var taskStore = new InMemoryAgentTaskHistoryStore(sanitizer);
        var contextStore = new InMemoryAgentCompressedContextStore();
        var memoryStore = new InMemoryAgentMemoryStore();
        var compressor = new DefaultAgentContextCompressor(sanitizer);
        var extractor = new DefaultAgentMemoryExtractor();
        var promotionService = new DefaultAgentMemoryPromotionService(memoryStore);
        var retriever = new DefaultAgentMemoryRetriever(memoryStore);
        var expander = new DefaultAgentContextSourceExpander(conversationStore, taskStore, contextStore, memoryStore);
        var builder = new DefaultAgentAuthoringContextBuilder(retriever);

        const string tenantId = "tenant-1";
        const string conversationId = "conv-1";

        // Step 1: Save a conversation
        var conversation = new AgentConversationRecord
        {
            ConversationId = conversationId,
            TenantId = tenantId,
            Turns =
            [
                new AgentConversationTurn
                {
                    TurnId = "turn-1",
                    TenantId = tenantId,
                    Role = AgentConversationRole.User,
                    Content = "I prefer using UTC timestamps for all event records.",
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new AgentConversationTurn
                {
                    TurnId = "turn-2",
                    TenantId = tenantId,
                    Role = AgentConversationRole.Assistant,
                    Content = "Noted. I will use UTC timestamps.",
                    CreatedAt = DateTimeOffset.UtcNow
                }
            ]
        };
        await conversationStore.SaveConversationAsync(conversation);

        // Step 2: Compress the conversation
        var compressed = await compressor.CompressConversationAsync(conversation);
        compressed.Blocks.Should().HaveCount(2);
        await contextStore.SaveCompressedContextAsync(compressed);

        // Step 3: Extract candidates
        var candidates = await extractor.ExtractCandidatesAsync(compressed);
        candidates.Should().HaveCount(2);
        foreach (var candidate in candidates)
        {
            await memoryStore.SaveCandidateAsync(candidate);
        }

        // Step 4: Promote the first candidate
        var operationRequest = new AgentMemoryOperationRequest
        {
            TenantId = tenantId,
            Actor = new AgentActorContext { ActorId = "agent-1", ActorKind = "Agent" },
            Reason = "User preference detected",
            Timestamp = DateTimeOffset.UtcNow,
            Explanation = "Promoting based on conversation analysis"
        };
        var promotedMemory = await promotionService.PromoteAsync(tenantId, candidates[0].CandidateId, operationRequest);
        promotedMemory.Status.Should().Be(AgentMemoryStatus.Active);
        promotedMemory.Content.Should().Contain("UTC");

        // Step 5: Recall memories
        var query = new AgentMemoryQuery { TenantId = tenantId };
        var pack = await retriever.RecallAsync(query);
        pack.Memories.Should().ContainSingle(m => m.MemoryId == promotedMemory.MemoryId);
        pack.IsAuthoritative.Should().BeFalse();

        // Step 6: Build authoring context
        var authoringRequest = new AgentAuthoringRequest
        {
            TenantId = tenantId,
            IntentText = "What timestamp format should I use?"
        };
        var metadataContextPack = CreateMinimalMetadataContextPack(tenantId);
        var authoringContext = await builder.BuildAsync(authoringRequest, metadataContextPack);
        authoringContext.MemoryPack.Memories.Should().ContainSingle();
        authoringContext.MetadataContextPack.Request.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public async Task Promotion_RejectsNonCandidateStatus()
    {
        var memoryStore = new InMemoryAgentMemoryStore();
        var promotionService = new DefaultAgentMemoryPromotionService(memoryStore);

        const string tenantId = "tenant-1";

        var alreadyPromoted = new AgentMemoryCandidate
        {
            CandidateId = "c-already",
            TenantId = tenantId,
            Kind = AgentMemoryKind.Preference,
            Content = "Already promoted",
            CanonicalContentHash = "hash",
            Status = AgentMemoryStatus.Active
        };
        await memoryStore.SaveCandidateAsync(alreadyPromoted);

        var request = new AgentMemoryOperationRequest
        {
            TenantId = tenantId,
            Actor = new AgentActorContext { ActorId = "agent-1", ActorKind = "Agent" },
            Reason = "Test",
            Timestamp = DateTimeOffset.UtcNow,
            Explanation = "Testing"
        };

        var act = async () => await promotionService.PromoteAsync(tenantId, "c-already", request);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*status*Candidate*");
    }

    [Fact]
    public async Task Supersede_CreatesLink()
    {
        var memoryStore = new InMemoryAgentMemoryStore();
        var promotionService = new DefaultAgentMemoryPromotionService(memoryStore);

        const string tenantId = "tenant-1";

        // Promote first memory
        var candidate = new AgentMemoryCandidate
        {
            CandidateId = "c-1",
            TenantId = tenantId,
            Kind = AgentMemoryKind.Preference,
            Content = "Old preference",
            CanonicalContentHash = "hash1"
        };
        await memoryStore.SaveCandidateAsync(candidate);

        var promoteRequest = new AgentMemoryOperationRequest
        {
            TenantId = tenantId,
            Actor = new AgentActorContext { ActorId = "agent-1", ActorKind = "Agent" },
            Reason = "Initial",
            Timestamp = DateTimeOffset.UtcNow,
            Explanation = "Initial promotion"
        };
        var original = await promotionService.PromoteAsync(tenantId, "c-1", promoteRequest);

        // Supersede with new candidate
        var replacement = new AgentMemoryCandidate
        {
            CandidateId = "c-2",
            TenantId = tenantId,
            Kind = AgentMemoryKind.Preference,
            Content = "New preference",
            CanonicalContentHash = "hash2"
        };
        await memoryStore.SaveCandidateAsync(replacement);

        var supersedeRequest = new AgentMemoryOperationRequest
        {
            TenantId = tenantId,
            Actor = new AgentActorContext { ActorId = "agent-1", ActorKind = "Agent" },
            Reason = "Updated preference",
            Timestamp = DateTimeOffset.UtcNow,
            Explanation = "Updating preference"
        };
        var superseding = await promotionService.SupersedeAsync(tenantId, original.MemoryId, replacement, supersedeRequest);

        // Verify chain
        superseding.SupersedesMemoryId.Should().Be(original.MemoryId);
        var oldMemory = await memoryStore.GetMemoryAsync(tenantId, original.MemoryId);
        oldMemory!.Status.Should().Be(AgentMemoryStatus.Superseded);
        oldMemory.SupersededByMemoryId.Should().Be(replacement.CandidateId);
    }

    [Fact]
    public async Task SourceExpander_ResolvesConversationSource()
    {
        var sanitizer = new DefaultAgentMemoryContentSanitizer();
        var conversationStore = new InMemoryAgentConversationStore(sanitizer);
        var taskStore = new InMemoryAgentTaskHistoryStore(sanitizer);
        var contextStore = new InMemoryAgentCompressedContextStore();
        var memoryStore = new InMemoryAgentMemoryStore();
        var expander = new DefaultAgentContextSourceExpander(conversationStore, taskStore, contextStore, memoryStore);

        const string tenantId = "tenant-1";

        var conversation = new AgentConversationRecord
        {
            ConversationId = "conv-1",
            TenantId = tenantId,
            Turns =
            [
                new AgentConversationTurn
                {
                    TurnId = "turn-1",
                    TenantId = tenantId,
                    Role = AgentConversationRole.User,
                    Content = "Hello",
                    CreatedAt = DateTimeOffset.UtcNow
                }
            ]
        };
        await conversationStore.SaveConversationAsync(conversation);

        var sourceRef = new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.ConversationTurn,
            TenantId = tenantId,
            SourceId = "conv-1"
        };

        var result = await expander.ExpandAsync(sourceRef);
        result.Status.Should().Be(AgentMemorySourceExpansionStatus.Expanded);
        result.SanitizedContent.Should().Contain("Hello");
    }

    [Fact]
    public async Task SourceExpander_ReturnsNotFoundForMissingSource()
    {
        var sanitizer = new DefaultAgentMemoryContentSanitizer();
        var conversationStore = new InMemoryAgentConversationStore(sanitizer);
        var taskStore = new InMemoryAgentTaskHistoryStore(sanitizer);
        var contextStore = new InMemoryAgentCompressedContextStore();
        var memoryStore = new InMemoryAgentMemoryStore();
        var expander = new DefaultAgentContextSourceExpander(conversationStore, taskStore, contextStore, memoryStore);

        var sourceRef = new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.ConversationTurn,
            TenantId = "tenant-1",
            SourceId = "nonexistent"
        };

        var result = await expander.ExpandAsync(sourceRef);
        result.Status.Should().Be(AgentMemorySourceExpansionStatus.NotFound);
        result.Diagnostics.Should().NotBeEmpty();
    }

    private static MetadataContextPack CreateMinimalMetadataContextPack(string tenantId)
    {
        return new MetadataContextPack
        {
            Request = new MetadataContextPackRequest
            {
                Scope = MetadataContextPackScope.FocusOnly,
                FocusDescriptors = Array.Empty<DescriptorRef>(),
                TenantId = tenantId
            },
            Descriptors = Array.Empty<MetadataContextPackDescriptorEntry>(),
            Relationships = Array.Empty<MetadataContextPackRelationshipEntry>(),
            Summary = new MetadataContextPackSummary
            {
                TotalDescriptorCount = 0,
                DescriptorCountsByKind = new Dictionary<DescriptorKind, int>(),
                TotalRelationshipCount = 0,
                RelationshipCountsByKind = new Dictionary<RelationshipKind, int>(),
                FocusRefs = Array.Empty<DescriptorRef>(),
                WasTruncated = false,
                TruncatedAtCount = null,
                TraversalDepthReached = 0
            },
            Diagnostics = Array.Empty<MetadataContextPackDiagnostic>()
        };
    }

    [Fact]
    public async Task Promotion_RejectsAlreadyRejectedCandidate()
    {
        var memoryStore = new InMemoryAgentMemoryStore();
        var promotionService = new DefaultAgentMemoryPromotionService(memoryStore);

        const string tenantId = "tenant-1";

        var candidate = new AgentMemoryCandidate
        {
            CandidateId = "c-rejected",
            TenantId = tenantId,
            Kind = AgentMemoryKind.Preference,
            Content = "Test",
            CanonicalContentHash = "hash",
            Status = AgentMemoryStatus.Rejected
        };
        await memoryStore.SaveCandidateAsync(candidate);

        var request = new AgentMemoryOperationRequest
        {
            TenantId = tenantId,
            Actor = new AgentActorContext { ActorId = "agent-1", ActorKind = "Agent" },
            Reason = "Test",
            Timestamp = DateTimeOffset.UtcNow,
            Explanation = "Testing"
        };

        var act = async () => await promotionService.RejectAsync(tenantId, "c-rejected", request);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*status*Rejected*");
    }

    [Fact]
    public async Task Supersede_RejectsArchivedMemory()
    {
        var memoryStore = new InMemoryAgentMemoryStore();
        var promotionService = new DefaultAgentMemoryPromotionService(memoryStore);

        const string tenantId = "tenant-1";

        // Create and archive a memory
        var candidate = new AgentMemoryCandidate
        {
            CandidateId = "c-1",
            TenantId = tenantId,
            Kind = AgentMemoryKind.Preference,
            Content = "Archived",
            CanonicalContentHash = "hash1"
        };
        await memoryStore.SaveCandidateAsync(candidate);

        var promoteRequest = new AgentMemoryOperationRequest
        {
            TenantId = tenantId,
            Actor = new AgentActorContext { ActorId = "agent-1", ActorKind = "Agent" },
            Reason = "Initial",
            Timestamp = DateTimeOffset.UtcNow,
            Explanation = "Initial promotion"
        };
        var memory = await promotionService.PromoteAsync(tenantId, "c-1", promoteRequest);

        var archiveRequest = new AgentMemoryOperationRequest
        {
            TenantId = tenantId,
            Actor = new AgentActorContext { ActorId = "agent-1", ActorKind = "Agent" },
            Reason = "Archive",
            Timestamp = DateTimeOffset.UtcNow,
            Explanation = "Archiving memory"
        };
        await promotionService.ArchiveAsync(tenantId, memory.MemoryId, archiveRequest);

        // Try to supersede the archived memory
        var replacement = new AgentMemoryCandidate
        {
            CandidateId = "c-2",
            TenantId = tenantId,
            Kind = AgentMemoryKind.Preference,
            Content = "Replacement",
            CanonicalContentHash = "hash2"
        };
        await memoryStore.SaveCandidateAsync(replacement);

        var supersedeRequest = new AgentMemoryOperationRequest
        {
            TenantId = tenantId,
            Actor = new AgentActorContext { ActorId = "agent-1", ActorKind = "Agent" },
            Reason = "Supersede attempt",
            Timestamp = DateTimeOffset.UtcNow,
            Explanation = "Attempting supersede"
        };

        var act = async () => await promotionService.SupersedeAsync(tenantId, memory.MemoryId, replacement, supersedeRequest);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*status*Archived*");
    }

    [Fact]
    public async Task Archive_RejectsCandidateStatusMemory()
    {
        var memoryStore = new InMemoryAgentMemoryStore();
        var promotionService = new DefaultAgentMemoryPromotionService(memoryStore);

        const string tenantId = "tenant-1";

        // Directly save a memory with Candidate status (edge case)
        var memory = new AgentMemoryItem
        {
            MemoryId = "m-candidate",
            TenantId = tenantId,
            Kind = AgentMemoryKind.Preference,
            Content = "Should not be archivable",
            CanonicalContentHash = "hash",
            PromotedAt = DateTimeOffset.UtcNow,
            Status = AgentMemoryStatus.Candidate
        };
        await memoryStore.SaveMemoryAsync(memory);

        var request = new AgentMemoryOperationRequest
        {
            TenantId = tenantId,
            Actor = new AgentActorContext { ActorId = "agent-1", ActorKind = "Agent" },
            Reason = "Test",
            Timestamp = DateTimeOffset.UtcNow,
            Explanation = "Testing"
        };

        var act = async () => await promotionService.ArchiveAsync(tenantId, "m-candidate", request);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Recall_IsAlwaysNonAuthoritative_WithAndWithoutTruncation()
    {
        var memoryStore = new InMemoryAgentMemoryStore();
        var retriever = new DefaultAgentMemoryRetriever(memoryStore);

        const string tenantId = "tenant-1";

        // Save two memories that together exceed budget
        var mem1 = new AgentMemoryItem
        {
            MemoryId = "m-1",
            TenantId = tenantId,
            Kind = AgentMemoryKind.ProjectFact,
            Content = new string('a', 50),
            CanonicalContentHash = "hash1",
            PromotedAt = DateTimeOffset.UtcNow
        };
        var mem2 = new AgentMemoryItem
        {
            MemoryId = "m-2",
            TenantId = tenantId,
            Kind = AgentMemoryKind.ProjectFact,
            Content = new string('b', 50),
            CanonicalContentHash = "hash2",
            PromotedAt = DateTimeOffset.UtcNow
        };
        await memoryStore.SaveMemoryAsync(mem1);
        await memoryStore.SaveMemoryAsync(mem2);

        // Truncated recall
        var truncatedQuery = new AgentMemoryQuery
        {
            TenantId = tenantId,
            CharacterBudget = 60 // Only fits first memory
        };

        var truncatedPack = await retriever.RecallAsync(truncatedQuery);
        truncatedPack.Memories.Should().ContainSingle();
        truncatedPack.IsAuthoritative.Should().BeFalse();
        truncatedPack.Diagnostics.Should().Contain(d => d.Code == AgentMemoryDiagnosticCodes.BudgetTruncated);

        // Non-truncated recall should still have IsAuthoritative=false
        var fullQuery = new AgentMemoryQuery { TenantId = tenantId };
        var fullPack = await retriever.RecallAsync(fullQuery);
        fullPack.Memories.Should().HaveCount(2);
        fullPack.IsAuthoritative.Should().BeFalse();
    }

    [Fact]
    public async Task AppendEvent_ThrowsWhenTaskNotFound()
    {
        var sanitizer = new DefaultAgentMemoryContentSanitizer();
        var taskStore = new InMemoryAgentTaskHistoryStore(sanitizer);
        var taskEvent = new AgentTaskEvent
        {
            EventId = "evt-1",
            TenantId = "tenant-1",
            TaskId = "nonexistent-task",
            EventKind = "Progress",
            Content = "Started"
        };

        var act = async () => await taskStore.AppendEventAsync("tenant-1", "nonexistent-task", taskEvent);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
    }

    [Fact]
    public async Task MemoryStore_SnapshotPreventsExternalMutation()
    {
        var memoryStore = new InMemoryAgentMemoryStore();
        const string tenantId = "tenant-1";

        var tags = new List<string> { "tag1" };
        var candidate = new AgentMemoryCandidate
        {
            CandidateId = "c-1",
            TenantId = tenantId,
            Kind = AgentMemoryKind.Preference,
            Content = "Test",
            CanonicalContentHash = "hash",
            Tags = tags
        };
        await memoryStore.SaveCandidateAsync(candidate);

        // Mutate the original list
        tags.Add("tag2");

        // Retrieved candidate should not see the mutation
        var retrieved = await memoryStore.GetCandidateAsync(tenantId, "c-1");
        retrieved!.Tags.Should().ContainSingle();
    }

    // --- Fix #1a: Sanitizer tests ---

    [Fact]
    public void Sanitizer_RedactsBearerTokens()
    {
        var sanitizer = new DefaultAgentMemoryContentSanitizer();
        var result = sanitizer.Sanitize("tenant-1", "Authorization: Bearer abc123token", Array.Empty<AgentContextSourceRef>());

        result.Rejected.Should().BeFalse();
        result.RedactionKinds.Should().Contain(AgentMemoryDiagnosticCodes.AgentMemoryRedactionKinds.BearerToken);
        result.SanitizedContent.Should().NotContain("abc123token");
        result.SanitizedContent.Should().Contain("[REDACTED:bearer-token]");
    }

    [Fact]
    public void Sanitizer_RedactsCredentialAssignments()
    {
        var sanitizer = new DefaultAgentMemoryContentSanitizer();
        var result = sanitizer.Sanitize("tenant-1", "password=secret123", Array.Empty<AgentContextSourceRef>());

        result.Rejected.Should().BeTrue(); // Entirely credential content is rejected
        result.RedactionKinds.Should().Contain(AgentMemoryDiagnosticCodes.AgentMemoryRedactionKinds.Credential);
        result.SanitizedContent.Should().Contain("[REDACTED:credential]");
    }

    [Fact]
    public void Sanitizer_RedactsConnectionStringPasswords()
    {
        var sanitizer = new DefaultAgentMemoryContentSanitizer();
        var result = sanitizer.Sanitize("tenant-1", "Server=host;Password=mypass;Database=db", Array.Empty<AgentContextSourceRef>());

        result.Rejected.Should().BeFalse(); // Not entirely redacted — other parts remain
        result.RedactionKinds.Should().Contain(AgentMemoryDiagnosticCodes.AgentMemoryRedactionKinds.ConnectionCredential);
        result.SanitizedContent.Should().Contain("[REDACTED:connection-credential]");
        result.SanitizedContent.Should().NotContain("mypass");
    }

    [Fact]
    public void Sanitizer_RejectsEntirelyRedactedContent()
    {
        var sanitizer = new DefaultAgentMemoryContentSanitizer();
        // Content that is entirely a password assignment
        var result = sanitizer.Sanitize("tenant-1", "api_key=sk-abc123xyz", Array.Empty<AgentContextSourceRef>());

        result.Rejected.Should().BeTrue();
        result.SanitizedContent.Should().Contain("[REDACTED:credential]");
        result.Diagnostics.Should().Contain(d => d.Code == AgentMemoryDiagnosticCodes.ContentRejected);
    }

    // --- Fix #1b: Compressor sanitizes ---

    [Fact]
    public async Task Compressor_SanitizesBeforeCompressing()
    {
        var sanitizer = new DefaultAgentMemoryContentSanitizer();
        var compressor = new DefaultAgentContextCompressor(sanitizer);

        var conversation = new AgentConversationRecord
        {
            ConversationId = "conv-secrets",
            TenantId = "tenant-1",
            Turns =
            [
                new AgentConversationTurn
                {
                    TurnId = "turn-1",
                    TenantId = "tenant-1",
                    Role = AgentConversationRole.User,
                    Content = "Authorization: Bearer secret123",
                    CreatedAt = DateTimeOffset.UtcNow
                }
            ]
        };

        var compressed = await compressor.CompressConversationAsync(conversation);
        compressed.Blocks.Should().ContainSingle();
        compressed.Blocks[0].Content.Should().Contain("[REDACTED:bearer-token]");
        compressed.Blocks[0].Content.Should().NotContain("secret123");
        compressed.Diagnostics.Should().Contain(d => d.Code == AgentMemoryDiagnosticCodes.BlockSanitized);
    }

    [Fact]
    public async Task Compressor_SkipsRejectedBlocks()
    {
        var sanitizer = new DefaultAgentMemoryContentSanitizer();
        var compressor = new DefaultAgentContextCompressor(sanitizer);

        var conversation = new AgentConversationRecord
        {
            ConversationId = "conv-credentials",
            TenantId = "tenant-1",
            Turns =
            [
                new AgentConversationTurn
                {
                    TurnId = "turn-1",
                    TenantId = "tenant-1",
                    Role = AgentConversationRole.User,
                    Content = "api_key=sk-abc123xyz", // Entirely redacted
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new AgentConversationTurn
                {
                    TurnId = "turn-2",
                    TenantId = "tenant-1",
                    Role = AgentConversationRole.Assistant,
                    Content = "This is fine content.",
                    CreatedAt = DateTimeOffset.UtcNow
                }
            ]
        };

        var compressed = await compressor.CompressConversationAsync(conversation);
        // Only the fine content block should remain
        compressed.Blocks.Should().ContainSingle();
        compressed.Blocks[0].Content.Should().Contain("fine content");
        compressed.Diagnostics.Should().Contain(d => d.Code == AgentMemoryDiagnosticCodes.ContentRejected);
    }

    // --- Fix #2: Recall tests ---

    [Fact]
    public async Task Recall_IsAlwaysNonAuthoritative()
    {
        var memoryStore = new InMemoryAgentMemoryStore();
        var retriever = new DefaultAgentMemoryRetriever(memoryStore);

        const string tenantId = "tenant-1";
        var memory = new AgentMemoryItem
        {
            MemoryId = "m-1",
            TenantId = tenantId,
            Kind = AgentMemoryKind.ProjectFact,
            Content = "Some memory",
            CanonicalContentHash = "hash",
            PromotedAt = DateTimeOffset.UtcNow
        };
        await memoryStore.SaveMemoryAsync(memory);

        var pack = await retriever.RecallAsync(new AgentMemoryQuery { TenantId = tenantId });
        pack.Memories.Should().ContainSingle();
        pack.IsAuthoritative.Should().BeFalse(); // Always false, even without truncation
    }

    [Fact]
    public async Task Recall_EmitsDiagnosticWhenBudgetTruncates()
    {
        var memoryStore = new InMemoryAgentMemoryStore();
        var retriever = new DefaultAgentMemoryRetriever(memoryStore);

        const string tenantId = "tenant-1";

        await memoryStore.SaveMemoryAsync(new AgentMemoryItem
        {
            MemoryId = "m-1",
            TenantId = tenantId,
            Kind = AgentMemoryKind.ProjectFact,
            Content = new string('a', 50),
            CanonicalContentHash = "hash1",
            PromotedAt = DateTimeOffset.UtcNow
        });
        await memoryStore.SaveMemoryAsync(new AgentMemoryItem
        {
            MemoryId = "m-2",
            TenantId = tenantId,
            Kind = AgentMemoryKind.ProjectFact,
            Content = new string('b', 50),
            CanonicalContentHash = "hash2",
            PromotedAt = DateTimeOffset.UtcNow
        });

        var pack = await retriever.RecallAsync(new AgentMemoryQuery
        {
            TenantId = tenantId,
            CharacterBudget = 60
        });

        pack.Diagnostics.Should().Contain(d => d.Code == AgentMemoryDiagnosticCodes.BudgetTruncated);
        pack.Diagnostics.Should().Contain(d => d.Severity == SeverityLevel.Warning);
    }

    // --- Fix #3: Store does not apply recall-level filters ---

    [Fact]
    public async Task Store_DoesNotApplyRecallFilters()
    {
        var memoryStore = new InMemoryAgentMemoryStore();
        const string tenantId = "tenant-1";

        var lowConfidence = new AgentMemoryItem
        {
            MemoryId = "m-low",
            TenantId = tenantId,
            Kind = AgentMemoryKind.ProjectFact,
            Content = "Low confidence memory",
            CanonicalContentHash = "hash",
            PromotedAt = DateTimeOffset.UtcNow,
            Confidence = AgentMemoryConfidence.Low
        };
        await memoryStore.SaveMemoryAsync(lowConfidence);

        // Query with MinimumConfidence=High — store should still return it (retriever filters later)
        var storeQuery = new AgentMemoryQuery { TenantId = tenantId, MinimumConfidence = AgentMemoryConfidence.Low };
        var results = await memoryStore.ListMemoriesAsync(storeQuery);
        results.Should().ContainSingle(); // Store returns everything matching persistence fields
    }

    // --- Fix #4: Promotion validation tests ---

    [Fact]
    public async Task Promotion_RequiresNonEmptyReason()
    {
        var memoryStore = new InMemoryAgentMemoryStore();
        var promotionService = new DefaultAgentMemoryPromotionService(memoryStore);

        const string tenantId = "tenant-1";
        var candidate = new AgentMemoryCandidate
        {
            CandidateId = "c-1",
            TenantId = tenantId,
            Kind = AgentMemoryKind.Preference,
            Content = "Test",
            CanonicalContentHash = "hash"
        };
        await memoryStore.SaveCandidateAsync(candidate);

        var request = new AgentMemoryOperationRequest
        {
            TenantId = tenantId,
            Actor = new AgentActorContext { ActorId = "agent-1", ActorKind = "Agent" },
            Reason = "",
            Timestamp = DateTimeOffset.UtcNow,
            Explanation = "Testing empty reason"
        };

        var act = async () => await promotionService.PromoteAsync(tenantId, "c-1", request);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{AgentMemoryDiagnosticCodes.InvalidOperationMissingReason}*");
    }

    [Fact]
    public async Task Promotion_RequiresSourceRefsOrExplanation()
    {
        var memoryStore = new InMemoryAgentMemoryStore();
        var promotionService = new DefaultAgentMemoryPromotionService(memoryStore);

        const string tenantId = "tenant-1";
        var candidate = new AgentMemoryCandidate
        {
            CandidateId = "c-1",
            TenantId = tenantId,
            Kind = AgentMemoryKind.Preference,
            Content = "Test",
            CanonicalContentHash = "hash"
        };
        await memoryStore.SaveCandidateAsync(candidate);

        var request = new AgentMemoryOperationRequest
        {
            TenantId = tenantId,
            Actor = new AgentActorContext { ActorId = "agent-1", ActorKind = "Agent" },
            Reason = "Valid reason",
            Timestamp = DateTimeOffset.UtcNow
            // No SourceRefs and no Explanation
        };

        var act = async () => await promotionService.PromoteAsync(tenantId, "c-1", request);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{AgentMemoryDiagnosticCodes.InvalidOperationMissingSourceOrExplanation}*");
    }

    [Fact]
    public async Task Promotion_RequiresActorContext()
    {
        var memoryStore = new InMemoryAgentMemoryStore();
        var promotionService = new DefaultAgentMemoryPromotionService(memoryStore);

        const string tenantId = "tenant-1";
        var candidate = new AgentMemoryCandidate
        {
            CandidateId = "c-1",
            TenantId = tenantId,
            Kind = AgentMemoryKind.Preference,
            Content = "Test",
            CanonicalContentHash = "hash"
        };
        await memoryStore.SaveCandidateAsync(candidate);

        var request = new AgentMemoryOperationRequest
        {
            TenantId = tenantId,
            Actor = new AgentActorContext { ActorId = "", ActorKind = "Agent" },
            Reason = "Valid reason",
            Timestamp = DateTimeOffset.UtcNow,
            Explanation = "Testing empty ActorId"
        };

        var act = async () => await promotionService.PromoteAsync(tenantId, "c-1", request);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{AgentMemoryDiagnosticCodes.InvalidOperationMissingActor}*");
    }

    [Fact]
    public async Task Promotion_RequiresMatchingTenantId()
    {
        var memoryStore = new InMemoryAgentMemoryStore();
        var promotionService = new DefaultAgentMemoryPromotionService(memoryStore);

        const string tenantId = "tenant-1";
        var candidate = new AgentMemoryCandidate
        {
            CandidateId = "c-1",
            TenantId = tenantId,
            Kind = AgentMemoryKind.Preference,
            Content = "Test",
            CanonicalContentHash = "hash"
        };
        await memoryStore.SaveCandidateAsync(candidate);

        var request = new AgentMemoryOperationRequest
        {
            TenantId = "tenant-2", // Mismatched!
            Actor = new AgentActorContext { ActorId = "agent-1", ActorKind = "Agent" },
            Reason = "Valid reason",
            Timestamp = DateTimeOffset.UtcNow,
            Explanation = "Testing tenant mismatch"
        };

        var act = async () => await promotionService.PromoteAsync("tenant-1", "c-1", request);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{AgentMemoryDiagnosticCodes.InvalidOperationTenantMismatch}*");
    }

    // --- Fix #5: Snapshot-on-read tests ---

    [Fact]
    public async Task Store_ReturnsSnapshotCopies()
    {
        var sanitizer = new DefaultAgentMemoryContentSanitizer();
        var conversationStore = new InMemoryAgentConversationStore(sanitizer);
        const string tenantId = "tenant-1";

        var conversation = new AgentConversationRecord
        {
            ConversationId = "conv-snapshot",
            TenantId = tenantId,
            Turns =
            [
                new AgentConversationTurn
                {
                    TurnId = "turn-1",
                    TenantId = tenantId,
                    Role = AgentConversationRole.User,
                    Content = "Hello",
                    CreatedAt = DateTimeOffset.UtcNow
                }
            ]
        };
        await conversationStore.SaveConversationAsync(conversation);

        var retrieved = await conversationStore.GetConversationAsync(tenantId, "conv-snapshot");
        retrieved!.Turns.Should().ContainSingle();

        // Retrieve again — should be a different array instance (snapshot copy)
        var retrievedAgain = await conversationStore.GetConversationAsync(tenantId, "conv-snapshot");
        retrievedAgain!.Turns.Should().ContainSingle();

        // Verify they are different array instances
        retrieved.Turns.Should().NotBeSameAs(retrievedAgain.Turns);
    }

    // --- Fix #1 tests: Store sanitization ---

    [Fact]
    public async Task Store_SanitizesContentOnSave()
    {
        var sanitizer = new DefaultAgentMemoryContentSanitizer();
        var store = new InMemoryAgentConversationStore(sanitizer);
        const string tenantId = "tenant-1";

        var conversation = new AgentConversationRecord
        {
            ConversationId = "conv-sanitize",
            TenantId = tenantId,
            Turns =
            [
                new AgentConversationTurn
                {
                    TurnId = "turn-1",
                    TenantId = tenantId,
                    Role = AgentConversationRole.User,
                    Content = "Authorization: Bearer abc123token",
                    CreatedAt = DateTimeOffset.UtcNow
                }
            ]
        };
        await store.SaveConversationAsync(conversation);

        var retrieved = await store.GetConversationAsync(tenantId, "conv-sanitize");
        retrieved!.Turns.Should().ContainSingle();
        retrieved.Turns[0].Content.Should().Contain("[REDACTED:bearer-token]");
        retrieved.Turns[0].Content.Should().NotContain("abc123token");
    }

    [Fact]
    public async Task Store_SanitizesTaskEventContentOnSave()
    {
        var sanitizer = new DefaultAgentMemoryContentSanitizer();
        var store = new InMemoryAgentTaskHistoryStore(sanitizer);
        const string tenantId = "tenant-1";

        var task = new AgentTaskRecord
        {
            TaskId = "task-sanitize",
            TenantId = tenantId,
            Title = "Sensitive task",
            Events =
            [
                new AgentTaskEvent
                {
                    EventId = "evt-1",
                    TenantId = tenantId,
                    TaskId = "task-sanitize",
                    EventKind = "Progress",
                    Content = "password=secret"
                }
            ]
        };
        await store.SaveTaskAsync(task);

        var retrieved = await store.GetTaskAsync(tenantId, "task-sanitize");
        retrieved!.Events.Should().ContainSingle();
        retrieved.Events[0].Content.Should().Contain("[REDACTED:credential]");
        retrieved.Events[0].Content.Should().NotContain("secret");
    }

    [Fact]
    public async Task Expander_ReturnsSanitizedContent()
    {
        var sanitizer = new DefaultAgentMemoryContentSanitizer();
        var conversationStore = new InMemoryAgentConversationStore(sanitizer);
        var taskStore = new InMemoryAgentTaskHistoryStore(sanitizer);
        var contextStore = new InMemoryAgentCompressedContextStore();
        var memoryStore = new InMemoryAgentMemoryStore();
        var expander = new DefaultAgentContextSourceExpander(conversationStore, taskStore, contextStore, memoryStore);
        const string tenantId = "tenant-1";

        var conversation = new AgentConversationRecord
        {
            ConversationId = "conv-expand-secret",
            TenantId = tenantId,
            Turns =
            [
                new AgentConversationTurn
                {
                    TurnId = "turn-1",
                    TenantId = tenantId,
                    Role = AgentConversationRole.User,
                    Content = "My token is Bearer xyz789secret",
                    CreatedAt = DateTimeOffset.UtcNow
                }
            ]
        };
        await conversationStore.SaveConversationAsync(conversation);

        var sourceRef = new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.ConversationTurn,
            TenantId = tenantId,
            SourceId = "conv-expand-secret"
        };

        var result = await expander.ExpandAsync(sourceRef);
        result.Status.Should().Be(AgentMemorySourceExpansionStatus.Expanded);
        result.SanitizedContent.Should().Contain("[REDACTED:bearer-token]");
        result.SanitizedContent.Should().NotContain("xyz789secret");
    }

    // --- Fix #2 tests: Compressor synthetic SourceRef ---

    [Fact]
    public async Task Compressor_GeneratesSyntheticSourceRef()
    {
        var sanitizer = new DefaultAgentMemoryContentSanitizer();
        var compressor = new DefaultAgentContextCompressor(sanitizer);

        var conversation = new AgentConversationRecord
        {
            ConversationId = "conv-no-refs",
            TenantId = "tenant-1",
            Turns =
            [
                new AgentConversationTurn
                {
                    TurnId = "turn-1",
                    TenantId = "tenant-1",
                    Role = AgentConversationRole.User,
                    Content = "Hello world",
                    CreatedAt = DateTimeOffset.UtcNow
                    // No SourceRefs
                }
            ]
        };

        var compressed = await compressor.CompressConversationAsync(conversation);
        compressed.Blocks.Should().ContainSingle();
        compressed.Blocks[0].SourceRefs.Should().ContainSingle();
        compressed.Blocks[0].SourceRefs[0].SourceKind.Should().Be(AgentSourceKind.ConversationTurn);
        compressed.Blocks[0].SourceRefs[0].SourceId.Should().Be("conv-no-refs");
        compressed.Blocks[0].SourceRefs[0].RangeStart.Should().Be(0);
        compressed.Blocks[0].SourceRefs[0].RangeEnd.Should().Be(0);
        compressed.Blocks[0].SourceRefs[0].CanonicalContentHash.Should().NotBeNullOrEmpty();
    }

    // --- Fix #3 tests: Deterministic recall ordering ---

    [Fact]
    public async Task Recall_DeterministicOrdering()
    {
        var memoryStore = new InMemoryAgentMemoryStore();
        var retriever = new DefaultAgentMemoryRetriever(memoryStore);
        const string tenantId = "tenant-1";

        await memoryStore.SaveMemoryAsync(new AgentMemoryItem
        {
            MemoryId = "m-low",
            TenantId = tenantId,
            Kind = AgentMemoryKind.ProjectFact,
            Content = "Low confidence",
            CanonicalContentHash = "hash1",
            PromotedAt = DateTimeOffset.UtcNow,
            Confidence = AgentMemoryConfidence.Low
        });
        await memoryStore.SaveMemoryAsync(new AgentMemoryItem
        {
            MemoryId = "m-high",
            TenantId = tenantId,
            Kind = AgentMemoryKind.ProjectFact,
            Content = "High confidence",
            CanonicalContentHash = "hash2",
            PromotedAt = DateTimeOffset.UtcNow,
            Confidence = AgentMemoryConfidence.High
        });
        await memoryStore.SaveMemoryAsync(new AgentMemoryItem
        {
            MemoryId = "m-med",
            TenantId = tenantId,
            Kind = AgentMemoryKind.ProjectFact,
            Content = "Medium confidence",
            CanonicalContentHash = "hash3",
            PromotedAt = DateTimeOffset.UtcNow,
            Confidence = AgentMemoryConfidence.Medium
        });

        var pack = await retriever.RecallAsync(new AgentMemoryQuery
        {
            TenantId = tenantId,
            MaxCount = 2
        });

        pack.Memories.Should().HaveCount(2);
        pack.Memories[0].MemoryId.Should().Be("m-high"); // High confidence first
        pack.Memories[1].MemoryId.Should().Be("m-med");  // Then medium
    }

    [Fact]
    public async Task Recall_DeterministicOrdering_SameConfidence()
    {
        var memoryStore = new InMemoryAgentMemoryStore();
        var retriever = new DefaultAgentMemoryRetriever(memoryStore);
        const string tenantId = "tenant-1";
        var promotedAt = DateTimeOffset.UtcNow;

        await memoryStore.SaveMemoryAsync(new AgentMemoryItem
        {
            MemoryId = "m-c",
            TenantId = tenantId,
            Kind = AgentMemoryKind.ProjectFact,
            Content = "Memory C",
            CanonicalContentHash = "hash1",
            PromotedAt = promotedAt,
            Confidence = AgentMemoryConfidence.Medium
        });
        await memoryStore.SaveMemoryAsync(new AgentMemoryItem
        {
            MemoryId = "m-a",
            TenantId = tenantId,
            Kind = AgentMemoryKind.ProjectFact,
            Content = "Memory A",
            CanonicalContentHash = "hash2",
            PromotedAt = promotedAt,
            Confidence = AgentMemoryConfidence.Medium
        });
        await memoryStore.SaveMemoryAsync(new AgentMemoryItem
        {
            MemoryId = "m-b",
            TenantId = tenantId,
            Kind = AgentMemoryKind.ProjectFact,
            Content = "Memory B",
            CanonicalContentHash = "hash3",
            PromotedAt = promotedAt,
            Confidence = AgentMemoryConfidence.Medium
        });

        var pack = await retriever.RecallAsync(new AgentMemoryQuery
        {
            TenantId = tenantId,
            MaxCount = 2
        });

        pack.Memories.Should().HaveCount(2);
        // Tie-breaker is MemoryId alphabetically: m-a, m-b, m-c → first two are m-a and m-b
        pack.Memories[0].MemoryId.Should().Be("m-a");
        pack.Memories[1].MemoryId.Should().Be("m-b");
    }

    // --- Fix #4 test: VisibleDescriptorKinds fail-closed ---

    [Fact]
    public async Task Recall_VisibleDescriptorKinds_FailClosed()
    {
        var memoryStore = new InMemoryAgentMemoryStore();
        var retriever = new DefaultAgentMemoryRetriever(memoryStore);
        const string tenantId = "tenant-1";

        await memoryStore.SaveMemoryAsync(new AgentMemoryItem
        {
            MemoryId = "m-1",
            TenantId = tenantId,
            Kind = AgentMemoryKind.ProjectFact,
            Content = "Some memory",
            CanonicalContentHash = "hash",
            PromotedAt = DateTimeOffset.UtcNow
        });

        var pack = await retriever.RecallAsync(new AgentMemoryQuery
        {
            TenantId = tenantId,
            VisibleDescriptorKinds = [DescriptorKind.Schema]
        });

        pack.Memories.Should().BeEmpty();
        pack.Diagnostics.Should().Contain(d => d.Code == AgentMemoryDiagnosticCodes.VisibilityKindUnresolvable);
        pack.Diagnostics.Should().Contain(d => d.Severity == SeverityLevel.Warning);
    }
}
