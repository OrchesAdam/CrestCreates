namespace CrestCreates.Accountability.Abstractions.Retention;

/// <summary>
/// Trusted in-process retention endpoint. Application admission constructs every request from the
/// trusted identity, tenant and policy sources; this interface is not a network authorization
/// barrier and must not be exposed directly.
/// </summary>
public interface IAuditRetentionService
{
    AuditRetentionStoreIdentity GetStoreIdentity(string storeId);

    ValueTask<AuditRetentionPreview> PreviewAsync(
        AuditRetentionPreviewRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<AuditRetentionRunResult> ExecuteAsync(
        AuditRetentionRunRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<AuditRetentionRunRecord?> GetRunAsync(
        AuditRetentionRunKey key,
        CancellationToken cancellationToken = default);

    ValueTask<AuditHoldResult> PlaceHoldAsync(
        AuditHoldPlacement placement,
        CancellationToken cancellationToken = default);

    ValueTask<AuditHoldResult> ReleaseHoldAsync(
        AuditHoldRelease release,
        CancellationToken cancellationToken = default);
}
