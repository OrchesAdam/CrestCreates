using System;
using System.Collections.Generic;
using System.Linq;
using CrestCreates.AspNetCore.Errors;
using CrestCreates.AspNetCore.Serialization;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Domain.Shared.Exceptions;
using CrestCreates.DynamicApi;
using Microsoft.AspNetCore.Http;

namespace CrestCreates.Web.Errors;

/// <summary>
/// Renders capability failures with the platform's authoritative error wire (CrestErrorResponse,
/// source-generated metadata, localized fallback messages). This is the only place that couples
/// runtime failure facts to HTTP status codes.
/// </summary>
public sealed class CrestCapabilityEndpointFailurePresenter(
    ICapabilityExceptionClassifier classifier) : ICapabilityEndpointFailurePresenter
{
    public IResult Present(CapabilityExecutionResult result, HttpContext context)
    {
        if (result.Status == CapabilityExecutionStatus.Cancelled
            || (result.Status == CapabilityExecutionStatus.TimedOut && context.RequestAborted.IsCancellationRequested))
        {
            return Results.StatusCode(context.RequestAborted.IsCancellationRequested ? 499 : 504);
        }

        var facts = result.FailureFacts ?? ClassifyErrorCode(result.ErrorCode, result.ErrorMessage, result.Issues);
        return Write(facts, context);
    }

    public IResult PresentBindingFailure(Exception exception, HttpContext context)
    {
        var facts = classifier.Classify(exception);
        if (facts.Kind is not (CapabilityFailureKind.InvalidRequest or CapabilityFailureKind.ValidationFailed))
        {
            facts = new CapabilityFailureFacts(
                CapabilityFailureKind.InvalidRequest,
                CrestErrorCodes.InvalidArgument,
                "请求参数错误。");
        }

        return Write(facts, context);
    }

    public IResult PresentNotFound(HttpContext context)
        => Write(new CapabilityFailureFacts(
            CapabilityFailureKind.NotFound, CrestErrorCodes.EntityNotFound, "资源不存在。"), context);

    private static IResult Write(CapabilityFailureFacts facts, HttpContext context)
    {
        var authenticated = context.User.Identity?.IsAuthenticated == true;
        var kind = facts.Kind == CapabilityFailureKind.Unauthorized && authenticated
            ? CapabilityFailureKind.Forbidden
            : facts.Kind;
        var errorCode = kind == CapabilityFailureKind.Forbidden
                        && facts.Kind == CapabilityFailureKind.Unauthorized
            ? CrestErrorCodes.AuthForbidden
            : facts.ErrorCode;

        var statusCode = kind switch
        {
            CapabilityFailureKind.InvalidRequest => StatusCodes.Status400BadRequest,
            CapabilityFailureKind.ValidationFailed => StatusCodes.Status400BadRequest,
            CapabilityFailureKind.Unauthorized => StatusCodes.Status401Unauthorized,
            CapabilityFailureKind.Forbidden => StatusCodes.Status403Forbidden,
            CapabilityFailureKind.NotFound => StatusCodes.Status404NotFound,
            CapabilityFailureKind.Conflict => StatusCodes.Status409Conflict,
            CapabilityFailureKind.PreconditionRequired => StatusCodes.Status428PreconditionRequired,
            CapabilityFailureKind.Cancelled => StatusCodes.Status499ClientClosedRequest,
            _ => StatusCodes.Status500InternalServerError,
        };

        var response = new CrestErrorResponse
        {
            Code = errorCode,
            Message = MessageFor(errorCode, facts.SafeMessage),
            Details = kind == CapabilityFailureKind.Unknown ? null : facts.SafeDetails,
            TraceId = context.TraceIdentifier,
            StatusCode = statusCode,
        };

        return Results.Json(
            response,
            CrestErrorResponseJsonContext.Default.CrestErrorResponse,
            statusCode: statusCode);
    }

    private static CapabilityFailureFacts ClassifyErrorCode(
        string? errorCode,
        string? errorMessage,
        IReadOnlyList<CapabilityExecutionIssue> issues)
    {
        var details = issues.Count == 0
            ? null
            : string.Join("; ", issues.Select(issue => issue.Message));
        return errorCode switch
        {
            "UNAUTHORIZED" => new(CapabilityFailureKind.Unauthorized, CrestErrorCodes.AuthUnauthorized, "当前请求未认证。", details),
            "CAPABILITY_FORBIDDEN" or "CAPABILITY_INVOCATION_SOURCE_FORBIDDEN" =>
                new(CapabilityFailureKind.Forbidden, CrestErrorCodes.AuthForbidden, "没有权限执行当前操作。", details),
            "CAPABILITY_RESOURCE_NOT_FOUND" or "CAPABILITY_NOT_FOUND" =>
                new(CapabilityFailureKind.NotFound, CrestErrorCodes.EntityNotFound, "资源不存在。"),
            "CAPABILITY_VALIDATION_FAILED" =>
                new(CapabilityFailureKind.ValidationFailed, CrestErrorCodes.ValidationFailed, "数据验证失败。"),
            "CAPABILITY_DECISION_CONFLICT" or "CAPABILITY_DECISION_STATE_INVALID" =>
                new(CapabilityFailureKind.Conflict, CrestErrorCodes.ConcurrencyConflict, "并发冲突。"),
            "CAPABILITY_CONTEXT_REQUIRED" =>
                new(CapabilityFailureKind.Forbidden, CrestErrorCodes.AuthForbidden, "没有可用的受信租户与用户上下文。"),
            "PIPELINE_ERROR" or null =>
                new(CapabilityFailureKind.Unknown, CrestErrorCodes.InternalError, "服务器内部错误。"),
            _ => new(CapabilityFailureKind.InvalidRequest, errorCode, errorMessage ?? "请求参数错误。", details),
        };
    }

    private static string MessageFor(string errorCode, string fallback) => errorCode switch
    {
        "Crest.Auth.Unauthorized" => "当前请求未认证。",
        "Crest.Auth.Forbidden" => "没有权限执行当前操作。",
        "Crest.Validation.Failed" => "数据验证失败。",
        "Crest.Entity.NotFound" => "资源不存在。",
        "Crest.Concurrency.Conflict" => "并发冲突。",
        "Crest.Concurrency.PreconditionRequired" => "缺少并发前置条件。",
        "Crest.Request.InvalidArgument" => "请求参数错误。",
        "Crest.Operation.Invalid" => "当前操作无效。",
        "Crest.InternalError" => "服务器内部错误。",
        _ => fallback,
    };
}
