namespace CrestCreates.Agent.Tools;

/// <summary>
/// Single source of truth for constructing the terminal invocation outcomes
/// used across the pre-dispatch protocol (invoker, coordinator, finalizer).
/// Keeps the governance and contract failure wording in one place so every
/// participant produces byte-identical outcomes for the same situation.
/// </summary>
internal static class AgentToolInvocationOutcomeFactory
{
    internal static AgentToolInvocationOutcome Outcome(
        AgentToolInvocationOutcomeKind kind,
        string code,
        string message)
        => AgentToolResultMapper.Outcome(kind, code, message);

    internal static AgentToolInvocationOutcome GovernanceDenied(string code)
        => Outcome(
            AgentToolInvocationOutcomeKind.GovernanceDenied,
            code,
            "The tool invocation was blocked by governance policy.");

    internal static AgentToolInvocationOutcome ContractFailure(string code)
        => Outcome(
            AgentToolInvocationOutcomeKind.InternalContractFailure,
            code,
            "The tool produced an invalid server result.");

    internal static AgentToolInvocationOutcome Indeterminate(string reasonCode)
        => Outcome(
            AgentToolInvocationOutcomeKind.InvocationIndeterminate,
            "AGENT_TOOL_INVOCATION_INDETERMINATE",
            "The invocation result is uncertain and must not be retried automatically.");
}
