namespace CrestCreates.Agent.Tools;

internal sealed class AgentToolOutputPreflightReceiptSink : IAgentToolOutputPreflightReceiptSink
{
    private readonly AgentToolPreparedOutcomeContract? _contract;
    private readonly AgentToolAuditProjectionContract? _auditContract;
    private readonly IAgentToolInvocationFactPreflightState _factState;
    private IReadOnlyList<AgentToolPreparedOutcomeReceipt>? _outcomes;

    public AgentToolOutputPreflightReceiptSink(
        AgentToolPreparedOutcomeContract? contract,
        AgentToolAuditProjectionContract? auditContract,
        IAgentToolInvocationFactPreflightState factState)
    {
        _contract = contract;
        _auditContract = auditContract;
        _factState = factState;
    }

    public bool HasPublishedOutcomes => _outcomes is not null;

    public void PublishAllowedOutcomes(IReadOnlyList<AgentToolPreparedOutcomeReceipt> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        if (_outcomes is not null)
            throw new InvalidOperationException("Output preflight receipts may only be published once.");
        if (outcomes.Count == 0)
            throw new ArgumentException("At least one allowed outcome is required.", nameof(outcomes));
        if (outcomes.Count > (_contract?.MaximumBranches ?? 5))
            throw new ArgumentException("Output preflight outcome set exceeds its generated contract.", nameof(outcomes));
        if (outcomes.Select(item => item.OutcomeCode).Distinct(StringComparer.Ordinal).Count() != outcomes.Count)
            throw new ArgumentException("Output preflight outcome codes must be unique.", nameof(outcomes));
        if (_contract is not null && outcomes.Any(item => !_contract.AllowedOutcomeCodes.Contains(item.OutcomeCode)))
            throw new ArgumentException("Output preflight outcome code is not allowed by the generated Tool contract.", nameof(outcomes));
        if (outcomes.Select(item => $"{item.OutcomeCode}|{item.Receipt.ToolDescriptorId}|{item.Receipt.ToolDescriptorVersion}|{item.Receipt.OutputContractFingerprint}|{item.Receipt.StructuredOutputHash}").Distinct(StringComparer.Ordinal).Count() != outcomes.Count)
            throw new ArgumentException("Output preflight receipt identities must be unique.", nameof(outcomes));
        if (outcomes.Any(item => item.InternalFacts.Count > 32
            || item.InternalFacts.Any(fact => fact is null || string.IsNullOrWhiteSpace(fact.Code)
                || fact.Code.Length > 96 || fact.Value?.Length > 256)))
            throw new ArgumentException("Output preflight branch facts exceed the safe shape.", nameof(outcomes));
        var factSnapshot = _factState.Capture();
        var effectiveMaximumFacts = Math.Min(64, Math.Min(factSnapshot.MaximumFacts, _auditContract?.MaximumFacts ?? 64));
        if (outcomes.Any(item => !AgentToolAuditFactValidator.Validate(
                factSnapshot.Facts.Concat(item.InternalFacts).Concat(item.ProjectedOutputFacts).ToArray(),
                effectiveMaximumFacts,
                _auditContract)))
            throw new ArgumentException("Output preflight facts violate the frozen audit contract or effective limit.", nameof(outcomes));
        _outcomes = outcomes.Select(item => item with
        {
            Receipt = item.Receipt with { },
            InternalFacts = item.InternalFacts?.Select(fact => fact with { }).ToArray() ?? Array.Empty<AgentToolAuditFact>(),
            ProjectedOutputFacts = item.ProjectedOutputFacts?.Select(fact => fact with { }).ToArray() ?? Array.Empty<AgentToolAuditFact>()
        }).ToArray();
    }

    public IReadOnlyList<AgentToolPreparedOutcomeReceipt> Seal()
        => _outcomes ?? Array.Empty<AgentToolPreparedOutcomeReceipt>();
}
