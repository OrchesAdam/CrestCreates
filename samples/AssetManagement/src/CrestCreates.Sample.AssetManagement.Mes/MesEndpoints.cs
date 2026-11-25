using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Sample.AssetManagement.Contracts.Mes;
using Microsoft.Data.Sqlite;

namespace CrestCreates.Sample.AssetManagement.Mes;

internal static class MesEndpoints
{
    public static void Map(WebApplication app, MesStore store)
    {
        var json = MesJsonContext.Default;
        app.Use(WriteRejectionsAsync);
        var locks = app.MapGroup("/api/mes/production-locks/{equipmentId:guid}/{workOrderId}")
            .RequireAuthorization(policy => policy.RequireClaim(MesBearerAuthentication.TenantClaim));

        locks.MapPut("", Command(json.RegisterProductionLockRequest, json.ProductionLockState, (context, request) =>
        {
            if (!request.IsCritical)
                throw InvalidRequest("critical_order_required");

            return store.Register(Key(context));
        })).RequireAuthorization(policy => policy.RequireClaim(MesBearerAuthentication.PermissionClaim, MesPermissions.Trial));
        locks.MapGet("", Query(json.ProductionLockState, context => store.GetLock(Key(context))))
            .RequireAuthorization(policy => policy.RequireClaim(MesBearerAuthentication.PermissionClaim, MesPermissions.Trial));
        locks.MapPut("/observations/{observationId:guid}", Command(json.RecordTrialRequest, json.TrialObservation, (context, request) =>
        {
            if (request.Outcome is not (TrialOutcome.Passed or TrialOutcome.Failed) || request.ObservedAt == default)
                throw InvalidRequest("invalid_trial_observation");

            return store.RecordTrial(Key(context), RouteId(context, "observationId"), request);
        })).RequireAuthorization(policy => policy.RequireClaim(MesBearerAuthentication.PermissionClaim, MesPermissions.Trial));
        locks.MapGet("/observations/{observationId:guid}",
            Query(json.TrialObservation, context => store.GetObservation(Key(context), RouteId(context, "observationId"))))
            .RequireAuthorization(policy => policy.RequireClaim(MesBearerAuthentication.PermissionClaim, MesPermissions.Trial));
        locks.MapGet("/observations/{observationId:guid}/release-validation",
            Query(json.TrialObservation, context => store.ValidateReleaseEvidence(Key(context), RouteId(context, "observationId"))))
            .RequireAuthorization(policy => policy.RequireClaim(MesBearerAuthentication.PermissionClaim, MesPermissions.Release));
        locks.MapPost("/release", Command(json.ReleaseProductionLockRequest, json.ProductionReleaseReceipt, (context, request) =>
        {
            if (request.OperationId == Guid.Empty || request.ObservationId == Guid.Empty || request.SourceEventId == Guid.Empty)
                throw InvalidRequest("invalid_release_request");

            return store.Release(Key(context), request);
        })).RequireAuthorization(policy => policy.RequireClaim(MesBearerAuthentication.PermissionClaim, MesPermissions.Release));
        locks.MapGet("/releases/{operationId:guid}", Query(json.ProductionReleaseReceipt, context =>
            store.FindRelease(Key(context), RouteId(context, "operationId"))
                ?? throw new MesRejection(StatusCodes.Status404NotFound, "release_not_found")))
            .RequireAuthorization(policy => policy.RequireClaim(MesBearerAuthentication.PermissionClaim, MesPermissions.Release));

        var events = app.MapGroup(MesTrialEventFeed.Path)
            .RequireAuthorization(policy => policy.RequireClaim(MesBearerAuthentication.TenantClaim));
        events.MapGet("", Query(json.TrialObservationArray, context =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return store.ReadPendingEvents(TenantId(context), ReadEventCursor(context), ReadEventLimit(context));
        })).RequireAuthorization(policy => policy.RequireClaim(MesBearerAuthentication.PermissionClaim, MesPermissions.Consume));
        events.MapPut("/{sourceEventId:guid}/ack", (RequestDelegate)(context =>
        {
            context.RequestAborted.ThrowIfCancellationRequested();
            store.AcknowledgeEvent(TenantId(context), RouteId(context, "sourceEventId"));
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        })).RequireAuthorization(policy => policy.RequireClaim(MesBearerAuthentication.PermissionClaim, MesPermissions.Consume));
    }

    private static RequestDelegate Command<TRequest, TResponse>(JsonTypeInfo<TRequest> requestType, JsonTypeInfo<TResponse> responseType,
        Func<HttpContext, TRequest, TResponse> execute) where TRequest : class => async context =>
    {
        if (!context.Request.HasJsonContentType())
            throw new MesRejection(StatusCodes.Status415UnsupportedMediaType, "json_required");

        TRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync(requestType, cancellationToken: context.RequestAborted);
        }
        catch (JsonException)
        {
            throw InvalidRequest("invalid_json");
        }

        if (request is null)
            throw InvalidRequest("invalid_json");

        context.RequestAborted.ThrowIfCancellationRequested();
        await context.Response.WriteAsJsonAsync(execute(context, request), responseType, cancellationToken: context.RequestAborted);
    };

    private static RequestDelegate Query<TResponse>(JsonTypeInfo<TResponse> responseType, Func<HttpContext, TResponse> execute) =>
        context => context.Response.WriteAsJsonAsync(execute(context), responseType, cancellationToken: context.RequestAborted);

    private static async Task WriteRejectionsAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (MesRejection rejection)
        {
            await WriteErrorAsync(context, rejection.Status, rejection.Message);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6)
        {
            await WriteErrorAsync(context, StatusCodes.Status503ServiceUnavailable, "store_busy");
        }
    }

    private static Task WriteErrorAsync(HttpContext context, int status, string code)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new MesError(code), MesJsonContext.Default.MesError, cancellationToken: context.RequestAborted);
    }

    private static ProductionLockKey Key(HttpContext context)
    {
        var equipmentId = RouteId(context, "equipmentId");
        var workOrderId = (string)context.Request.RouteValues["workOrderId"]!;
        if (workOrderId.Length is 0 or > 128 || !char.IsAsciiLetterOrDigit(workOrderId[0])
            || !workOrderId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or '~'))
        {
            throw InvalidRequest("invalid_work_order_id");
        }

        return new ProductionLockKey(TenantId(context), equipmentId, workOrderId);
    }

    private static string TenantId(HttpContext context) => context.User.FindFirst(MesBearerAuthentication.TenantClaim)!.Value;

    private static Guid? ReadEventCursor(HttpContext context)
    {
        if (!context.Request.Query.TryGetValue("after", out var values))
            return null;

        return values.Count == 1 && Guid.TryParseExact(values[0], "D", out var id) && id != Guid.Empty
            ? id
            : throw InvalidRequest("invalid_trial_event_cursor");
    }

    private static int ReadEventLimit(HttpContext context)
    {
        if (!context.Request.Query.TryGetValue("limit", out var values))
            return MesTrialEventFeed.DefaultBatchSize;

        return values.Count == 1 && int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out var limit)
            && limit is > 0 and <= MesTrialEventFeed.MaximumBatchSize
            ? limit
            : throw InvalidRequest("invalid_trial_event_limit");
    }

    private static Guid RouteId(HttpContext context, string name) =>
        Guid.TryParse((string?)context.Request.RouteValues[name], out var id) && id != Guid.Empty
            ? id
            : throw InvalidRequest("invalid_route_id");

    private static MesRejection InvalidRequest(string code) => new(StatusCodes.Status400BadRequest, code);
}
