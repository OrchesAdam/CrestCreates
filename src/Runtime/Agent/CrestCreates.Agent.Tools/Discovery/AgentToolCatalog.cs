using CrestCreates.Agent.Abstractions;

namespace CrestCreates.Agent.Tools;

public sealed class AgentToolCatalog : IAgentToolCatalog
{
    private readonly AgentToolRuntimeSnapshotProvider _snapshots;
    private readonly IAgentExecutionContextAccessor _execution;

    public AgentToolCatalog(
        AgentToolRuntimeSnapshotProvider snapshots,
        IAgentExecutionContextAccessor execution)
    {
        _snapshots = snapshots;
        _execution = execution;
    }

    public ValueTask<IReadOnlyList<AgentToolDiscoveryContract>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = _execution.Current;
        if (!IsValid(context))
            throw new InvalidOperationException("A valid trusted Agent execution context is required.");

        var contracts = _snapshots.GetRequired().Entries.Values
            .Where(entry => IsVisible(entry, context!))
            .OrderBy(entry => entry.Descriptor.ToolName, StringComparer.Ordinal)
            .Select(entry => entry.DiscoveryContract)
            .ToArray();
        return ValueTask.FromResult<IReadOnlyList<AgentToolDiscoveryContract>>(contracts);
    }

    internal static bool IsVisible(AgentToolRuntimeEntry entry, AgentExecutionContext context)
        => entry.AllowedAgentRoles.Overlaps(context.AgentRoles)
            && (context.CallOrigin == AgentToolCallOrigin.ExplicitRequest
                || entry.Governance.SelectionPolicy
                    == CrestCreates.Metadata.AgentTool.AgentToolSelectionPolicy.AutomaticAllowed);

    internal static bool IsValid(AgentExecutionContext? context)
        => context is not null
            && !string.IsNullOrWhiteSpace(context.ExecutionId)
            && !string.IsNullOrWhiteSpace(context.InvocationId)
            && !string.IsNullOrWhiteSpace(context.AgentId)
            && context.AgentRoles is { Count: > 0 }
            && context.AgentRoles.All(role => !string.IsNullOrWhiteSpace(role))
            && context.AgentRoles.Distinct(StringComparer.Ordinal).Count() == context.AgentRoles.Count
            && context.CallOrigin is AgentToolCallOrigin.ExplicitRequest
                or AgentToolCallOrigin.AutomaticSelection;
}
