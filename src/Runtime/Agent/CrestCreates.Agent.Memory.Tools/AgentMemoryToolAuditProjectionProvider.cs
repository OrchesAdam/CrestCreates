using CrestCreates.Agent.Tools;

namespace CrestCreates.Agent.Memory.Tools;

internal sealed class AgentMemoryToolAuditProjectionProvider : IAgentToolOutputAuditProjectionProvider, IAgentToolOutputOutcomeCodeProvider
{
    public Func<object?, string?>? CreateOutcomeCode(string toolName, Type outputType)
        => toolName switch
        {
            AgentMemoryToolCapabilityIds.BuildPack when outputType == typeof(BuildAgentMemoryPackResult) => value => ToWire(((BuildAgentMemoryPackResult)value!).OperationStatus),
            AgentMemoryToolCapabilityIds.ExpandSource when outputType == typeof(ExpandAgentMemorySourceResult) => value => ToWire(((ExpandAgentMemorySourceResult)value!).OperationStatus),
            AgentMemoryToolCapabilityIds.CompressHistory when outputType == typeof(CompressAgentHistoryResult) => value => ToWire(((CompressAgentHistoryResult)value!).OperationStatus),
            AgentMemoryToolCapabilityIds.ExtractCandidates when outputType == typeof(ExtractMemoryCandidatesResult) => value => ToWire(((ExtractMemoryCandidatesResult)value!).OperationStatus),
            AgentMemoryToolCapabilityIds.PromoteCandidate when outputType == typeof(PromoteMemoryCandidateResult) => value => ToWire(((PromoteMemoryCandidateResult)value!).OperationStatus),
            AgentMemoryToolCapabilityIds.RejectCandidate when outputType == typeof(RejectMemoryCandidateResult) => value => ToWire(((RejectMemoryCandidateResult)value!).OperationStatus),
            AgentMemoryToolCapabilityIds.SupersedeItem when outputType == typeof(SupersedeMemoryItemResult) => value => ToWire(((SupersedeMemoryItemResult)value!).OperationStatus),
            _ => null
        };

    public Func<object?, IReadOnlyList<AgentToolAuditFact>>? Create(string toolName, Type outputType)
        => toolName switch
        {
            AgentMemoryToolCapabilityIds.BuildPack when outputType == typeof(BuildAgentMemoryPackResult) => ProjectBuild,
            AgentMemoryToolCapabilityIds.ExpandSource when outputType == typeof(ExpandAgentMemorySourceResult) => ProjectExpand,
            AgentMemoryToolCapabilityIds.CompressHistory when outputType == typeof(CompressAgentHistoryResult) => ProjectCompress,
            AgentMemoryToolCapabilityIds.ExtractCandidates when outputType == typeof(ExtractMemoryCandidatesResult) => ProjectExtract,
            AgentMemoryToolCapabilityIds.PromoteCandidate when outputType == typeof(PromoteMemoryCandidateResult) => ProjectPromote,
            AgentMemoryToolCapabilityIds.RejectCandidate when outputType == typeof(RejectMemoryCandidateResult) => ProjectReject,
            AgentMemoryToolCapabilityIds.SupersedeItem when outputType == typeof(SupersedeMemoryItemResult) => ProjectSupersede,
            _ => null
        };

    private static IReadOnlyList<AgentToolAuditFact> ProjectBuild(object? value)
    {
        var result = (BuildAgentMemoryPackResult)value!;
        var facts = Common(result.OperationStatus);
        Add(facts, "output.returned-count", result.ReturnedCount);
        Add(facts, "output.was-truncated", result.WasTruncated);
        for (var i = 0; i < result.Items.Count; i++)
        {
            var item = result.Items[i];
            Add(facts, $"output.items[{i}].memory-status", item.MemoryStatus);
            Add(facts, $"output.items[{i}].canonical-content-hash", item.CanonicalContentHash.Value);
        }
        return facts;
    }

    private static IReadOnlyList<AgentToolAuditFact> ProjectExpand(object? value)
    {
        var result = (ExpandAgentMemorySourceResult)value!;
        var facts = Common(result.OperationStatus);
        Add(facts, "output.was-truncated", result.WasTruncated);
        if (result.CanonicalContentHash is not null)
            Add(facts, "output.canonical-content-hash", result.CanonicalContentHash.Value);
        return facts;
    }

    private static IReadOnlyList<AgentToolAuditFact> ProjectCompress(object? value)
    {
        var result = (CompressAgentHistoryResult)value!;
        var facts = Common(result.OperationStatus);
        Add(facts, "output.block-count", result.BlockCount);
        for (var i = 0; i < result.Blocks.Count; i++)
            Add(facts, $"output.blocks[{i}].canonical-content-hash", result.Blocks[i].CanonicalContentHash.Value);
        return facts;
    }

    private static IReadOnlyList<AgentToolAuditFact> ProjectExtract(object? value)
    {
        var result = (ExtractMemoryCandidatesResult)value!;
        var facts = Common(result.OperationStatus);
        Add(facts, "output.candidate-count", result.CandidateCount);
        for (var i = 0; i < result.Candidates.Count; i++)
        {
            var candidate = result.Candidates[i];
            Add(facts, $"output.candidates[{i}].candidate-status", candidate.CandidateStatus);
            Add(facts, $"output.candidates[{i}].canonical-content-hash", candidate.CanonicalContentHash.Value);
        }
        return facts;
    }

    private static IReadOnlyList<AgentToolAuditFact> ProjectPromote(object? value)
    {
        var result = (PromoteMemoryCandidateResult)value!;
        var facts = Common(result.OperationStatus);
        AddItem(facts, result.Item);
        return facts;
    }

    private static IReadOnlyList<AgentToolAuditFact> ProjectReject(object? value)
    {
        var result = (RejectMemoryCandidateResult)value!;
        var facts = Common(result.OperationStatus);
        if (result.CandidateStatus is { } status)
            Add(facts, "output.candidate-status", status);
        return facts;
    }

    private static IReadOnlyList<AgentToolAuditFact> ProjectSupersede(object? value)
    {
        var result = (SupersedeMemoryItemResult)value!;
        var facts = Common(result.OperationStatus);
        AddItem(facts, result.Item);
        return facts;
    }

    private static List<AgentToolAuditFact> Common(AgentMemoryToolOperationStatus status)
    {
        var facts = new List<AgentToolAuditFact>();
        Add(facts, "output.operation-status", status);
        return facts;
    }

    private static string? ToWire(AgentMemoryToolOperationStatus status)
        => status switch
        {
            AgentMemoryToolOperationStatus.Completed => "completed",
            AgentMemoryToolOperationStatus.Unavailable => "unavailable",
            AgentMemoryToolOperationStatus.Conflict => "conflict",
            AgentMemoryToolOperationStatus.Redacted => "redacted",
            AgentMemoryToolOperationStatus.NotExpandable => "not-expandable",
            _ => null
        };

    private static void AddItem(List<AgentToolAuditFact> facts, AgentMemoryToolItemDto? item)
    {
        if (item is null) return;
        Add(facts, "output.item.memory-status", item.MemoryStatus);
        Add(facts, "output.item.canonical-content-hash", item.CanonicalContentHash.Value);
    }

    private static void Add(List<AgentToolAuditFact> facts, string code, object value)
        => facts.Add(new AgentToolAuditFact
        {
            Code = code,
            Value = value switch
            {
                bool boolean => boolean ? "true" : "false",
                AgentMemoryToolOperationStatus operationStatus => ToWire(operationStatus),
                AgentMemoryToolMemoryStatus memoryStatus => ToWire(memoryStatus),
                AgentMemoryToolCandidateStatus candidateStatus => ToWire(candidateStatus),
                _ => value.ToString()
            },
            Kind = AgentToolAuditFactKind.Output
        });

    private static string? ToWire(AgentMemoryToolMemoryStatus status)
        => status switch
        {
            AgentMemoryToolMemoryStatus.Active => "active",
            AgentMemoryToolMemoryStatus.Superseded => "superseded",
            AgentMemoryToolMemoryStatus.Archived => "archived",
            _ => null
        };

    private static string? ToWire(AgentMemoryToolCandidateStatus status)
        => status switch
        {
            AgentMemoryToolCandidateStatus.Candidate => "candidate",
            AgentMemoryToolCandidateStatus.Active => "active",
            AgentMemoryToolCandidateStatus.Rejected => "rejected",
            _ => null
        };
}
