using System.Diagnostics;
using CrestCreates.Capability.Abstractions;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Capability.Middleware;

internal sealed class AuditMiddleware : ICapabilityPipelineMiddleware
{
    private readonly ICapabilityAuditStore _auditStore;
    private readonly ILogger<AuditMiddleware> _logger;

    public AuditMiddleware(ICapabilityAuditStore auditStore, ILogger<AuditMiddleware> logger)
    {
        _auditStore = auditStore;
        _logger = logger;
    }

    public async Task<CapabilityExecutionResult> InvokeAsync(
        CapabilityExecutionContext context,
        CapabilityPipelineDelegate next)
    {
        var executionId = Guid.NewGuid().ToString("N");
        var sw = Stopwatch.StartNew();
        CapabilityExecutionResult? result = null;
        Exception? unhandledException = null;
        CapabilityFailureException? capabilityFailure = null;
        bool cancelled = false;

        try
        {
            result = await next(context);
            return result;
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            throw;
        }
        catch (CapabilityFailureException ex)
        {
            capabilityFailure = ex;
            throw;
        }
        catch (Exception ex)
        {
            unhandledException = ex;
            throw;
        }
        finally
        {
            sw.Stop();

            try
            {
                var errorCode = cancelled
                    ? "CANCELLED"
                    : result?.ErrorCode
                      ?? capabilityFailure?.ErrorCode
                      ?? (unhandledException is not null ? "UNHANDLED_EXCEPTION" : null);

                await _auditStore.RecordAsync(new CapabilityExecutionRecord
                {
                    ExecutionId = executionId,
                    CapabilityId = context.CapabilityId,
                    CapabilityName = context.CapabilityName,
                    CapabilityVersion = context.CapabilityVersion,
                    TenantId = context.TenantId,
                    UserId = context.UserId,
                    CorrelationId = context.CorrelationId,
                    Source = context.InvocationSource,
                    IsSuccess = result?.IsSuccess ?? false,
                    ErrorCode = errorCode,
                    Duration = sw.Elapsed,
                    Timestamp = DateTimeOffset.UtcNow
                });
            }
            catch (Exception auditEx)
            {
                _logger.LogError(auditEx, "Failed to record audit for capability '{CapabilityId}'", context.CapabilityId);
            }
        }
    }
}
