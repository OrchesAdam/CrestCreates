using CrestCreates.Authorization.Abstractions;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Domain.Shared.Exceptions;
using CrestCreates.DynamicApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CrestCreates.TicketHttpProjection.AotFixture;

/// <summary>
/// Fixture-local transport adapter over the shared failure facts. The real Web presenter owns the
/// CrestErrorResponse shape; this native fixture only needs bounded statuses and a serializable
/// ProblemDetails body, and it keeps the same attribution rules (validation 400, forbidden 403,
/// not-found 404, conflict 409, unknown 500).
/// </summary>
public sealed class TicketExceptionClassifier : ICapabilityExceptionClassifier
{
    public CapabilityFailureFacts Classify(Exception exception)
        => exception switch
        {
            CrestValidationException => new CapabilityFailureFacts(
                CapabilityFailureKind.ValidationFailed, "Crest.Validation.Failed", "数据验证失败。"),
            CrestPermissionException => new CapabilityFailureFacts(
                CapabilityFailureKind.Forbidden, "Crest.Auth.Forbidden", "没有权限执行当前操作。"),
            CrestCreates.Domain.Exceptions.CrestPermissionException => new CapabilityFailureFacts(
                CapabilityFailureKind.Forbidden, "Crest.Auth.Forbidden", "没有权限执行当前操作。"),
            CrestEntityNotFoundException or KeyNotFoundException => new CapabilityFailureFacts(
                CapabilityFailureKind.NotFound, "Crest.Entity.NotFound", "资源不存在。"),
            CrestCreates.Domain.Exceptions.CrestConcurrencyException => new CapabilityFailureFacts(
                CapabilityFailureKind.Conflict, "Crest.Concurrency.Conflict", "并发冲突。"),
            InvalidOperationException or ArgumentException => new CapabilityFailureFacts(
                CapabilityFailureKind.InvalidRequest, "Crest.Operation.Invalid", "操作无效。"),
            _ => new CapabilityFailureFacts(
                CapabilityFailureKind.Unknown, "Crest.InternalError", "服务器内部错误。"),
        };
}

public sealed class TicketEndpointFailurePresenter : ICapabilityEndpointFailurePresenter
{
    public IResult Present(CapabilityExecutionResult result, HttpContext context)
    {
        if (context.RequestAborted.IsCancellationRequested || context.Response.HasStarted)
        {
            return Results.Empty;
        }

        var facts = result.FailureFacts;
        var status = facts?.Kind switch
        {
            CapabilityFailureKind.InvalidRequest => StatusCodes.Status400BadRequest,
            CapabilityFailureKind.ValidationFailed => StatusCodes.Status400BadRequest,
            CapabilityFailureKind.Unauthorized => StatusCodes.Status403Forbidden,
            CapabilityFailureKind.Forbidden => StatusCodes.Status403Forbidden,
            CapabilityFailureKind.NotFound => StatusCodes.Status404NotFound,
            CapabilityFailureKind.Conflict => StatusCodes.Status409Conflict,
            CapabilityFailureKind.PreconditionRequired => StatusCodes.Status428PreconditionRequired,
            CapabilityFailureKind.Cancelled => 499,
            _ => result.ErrorCode switch
            {
                "UNAUTHORIZED" => StatusCodes.Status403Forbidden,
                "CAPABILITY_FORBIDDEN" => StatusCodes.Status403Forbidden,
                "CAPABILITY_RESOURCE_NOT_FOUND" or "CAPABILITY_NOT_FOUND" => StatusCodes.Status404NotFound,
                "CAPABILITY_VALIDATION_FAILED" => StatusCodes.Status400BadRequest,
                "CAPABILITY_DECISION_CONFLICT" or "CAPABILITY_DECISION_STATE_INVALID" => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status500InternalServerError,
            },
        };

        Console.WriteLine($"[ticket-gate] failure {status}: {result.ErrorCode} {result.ErrorMessage}");
        return Results.Problem(
            statusCode: status,
            title: facts?.SafeMessage ?? "服务器内部错误。",
            detail: facts?.SafeDetails);
    }

    public IResult PresentBindingFailure(Exception exception, HttpContext context)
        => context.Response.HasStarted || context.RequestAborted.IsCancellationRequested
            ? Results.Empty
            : Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "请求无效。");

    public IResult PresentNotFound(HttpContext context)
        => context.Response.HasStarted || context.RequestAborted.IsCancellationRequested
            ? Results.Empty
            : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "资源不存在。");
}
