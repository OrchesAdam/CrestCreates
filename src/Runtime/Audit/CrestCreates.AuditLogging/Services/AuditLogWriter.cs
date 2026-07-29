using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Identity;
using CrestCreates.Accountability.Abstractions.Recording;
using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.AuditLogging.Context;
using Microsoft.Extensions.Logging;

namespace CrestCreates.AuditLogging.Services
{
    /// <summary>
    /// 统一审计日志写入器实现
    /// </summary>
    public class AuditLogWriter : IAuditLogWriter
    {
        private readonly IAuditRecorder _recorder;
        private readonly IAuditIdentityGenerator _identity;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<AuditLogWriter> _logger;

        public AuditLogWriter(
            IAuditRecorder recorder,
            IAuditIdentityGenerator identity,
            ILogger<AuditLogWriter> logger,
            TimeProvider? timeProvider = null)
        {
            _recorder = recorder;
            _identity = identity;
            _logger = logger;
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        public async Task WriteAsync(AuditContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            var occurredAt = _timeProvider.GetUtcNow();
            var method = string.IsNullOrWhiteSpace(context.HttpMethod)
                ? "UNKNOWN"
                : context.HttpMethod.ToUpperInvariant();
            var endpointIdentity = $"{method} <legacy-unmatched>";
            var status = context.IsException || context.HttpStatusCode >= 400
                ? "failed"
                : "succeeded";
            var code = context.IsException
                ? "UNHANDLED_EXCEPTION"
                : context.HttpStatusCode >= 400
                    ? $"HTTP_{context.HttpStatusCode}"
                    : null;
            var actor = string.IsNullOrWhiteSpace(context.UserId)
                ? new AuditActor { Kind = "unknown", Id = "unknown" }
                : new AuditActor { Kind = "user", Id = context.UserId };
            var startedAt = new DateTimeOffset(context.StartTime.ToUniversalTime());
            var duration = occurredAt - startedAt;
            if (duration < TimeSpan.Zero)
                duration = TimeSpan.Zero;

            var envelope = new AuditEnvelope
            {
                AuditId = _identity.CreateAuditId(),
                OccurredAt = occurredAt,
                TenantId = context.TenantId,
                CorrelationId = _identity.CreateOperationId(),
                Actor = actor,
                Action = new AuditAction { Kind = "http.request", Name = endpointIdentity },
                Target = new AuditTarget { Kind = "http.endpoint", Id = endpointIdentity },
                Outcome = new AuditOutcome { Status = status, Code = code },
                Runtime = new AuditRuntimeContext
                {
                    InvocationSource = "http",
                    ExecutionId = _identity.CreateOperationId(),
                    RequestId = context.TraceId,
                    Duration = duration,
                    References = []
                },
                Descriptors = AuditDescriptorContext.Empty,
                Evidence = [],
                Tags = AuditTagMap.Empty
            };

            try
            {
                var result = await _recorder.RecordAsync(envelope, CancellationToken.None);
                _logger.LogDebug(
                    "Legacy audit observation exported to Accountability: {Endpoint} -> {Status}",
                    endpointIdentity,
                    result.Status);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to export legacy audit observation for {Endpoint}", endpointIdentity);
            }
        }
    }
}
