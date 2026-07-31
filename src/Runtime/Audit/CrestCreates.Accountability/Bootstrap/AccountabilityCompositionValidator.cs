using CrestCreates.Accountability.Abstractions.Composition;
using CrestCreates.Accountability.Abstractions.Recording;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Accountability.Recording;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Bootstrap;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Accountability.Abstractions.Semantics;
using CrestCreates.Accountability.Abstractions.Validation;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.Accountability.Bootstrap;

public sealed class AccountabilityCompositionValidator : IBootstrapValidator, IHostedService
{
    private readonly IAccountabilityRuntimeMarker? _marker;
    private readonly IAuditRecorder? _recorder;
    private readonly AccountabilityOptions _options;
    private readonly IReadOnlyCollection<IAuditSink> _sinks;

    public AccountabilityCompositionValidator(
        AccountabilityOptions options,
        IEnumerable<IAuditSink> sinks,
        IAccountabilityRuntimeMarker? marker = null,
        IAuditRecorder? recorder = null)
    {
        _options = options;
        _sinks = sinks.ToArray();
        _marker = marker;
        _recorder = recorder;
    }

    public int Order => -200;

    public ValidationReport Validate()
    {
        if (_marker is null || _recorder is null)
            return Failure("ACCOUNTABILITY_FOUNDATION_MISSING", "Accountability runtime is not registered.");
        if (_options.WriteTimeout <= TimeSpan.Zero
            || _options.WriteTimeout == Timeout.InfiniteTimeSpan
            || _options.WriteTimeout.TotalMilliseconds > uint.MaxValue - 1d)
            return Failure("ACCOUNTABILITY_WRITE_TIMEOUT_INVALID", "Accountability WriteTimeout must be finite, positive, and supported by CancellationTokenSource.");
        if (_options.RequireAtLeastOneSink && _sinks.Count == 0)
            return Failure("ACCOUNTABILITY_SINK_REQUIRED", "Accountability requires at least one sink.");
        var duplicate = _sinks.GroupBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault(x => x.Count() > 1);
        var invalid = _sinks.FirstOrDefault(x => !AuditSemanticNames.IsStableKind(x.Id, AuditContractLimits.MaxIdentifierLength));
        if (invalid is not null)
            return Failure("ACCOUNTABILITY_INVALID_SINK_ID", $"Invalid Accountability sink id '{invalid.Id}'.");
        return duplicate is null
            ? ValidationReport.Empty
            : Failure("ACCOUNTABILITY_DUPLICATE_SINK_ID", $"Duplicate Accountability sink id '{duplicate.Key}'.");
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var report = Validate();
        if (report.HasErrors)
            throw new InvalidOperationException(string.Join("; ", report.Issues.Select(x => $"{x.Code}: {x.Message}")));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static ValidationReport Failure(string code, string message)
        => ValidationReport.FromIssues(new ValidationIssue(SeverityLevel.Error, message)
        {
            Code = new DiagnosticCode(code)
        });
}
