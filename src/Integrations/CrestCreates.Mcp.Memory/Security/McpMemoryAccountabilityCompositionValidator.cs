using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Bootstrap;
using CrestCreates.Metadata.Abstractions.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.Mcp.Memory.Security;

/// <summary>
/// Fail-closed composition gate for MCP Memory tooling that writes
/// Accountability facts. MCP Memory relies on the ambient audit scope
/// (IAuditOperationContextAccessor, registered by AddAccountability) to
/// derive CausationId / ParentAuditId from the Capability execution context.
/// When the accessor is missing, MCP handlers cannot attribute their facts to
/// a Capability execution, so composition must fail rather than silently emit
/// unattributed audit records.
/// </summary>
internal sealed class McpMemoryAccountabilityCompositionValidator : IBootstrapValidator, IHostedService
{
    private readonly IAuditOperationContextAccessor? _accessor;
    private readonly IServiceProvider? _services;

    public McpMemoryAccountabilityCompositionValidator(
        IAuditOperationContextAccessor? accessor = null,
        IServiceProvider? services = null)
    {
        _accessor = accessor;
        _services = services;
    }

    public int Order => 200;

    public ValidationReport Validate()
        => ValidateCore(out var message)
            ? ValidationReport.Empty
            : ValidationReport.FromIssues(new ValidationIssue(SeverityLevel.Error, message)
            {
                Code = McpMemoryAccountabilityDiagnosticCodes.AccessorMissing
            });

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!ValidateCore(out var message))
            throw new InvalidOperationException(
                $"{McpMemoryAccountabilityDiagnosticCodes.AccessorMissing}: {message}");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private bool ValidateCore(out string message)
    {
        message = string.Empty;

        // Resolve lazily: AddMcpMemoryTools() may run before AddAccountability(),
        // so eagerly injecting the accessor would surface a raw DI resolution
        // error instead of this validator's fail-closed composition diagnostic.
        var accessor = _services?.GetService<IAuditOperationContextAccessor>() ?? _accessor;
        if (accessor is null)
        {
            message = "IAuditOperationContextAccessor is not registered (AddAccountability()). " +
                      "MCP Memory facts cannot be attributed to a Capability execution without it.";
            return false;
        }

        return true;
    }
}

/// <summary>
/// Diagnostic codes emitted by MCP Memory accountability composition gates.
/// </summary>
internal static class McpMemoryAccountabilityDiagnosticCodes
{
    private const string AccessorMissingValue = "MCP_MEMORY_ACCOUNTABILITY_ACCESSOR_MISSING";
    public static DiagnosticCode AccessorMissing { get; } = new(AccessorMissingValue);
}
