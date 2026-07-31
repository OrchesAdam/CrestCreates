using CrestCreates.Accountability.Abstractions.Composition;
using CrestCreates.Accountability.Abstractions.Recording;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Bootstrap;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Core.Abstractions.Identity;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.Capability.Bootstrap;

public sealed class CapabilityAccountabilityCompositionValidator : IBootstrapValidator, IHostedService
{
    private readonly IAccountabilityRuntimeMarker? _marker;
    private readonly IAuditRecorder? _recorder;

    public CapabilityAccountabilityCompositionValidator(IAccountabilityRuntimeMarker? marker = null, IAuditRecorder? recorder = null)
    {
        _marker = marker;
        _recorder = recorder;
    }

    public int Order => -100;

    public ValidationReport Validate()
        => _marker is not null && _recorder is not null
            ? ValidationReport.Empty
            : ValidationReport.FromIssues(new ValidationIssue(SeverityLevel.Error, "Capability Accountability Foundation is not registered.")
            {
                Code = new DiagnosticCode("CAPABILITY_ACCOUNTABILITY_FOUNDATION_MISSING")
            });

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_marker is null || _recorder is null)
            throw new InvalidOperationException("CAPABILITY_ACCOUNTABILITY_FOUNDATION_MISSING: Capability Accountability Foundation is not registered.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
