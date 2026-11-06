using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.AuditRetention;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.Application.Contracts.Interfaces;

public interface IAuditRetentionAppService
{
    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "preview",
        Kind = HttpProjectionOperationKind.Query,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { AuditRetentionPermissions.Preview })]
    Task<AuditRetentionPreviewDto> PreviewAsync(
        AuditRetentionPreviewInput input,
        CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "execute",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { AuditRetentionPermissions.Execute })]
    Task<AuditRetentionRunDto> ExecuteAsync(
        AuditRetentionExecuteInput input,
        CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "run",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { AuditRetentionPermissions.ReadRuns })]
    Task<AuditRetentionRunDto?> GetRunAsync(
        string storeId,
        string requestId,
        string? tenantId = null,
        CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "batches/export",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { AuditRetentionPermissions.Export })]
    Task<AuditRetentionMirrorDto> ExportBatchAsync(
        AuditRetentionExportInput input,
        CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "holds",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { AuditRetentionPermissions.ManageHolds })]
    Task<AuditHoldDto> PlaceHoldAsync(
        AuditRetentionHoldInput input,
        CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "holds/release",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { AuditRetentionPermissions.ManageHolds })]
    Task<AuditHoldDto> ReleaseHoldAsync(
        AuditRetentionReleaseInput input,
        CancellationToken cancellationToken = default);
}
