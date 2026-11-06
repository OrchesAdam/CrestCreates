using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Retention;
using CrestCreates.Accountability.Abstractions.Semantics;
using CrestCreates.Application.Contracts.AuditRetention;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Settings;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.Application.AuditLog;

/// <summary>
/// The single authorization and policy admission for audit retention. HTTP projections, jobs and
/// direct DI callers all pass through this service; the runtime retention service itself is not a
/// network authorization barrier.
/// </summary>
public sealed class AuditRetentionAppService : IAuditRetentionAppService
{
    private readonly IAuditRetentionService _retention;
    private readonly ISettingProvider _settings;
    private readonly IPermissionChecker _permissions;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;
    private readonly ITenantProvider _tenants;
    private readonly TimeProvider _timeProvider;

    public AuditRetentionAppService(
        IAuditRetentionService retention,
        ISettingProvider settings,
        IPermissionChecker permissions,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        ITenantProvider tenants,
        TimeProvider timeProvider)
    {
        _retention = retention ?? throw new ArgumentNullException(nameof(retention));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _currentTenant = currentTenant ?? throw new ArgumentNullException(nameof(currentTenant));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<AuditRetentionPreviewDto> PreviewAsync(
        AuditRetentionPreviewInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        BuildActor();
        await _permissions.CheckAsync(AuditRetentionPermissions.Preview).ConfigureAwait(false);
        var scope = await ResolveScopeAsync(input.TenantId, cancellationToken).ConfigureAwait(false);
        var policy = await ResolvePolicyAsync(requireEnabled: false, cancellationToken).ConfigureAwait(false);
        var identity = _retention.GetStoreIdentity(RequireText(input.StoreId, nameof(input.StoreId)));
        var snapshotAt = _timeProvider.GetUtcNow();
        var preview = await _retention.PreviewAsync(
            new AuditRetentionPreviewRequest
            {
                StoreId = identity.StoreId,
                SinkId = identity.SinkId,
                Scope = scope,
                Policy = policy,
                SnapshotAt = snapshotAt,
                Cutoff = policy.ComputeCutoff(snapshotAt)
            },
            cancellationToken).ConfigureAwait(false);
        return Map(preview);
    }

    public async Task<AuditRetentionRunDto> ExecuteAsync(
        AuditRetentionExecuteInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var actor = BuildActor();
        await _permissions.CheckAsync(AuditRetentionPermissions.Execute).ConfigureAwait(false);
        var scope = await ResolveScopeAsync(input.TenantId, cancellationToken).ConfigureAwait(false);
        var policy = await ResolvePolicyAsync(requireEnabled: true, cancellationToken).ConfigureAwait(false);
        var identity = _retention.GetStoreIdentity(RequireText(input.StoreId, nameof(input.StoreId)));
        var snapshotAt = _timeProvider.GetUtcNow();
        var result = await _retention.ExecuteAsync(
            new AuditRetentionRunRequest
            {
                StoreId = identity.StoreId,
                SinkId = identity.SinkId,
                Scope = scope,
                RequestId = RequireIdentifier(input.RequestId, nameof(input.RequestId)),
                ReasonCode = RequireReasonCode(input.ReasonCode),
                Policy = policy,
                SnapshotAt = snapshotAt,
                Cutoff = policy.ComputeCutoff(snapshotAt),
                Actor = actor,
                CorrelationId = CurrentCorrelationId(),
                ExpectedPolicyHash = string.IsNullOrWhiteSpace(input.ExpectedPolicyHash)
                    ? null
                    : input.ExpectedPolicyHash.Trim()
            },
            cancellationToken).ConfigureAwait(false);
        return Map(result);
    }

    public async Task<AuditRetentionRunDto?> GetRunAsync(
        string storeId,
        string requestId,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        BuildActor();
        await _permissions.CheckAsync(AuditRetentionPermissions.ReadRuns).ConfigureAwait(false);
        var scope = await ResolveScopeAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var identity = _retention.GetStoreIdentity(RequireText(storeId, nameof(storeId)));
        var record = await _retention.GetRunAsync(
            new AuditRetentionRunKey
            {
                StoreId = identity.StoreId,
                SinkId = identity.SinkId,
                Scope = scope,
                RequestId = RequireIdentifier(requestId, nameof(requestId))
            },
            cancellationToken).ConfigureAwait(false);
        return record is null ? null : Map(record);
    }

    public async Task<AuditRetentionMirrorDto> ExportBatchAsync(
        AuditRetentionExportInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        BuildActor();
        await _permissions.CheckAsync(AuditRetentionPermissions.Export).ConfigureAwait(false);
        var scope = await ResolveScopeAsync(input.TenantId, cancellationToken).ConfigureAwait(false);
        var storeId = RequireText(input.StoreId, nameof(input.StoreId));
        var requestId = RequireIdentifier(input.RequestId, nameof(input.RequestId));
        if (input.BatchOrdinal <= 0)
        {
            throw new AuditRetentionException(
                AuditRetentionErrorCodes.InvalidRequest,
                "BatchOrdinal must be positive.");
        }

        var identity = _retention.GetStoreIdentity(storeId);
        var outcome = await _retention.ExportBatchAsync(
            new AuditRetentionRunKey
            {
                StoreId = identity.StoreId,
                SinkId = identity.SinkId,
                Scope = scope,
                RequestId = requestId
            },
            input.BatchOrdinal,
            cancellationToken).ConfigureAwait(false);
        return new AuditRetentionMirrorDto { Status = outcome.Status, Code = outcome.Code };
    }

    public async Task<AuditHoldDto> PlaceHoldAsync(
        AuditRetentionHoldInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var actor = BuildActor();
        await _permissions.CheckAsync(AuditRetentionPermissions.ManageHolds).ConfigureAwait(false);
        var scope = await ResolveScopeAsync(input.TenantId, cancellationToken).ConfigureAwait(false);
        var identity = _retention.GetStoreIdentity(RequireText(input.StoreId, nameof(input.StoreId)));
        var result = await _retention.PlaceHoldAsync(
            new AuditHoldPlacement
            {
                StoreId = identity.StoreId,
                SinkId = identity.SinkId,
                Scope = scope,
                AuditId = RequireIdentifier(input.AuditId, nameof(input.AuditId)),
                ReasonCode = RequireReasonCode(input.ReasonCode),
                Actor = actor,
                CorrelationId = CurrentCorrelationId()
            },
            cancellationToken).ConfigureAwait(false);
        return Map(result);
    }

    public async Task<AuditHoldDto> ReleaseHoldAsync(
        AuditRetentionReleaseInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var actor = BuildActor();
        await _permissions.CheckAsync(AuditRetentionPermissions.ManageHolds).ConfigureAwait(false);
        var scope = await ResolveScopeAsync(input.TenantId, cancellationToken).ConfigureAwait(false);
        var identity = _retention.GetStoreIdentity(RequireText(input.StoreId, nameof(input.StoreId)));
        var result = await _retention.ReleaseHoldAsync(
            new AuditHoldRelease
            {
                StoreId = identity.StoreId,
                SinkId = identity.SinkId,
                Scope = scope,
                HoldId = RequireIdentifier(input.HoldId, nameof(input.HoldId)),
                ExpectedVersion = input.ExpectedVersion,
                ReasonCode = RequireReasonCode(input.ReasonCode),
                Actor = actor,
                CorrelationId = CurrentCorrelationId()
            },
            cancellationToken).ConfigureAwait(false);
        return Map(result);
    }

    private AuditActor BuildActor()
    {
        if (!_currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(_currentUser.Id))
        {
            throw new AuditRetentionException(
                AuditRetentionErrorCodes.IdentityRequired,
                "Audit retention requires an authenticated identity.");
        }

        return new AuditActor
        {
            Kind = AuditActorKinds.User,
            Id = _currentUser.Id,
            DisplayName = string.IsNullOrWhiteSpace(_currentUser.UserName) ? null : _currentUser.UserName
        };
    }

    private async Task<AuditRetentionScope> ResolveScopeAsync(string? requestedTenantId, CancellationToken cancellationToken)
    {
        var currentTenantId = _currentTenant.Id;
        if (!string.IsNullOrWhiteSpace(currentTenantId))
        {
            if (!string.IsNullOrWhiteSpace(requestedTenantId)
                && !string.Equals(requestedTenantId.Trim(), currentTenantId, StringComparison.Ordinal))
            {
                throw new AuditRetentionException(
                    AuditRetentionErrorCodes.ScopeInvalid,
                    "A tenant caller may only target its own authoritative tenant id.");
            }

            return AuditRetentionScope.ForTenant(currentTenantId);
        }

        if (string.IsNullOrWhiteSpace(requestedTenantId))
            return AuditRetentionScope.Host;

        var resolved = await _tenants.GetTenantAsync(requestedTenantId.Trim(), cancellationToken).ConfigureAwait(false);
        if (resolved is null || string.IsNullOrWhiteSpace(resolved.Id))
        {
            throw new AuditRetentionException(
                AuditRetentionErrorCodes.ScopeInvalid,
                $"Tenant '{requestedTenantId}' could not be resolved to an active authoritative tenant id.");
        }

        return AuditRetentionScope.ForTenant(resolved.Id);
    }

    private async Task<AuditRetentionPolicy> ResolvePolicyAsync(bool requireEnabled, CancellationToken cancellationToken)
    {
        var enabled = await ResolveBooleanAsync(AuditRetentionSettingNames.Enabled, cancellationToken).ConfigureAwait(false);
        if (requireEnabled && !enabled)
        {
            throw new AuditRetentionException(
                AuditRetentionErrorCodes.Disabled,
                "Audit retention execution is disabled by the Global 'AuditLogging.Retention.Enabled' setting.");
        }

        var policy = new AuditRetentionPolicy
        {
            RetentionDays = await ResolveIntegerAsync(AuditRetentionSettingNames.RetentionDays, cancellationToken).ConfigureAwait(false),
            BatchSize = await ResolveIntegerAsync(AuditRetentionSettingNames.BatchSize, cancellationToken).ConfigureAwait(false),
            MaxBatchesPerRun = await ResolveIntegerAsync(AuditRetentionSettingNames.MaxBatchesPerRun, cancellationToken).ConfigureAwait(false),
            MaxRunSeconds = await ResolveIntegerAsync(AuditRetentionSettingNames.MaxRunSeconds, cancellationToken).ConfigureAwait(false)
        };

        try
        {
            policy.EnsureValid();
        }
        catch (ArgumentException exception)
        {
            throw new AuditRetentionException(
                AuditRetentionErrorCodes.PolicyInvalid,
                $"Audit retention policy settings are invalid: {exception.Message}",
                exception);
        }

        return policy;
    }

    private async Task<bool> ResolveBooleanAsync(string name, CancellationToken cancellationToken)
    {
        var raw = await _settings.GetOrNullAsync(name, cancellationToken).ConfigureAwait(false);
        if (bool.TryParse(raw, out var value))
            return value;
        throw new AuditRetentionException(
            AuditRetentionErrorCodes.PolicyInvalid,
            $"Audit retention setting '{name}' is missing or not a boolean.");
    }

    private async Task<int> ResolveIntegerAsync(string name, CancellationToken cancellationToken)
    {
        var raw = await _settings.GetOrNullAsync(name, cancellationToken).ConfigureAwait(false);
        if (int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value))
            return value;
        throw new AuditRetentionException(
            AuditRetentionErrorCodes.PolicyInvalid,
            $"Audit retention setting '{name}' is missing or not a valid 32-bit integer.");
    }

    private static string RequireText(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new AuditRetentionException(AuditRetentionErrorCodes.InvalidRequest, $"{name} is required.");
        return value.Trim();
    }

    private static string RequireIdentifier(string? value, string name)
    {
        var text = RequireText(value, name);
        if (text.Length > AuditRetentionContract.MaxIdentifierLength)
            throw new AuditRetentionException(AuditRetentionErrorCodes.InvalidRequest, $"{name} is too long.");
        return text;
    }

    private static string RequireReasonCode(string? value)
    {
        var reason = RequireText(value, "ReasonCode");
        if (!AuditSemanticNames.IsStableKind(reason, 128))
        {
            throw new AuditRetentionException(
                AuditRetentionErrorCodes.InvalidRequest,
                "ReasonCode must be a stable lowercase kind such as 'retention.scheduled'.");
        }

        return reason;
    }

    private static string CurrentCorrelationId()
    {
        var traceId = Activity.Current?.TraceId.ToString();
        return string.IsNullOrWhiteSpace(traceId) ? Guid.NewGuid().ToString("N") : traceId;
    }

    private static AuditRetentionPreviewDto Map(AuditRetentionPreview preview)
        => new()
        {
            StoreId = preview.StoreId,
            SinkId = preview.SinkId,
            ScopeKind = preview.Scope.Kind,
            TenantId = preview.Scope.TenantId,
            SnapshotAt = preview.SnapshotAt,
            Cutoff = preview.Cutoff,
            PolicyHash = preview.PolicyHash,
            BatchSize = preview.BatchSize,
            MaxBatchesPerRun = preview.MaxBatchesPerRun,
            HasMore = preview.HasMore,
            EligibleInSample = preview.EligibleInSample,
            HeldInSample = preview.HeldInSample,
            ProtectedInSample = preview.ProtectedInSample,
            QuarantinedInSample = preview.QuarantinedInSample,
            Sample = preview.Sample.Select(candidate => new AuditRetentionPreviewCandidateDto
            {
                AuditId = candidate.AuditId,
                OccurredAt = candidate.OccurredAt,
                FirstAcceptedAt = candidate.FirstAcceptedAt,
                Disposition = candidate.Disposition switch
                {
                    AuditRetentionCandidateDisposition.Eligible => "eligible",
                    AuditRetentionCandidateDisposition.ActiveHold => "active-hold",
                    AuditRetentionCandidateDisposition.Protected => "protected",
                    AuditRetentionCandidateDisposition.Quarantined => "quarantined",
                    _ => "unknown"
                }
            }).ToList()
        };

    private static AuditRetentionRunDto Map(AuditRetentionRunResult result)
        => new()
        {
            StoreId = result.Key.StoreId,
            SinkId = result.Key.SinkId,
            ScopeKind = result.Key.Scope.Kind,
            TenantId = result.Key.Scope.TenantId,
            RequestId = result.Key.RequestId,
            State = ToWire(result.State),
            CommitKnowledge = ToWire(result.CommitKnowledge),
            CommittedDeletedCount = result.CommittedDeletedCount,
            CommittedBatchCount = result.CommittedBatchCount,
            RemainingMayExist = result.RemainingMayExist,
            StopReason = result.StopReason,
            MirrorStatus = result.MirrorOutcome?.Status,
            MirrorCode = result.MirrorOutcome?.Code
        };

    private static AuditRetentionRunDto Map(AuditRetentionRunRecord record)
        => new()
        {
            StoreId = record.Key.StoreId,
            SinkId = record.Key.SinkId,
            ScopeKind = record.Key.Scope.Kind,
            TenantId = record.Key.Scope.TenantId,
            RequestId = record.Key.RequestId,
            State = ToWire(record.State),
            CommitKnowledge = ToWire(AuditRetentionCommitKnowledge.Committed),
            CommittedDeletedCount = record.CommittedDeletedCount,
            CommittedBatchCount = record.CommittedBatchCount,
            RemainingMayExist = record.State is AuditRetentionRunState.BudgetReached
                or AuditRetentionRunState.Cancelled
                or AuditRetentionRunState.Failed
                or AuditRetentionRunState.Unknown,
            StopReason = record.StopReason
        };

    private static AuditHoldDto Map(AuditHoldResult result)
        => new()
        {
            HoldId = result.Hold.HoldId,
            StoreId = result.Hold.StoreId,
            SinkId = result.Hold.SinkId,
            AuditId = result.Hold.AuditId,
            ScopeKind = result.Hold.Scope.Kind,
            TenantId = result.Hold.Scope.TenantId,
            Version = result.Hold.Version,
            ReasonCode = result.Hold.ReasonCode,
            CreatedAt = result.Hold.CreatedAt,
            ReleasedAt = result.Hold.ReleasedAt,
            MirrorStatus = result.MirrorOutcome?.Status,
            MirrorCode = result.MirrorOutcome?.Code
        };

    private static string ToWire(AuditRetentionRunState state)
        => state switch
        {
            AuditRetentionRunState.Running => "running",
            AuditRetentionRunState.Completed => "completed",
            AuditRetentionRunState.BudgetReached => "budget-reached",
            AuditRetentionRunState.Cancelled => "cancelled",
            AuditRetentionRunState.Failed => "failed",
            AuditRetentionRunState.Unknown => "unknown",
            _ => "unknown"
        };

    private static string ToWire(AuditRetentionCommitKnowledge knowledge)
        => knowledge switch
        {
            AuditRetentionCommitKnowledge.None => "none",
            AuditRetentionCommitKnowledge.Committed => "committed",
            AuditRetentionCommitKnowledge.Unknown => "unknown",
            _ => "unknown"
        };
}
