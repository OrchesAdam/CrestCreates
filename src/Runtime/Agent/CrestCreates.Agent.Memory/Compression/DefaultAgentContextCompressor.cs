using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Identity;
using CrestCreates.Agent.Memory.CanonicalHashing;
using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.Memory.Compression;

public sealed class DefaultAgentContextCompressor : IAgentContextCompressor
{
    private readonly IAgentMemoryContentSanitizer _sanitizer;
    private readonly IAgentMemoryArtifactIdGenerator _ids;
    private readonly AgentMemoryCanonicalHashProjector? _hashProjector;

    public DefaultAgentContextCompressor(
        IAgentMemoryContentSanitizer sanitizer,
        IAgentMemoryArtifactIdGenerator? ids = null,
        AgentMemoryCanonicalHashProjector? hashProjector = null)
    {
        _sanitizer = sanitizer;
        _ids = ids ?? new DefaultAgentMemoryArtifactIdGenerator();
        _hashProjector = hashProjector;
    }

    public ValueTask<AgentCompressedContext> CompressConversationAsync(AgentConversationRecord conversation, CancellationToken cancellationToken = default)
    {
        var blocks = new List<AgentCompressedContextBlock>();
        var diagnostics = new List<AgentMemoryDiagnostic>();

        for (var i = 0; i < conversation.Turns.Count; i++)
        {
            var turn = conversation.Turns[i];
            var sanitized = _sanitizer.Sanitize(conversation.TenantId, turn.Content, turn.SourceRefs);

            if (sanitized.Rejected)
            {
                diagnostics.Add(new AgentMemoryDiagnostic
                {
                    Code = AgentMemoryDiagnosticCodes.ContentRejected,
                    Message = $"Skipped block for turn '{turn.TurnId}' because content was rejected after sanitization.",
                    Severity = SeverityLevel.Warning,
                    SourceRefs = turn.SourceRefs
                });
                continue;
            }

            if (sanitized.RedactionKinds.Count > 0)
            {
                diagnostics.Add(new AgentMemoryDiagnostic
                {
                    Code = AgentMemoryDiagnosticCodes.BlockSanitized,
                    Message = $"Block for turn '{turn.TurnId}' was sanitized (redactions: {string.Join(", ", sanitized.RedactionKinds)}).",
                    Severity = SeverityLevel.Info,
                    SourceRefs = turn.SourceRefs
                });
            }

            var content = $"[{turn.Role}] {sanitized.SanitizedContent}";

            var blockSourceRefs = turn.SourceRefs.Count > 0
                ? turn.SourceRefs.ToArray()
                : new AgentContextSourceRef[]
                {
                    new()
                    {
                        SourceKind = AgentSourceKind.ConversationTurn,
                        TenantId = conversation.TenantId,
                        SourceId = conversation.ConversationId,
                        RangeStart = i,
                        RangeEnd = i,
                        CanonicalContentHash = sanitized.CanonicalContentHash
                    }
                };
            var blockHash = _hashProjector?.ComputeContentHash(conversation.TenantId, blockSourceRefs, content)
                ?? sanitized.CanonicalContentHash;

            blocks.Add(new AgentCompressedContextBlock
            {
                BlockId = _ids.CreateBlockId(),
                TenantId = conversation.TenantId,
                Content = content,
                CanonicalContentHash = blockHash,
                SourceRefs = blockSourceRefs,
                Diagnostics = sanitized.Diagnostics.ToArray()
            });
        }

        return new ValueTask<AgentCompressedContext>(new AgentCompressedContext
        {
            ContextId = _ids.CreateContextId(),
            TenantId = conversation.TenantId,
            Blocks = blocks.ToArray(),
            Diagnostics = diagnostics.ToArray()
        });
    }

    public ValueTask<AgentCompressedContext> CompressTaskAsync(AgentTaskRecord task, CancellationToken cancellationToken = default)
    {
        var blocks = new List<AgentCompressedContextBlock>();
        var diagnostics = new List<AgentMemoryDiagnostic>();

        var rawSummaryContent = $"{task.Title}: {task.Summary ?? "No summary"}";
        var summarySanitized = _sanitizer.Sanitize(task.TenantId, rawSummaryContent, Array.Empty<AgentContextSourceRef>());

        if (!summarySanitized.Rejected)
        {
            if (summarySanitized.RedactionKinds.Count > 0)
            {
                diagnostics.Add(new AgentMemoryDiagnostic
                {
                    Code = AgentMemoryDiagnosticCodes.BlockSanitized,
                    Message = $"Task summary block was sanitized (redactions: {string.Join(", ", summarySanitized.RedactionKinds)}).",
                    Severity = SeverityLevel.Info
                });
            }

            var summaryBlockSourceRefs = new AgentContextSourceRef[]
            {
                new()
                {
                    SourceKind = AgentSourceKind.TaskRecord,
                    TenantId = task.TenantId,
                    SourceId = task.TaskId,
                    CanonicalContentHash = summarySanitized.CanonicalContentHash
                }
            };
            var summaryContent = $"[Task] {summarySanitized.SanitizedContent}";
            var summaryHash = _hashProjector?.ComputeContentHash(task.TenantId, summaryBlockSourceRefs, summaryContent)
                ?? summarySanitized.CanonicalContentHash;

            blocks.Add(new AgentCompressedContextBlock
            {
                BlockId = _ids.CreateBlockId(),
                TenantId = task.TenantId,
                Content = summaryContent,
                CanonicalContentHash = summaryHash,
                SourceRefs = summaryBlockSourceRefs
            });
        }

        for (var j = 0; j < task.Events.Count; j++)
        {
            var evt = task.Events[j];
            var sanitized = _sanitizer.Sanitize(task.TenantId, evt.Content, evt.SourceRefs);

            if (sanitized.Rejected)
            {
                diagnostics.Add(new AgentMemoryDiagnostic
                {
                    Code = AgentMemoryDiagnosticCodes.ContentRejected,
                    Message = $"Skipped block for event '{evt.EventId}' because content was rejected after sanitization.",
                    Severity = SeverityLevel.Warning,
                    SourceRefs = evt.SourceRefs
                });
                continue;
            }

            if (sanitized.RedactionKinds.Count > 0)
            {
                diagnostics.Add(new AgentMemoryDiagnostic
                {
                    Code = AgentMemoryDiagnosticCodes.BlockSanitized,
                    Message = $"Event block for '{evt.EventId}' was sanitized (redactions: {string.Join(", ", sanitized.RedactionKinds)}).",
                    Severity = SeverityLevel.Info,
                    SourceRefs = evt.SourceRefs
                });
            }

            var content = $"[{evt.EventKind}] {sanitized.SanitizedContent}";

            var eventBlockSourceRefs = evt.SourceRefs.Count > 0
                ? evt.SourceRefs.ToArray()
                : new AgentContextSourceRef[]
                {
                    new()
                    {
                        SourceKind = AgentSourceKind.TaskEvent,
                        TenantId = task.TenantId,
                        SourceId = task.TaskId,
                        RangeStart = j,
                        RangeEnd = j,
                        CanonicalContentHash = sanitized.CanonicalContentHash
                    }
                };
            var eventHash = _hashProjector?.ComputeContentHash(task.TenantId, eventBlockSourceRefs, content)
                ?? sanitized.CanonicalContentHash;

            blocks.Add(new AgentCompressedContextBlock
            {
                BlockId = _ids.CreateBlockId(),
                TenantId = task.TenantId,
                Content = content,
                CanonicalContentHash = eventHash,
                SourceRefs = eventBlockSourceRefs,
                Diagnostics = sanitized.Diagnostics.ToArray()
            });
        }

        return new ValueTask<AgentCompressedContext>(new AgentCompressedContext
        {
            ContextId = _ids.CreateContextId(),
            TenantId = task.TenantId,
            Blocks = blocks.ToArray(),
            Diagnostics = diagnostics.ToArray()
        });
    }
}
