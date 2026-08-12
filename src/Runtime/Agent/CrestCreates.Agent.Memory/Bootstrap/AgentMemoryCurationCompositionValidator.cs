using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Bootstrap;
using CrestCreates.Metadata.Abstractions.Registry;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.Agent.Memory.Bootstrap;

/// <summary>
/// Fails closed when a runtime that declares formal curation (AddAgentMemoryCuration)
/// is not backed by a store that performs conditional, atomic transitions. A
/// read-only runtime that never registers the curation marker is valid by definition.
/// </summary>
public sealed class AgentMemoryCurationCompositionValidator : IBootstrapValidator, IHostedService
{
    private readonly IAgentMemoryFormalCurationMarker? _marker;
    private readonly IAgentMemoryStore? _store;

    public AgentMemoryCurationCompositionValidator(
        IAgentMemoryFormalCurationMarker? marker = null,
        IAgentMemoryStore? store = null)
    {
        _marker = marker;
        _store = store;
    }

    public int Order => -100;

    public ValidationReport Validate()
        => ValidateCore(out var message)
            ? ValidationReport.Empty
            : ValidationReport.FromIssues(new ValidationIssue(SeverityLevel.Error, message)
            {
                Code = AgentMemoryDiagnosticCodes.CurationCompositionInvalid
            });

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!ValidateCore(out var message))
            throw new InvalidOperationException($"{AgentMemoryDiagnosticCodes.CurationCompositionInvalid}: {message}");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private bool ValidateCore(out string message)
    {
        message = string.Empty;
        if (_marker is null)
            return true;

        if (_store is null)
        {
            message = "Agent Memory store is not registered.";
            return false;
        }

        if (_store is not IAgentMemoryConditionalCurationStore)
        {
            message = "Agent Memory store does not support conditional curation transitions (IAgentMemoryConditionalCurationStore).";
            return false;
        }

        if (_store is not IAgentMemoryStoreCapabilities capabilities
            || capabilities.CurationOutcomeGuarantee != AgentMemoryCurationOutcomeGuarantee.ConfirmedAtomic)
        {
            message = "Agent Memory store does not guarantee ConfirmedAtomic curation outcomes.";
            return false;
        }

        return true;
    }
}
