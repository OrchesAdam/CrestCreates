using CrestCreates.Accountability.Abstractions.Composition;
using CrestCreates.Accountability.Abstractions.Recording;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Accountability.Options;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Bootstrap;
using CrestCreates.Metadata.Abstractions.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.Agent.Memory.Accountability.Bootstrap;

/// <summary>
/// Fails closed when a runtime that declares real Agent Memory Accountability
/// (AddAgentMemoryAccountability) is not backed by a complete write chain:
/// real producer, configured options, the Accountability runtime marker
/// (AddAccountability), and a registered recorder. The bridge intentionally
/// never registers sinks itself; a missing recorder means the host never
/// called AddAccountability.
/// </summary>
public sealed class AgentMemoryAccountabilityCompositionValidator : IBootstrapValidator, IHostedService
{
    private readonly AgentMemoryAccountabilityOptions? _options;
    private readonly IServiceProvider? _services;
    private readonly IAccountabilityRuntimeMarker? _marker;
    private readonly IAuditRecorder? _recorder;

    public AgentMemoryAccountabilityCompositionValidator(
        AgentMemoryAccountabilityOptions? options = null,
        IServiceProvider? services = null,
        IAccountabilityRuntimeMarker? marker = null,
        IAuditRecorder? recorder = null)
    {
        _options = options;
        _services = services;
        _marker = marker;
        _recorder = recorder;
    }

    public int Order => -100;

    public ValidationReport Validate()
        => ValidateCore(out var message)
            ? ValidationReport.Empty
            : ValidationReport.FromIssues(new ValidationIssue(SeverityLevel.Error, message)
            {
                Code = AgentMemoryAccountabilityDiagnosticCodes.CompositionInvalid
            });

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!ValidateCore(out var message))
            throw new InvalidOperationException(
                $"{AgentMemoryAccountabilityDiagnosticCodes.CompositionInvalid}: {message}");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private bool ValidateCore(out string message)
    {
        message = string.Empty;

        if (_options is null)
        {
            message = "Agent Memory Accountability options are not registered.";
            return false;
        }
        if (!_options.IsValidWriteTimeout)
        {
            message = "Agent Memory Accountability WriteTimeout must be finite and positive.";
            return false;
        }
        if (_marker is null)
        {
            message = "Accountability runtime marker is not registered (AddAccountability()).";
            return false;
        }
        if (_recorder is null)
        {
            message = "IAuditRecorder is not registered.";
            return false;
        }

        // Resolve the producer lazily: the bridge registers the real producer
        // even when the surrounding Accountability runtime is missing, so eagerly
        // injecting it would surface a raw DI resolution error instead of this
        // validator's fail-closed composition diagnostic.
        var producer = _services?.GetService<IAgentMemoryAccountabilityProducer>();
        if (producer is null)
        {
            message = "producer is not registered.";
            return false;
        }
        if (producer is INullAgentMemoryAccountabilityProducer)
        {
            message = "producer is still the null producer.";
            return false;
        }

        return true;
    }
}
