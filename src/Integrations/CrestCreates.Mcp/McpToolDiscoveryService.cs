using Microsoft.Extensions.Logging;

namespace CrestCreates.Mcp;

public sealed class McpToolDiscoveryService : IMcpToolDiscoveryService
{
    private readonly McpToolRuntimeSnapshotProvider _snapshotProvider;
    private readonly IMcpToolExposurePolicy _exposurePolicy;
    private readonly ILogger<McpToolDiscoveryService>? _logger;

    public McpToolDiscoveryService(
        McpToolRuntimeSnapshotProvider snapshotProvider,
        IMcpToolExposurePolicy exposurePolicy,
        ILogger<McpToolDiscoveryService>? logger = null)
    {
        _snapshotProvider = snapshotProvider;
        _exposurePolicy = exposurePolicy;
        _logger = logger;
    }

    public async ValueTask<IReadOnlyList<McpToolContract>> ListAsync(
        McpToolDiscoveryContext context,
        CancellationToken cancellationToken = default)
    {
        ValidateHost(context.Host);
        var contracts = new List<McpToolContract>();
        foreach (var entry in _snapshotProvider.GetRequired().Entries.Values.OrderBy(
                     entry => entry.Descriptor.ToolName,
                     StringComparer.Ordinal))
        {
            try
            {
                var decision = await _exposurePolicy.EvaluateAsync(
                    new McpToolExposureContext(
                        context.Host,
                        entry.Descriptor,
                        entry.Capability,
                        McpToolExposurePhase.Discovery),
                    cancellationToken).ConfigureAwait(false);
                if (decision.IsAllowed)
                    contracts.Add(entry.DiscoveryContract);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger?.LogError(
                    exception,
                    "MCP_TOOL_EXPOSURE_POLICY_FAILURE for tool {ToolName} on host {HostId}.",
                    entry.Descriptor.ToolName,
                    context.Host.HostId);
                throw new McpToolContractViolationException(
                    "MCP_TOOL_EXPOSURE_POLICY_FAILURE",
                    "The server could not evaluate tool exposure.",
                    exception);
            }
        }

        return contracts;
    }

    internal static void ValidateHost(McpToolHostContext host)
    {
        if (host is null)
            throw new McpInvalidRequestException("MCP_INVALID_HOST_CONTEXT", "MCP host context is required.");
        if (string.IsNullOrWhiteSpace(host.HostId) || string.IsNullOrWhiteSpace(host.EnvironmentName))
            throw new McpInvalidRequestException("MCP_INVALID_HOST_CONTEXT", "MCP host context is invalid.");
    }
}
