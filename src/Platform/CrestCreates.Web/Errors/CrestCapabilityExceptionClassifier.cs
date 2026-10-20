using System;
using System.Collections.Generic;
using System.Linq;
using CrestCreates.Authorization.Abstractions;
using CrestPermissionException = CrestCreates.Authorization.Abstractions.CrestPermissionException;
using CrestConcurrencyException = CrestCreates.Domain.Exceptions.CrestConcurrencyException;
using CrestPreconditionRequiredException = CrestCreates.Domain.Exceptions.CrestPreconditionRequiredException;
using System.ComponentModel.DataAnnotations;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Domain.Shared.Exceptions;
using Microsoft.AspNetCore.Http;

namespace CrestCreates.Web.Errors;

/// <summary>
/// Web-authoritative classifier for capability/business exceptions. It returns only bounded facts:
/// no exception, type, stack trace, HTTP status code or transport type crosses into Runtime.
/// </summary>
public sealed class CrestCapabilityExceptionClassifier : ICapabilityExceptionClassifier
{
    public CapabilityFailureFacts Classify(Exception exception) => exception switch
    {
        CapabilityFailureException capability => ClassifyCapabilityFailure(capability),
        CrestPermissionException permission => Facts(
            CapabilityFailureKind.Forbidden, CrestErrorCodes.AuthForbidden, "没有权限执行当前操作。",
            permission.PermissionName),
        CrestEntityNotFoundException notFound => Facts(
            CapabilityFailureKind.NotFound, CrestErrorCodes.EntityNotFound, "资源不存在。", notFound.Message),
        KeyNotFoundException keyNotFound => Facts(
            CapabilityFailureKind.NotFound, CrestErrorCodes.EntityNotFound, "资源不存在。", keyNotFound.Message),
        CrestPreconditionRequiredException precondition => Facts(
            CapabilityFailureKind.PreconditionRequired, CrestErrorCodes.ConcurrencyPreconditionRequired,
            "缺少并发前置条件。", precondition.Message),
        CrestConcurrencyException concurrency => Facts(
            CapabilityFailureKind.Conflict, CrestErrorCodes.ConcurrencyConflict, "并发冲突。", concurrency.Message),
        FluentValidation.ValidationException fluent => Facts(
            CapabilityFailureKind.ValidationFailed, CrestErrorCodes.ValidationFailed, "数据验证失败。",
            string.Join("; ", fluent.Errors.Select(error => error.ErrorMessage))),
        ValidationException dataAnnotations => Facts(
            CapabilityFailureKind.ValidationFailed, CrestErrorCodes.ValidationFailed, "数据验证失败。",
            dataAnnotations.Message),
        BadHttpRequestException badRequest => Facts(
            CapabilityFailureKind.InvalidRequest, CrestErrorCodes.InvalidArgument, "请求参数错误。",
            badRequest.Message),
        // JsonException/FormatException are only request errors at the binding boundary. The mapper
        // classifies that stage locally; a business-internal JSON/format failure stays a sanitized
        // server error instead of being rewritten into a 400.
        ArgumentException argument => Facts(
            CapabilityFailureKind.InvalidRequest, CrestErrorCodes.InvalidArgument, "请求参数错误。",
            argument.Message),
        UnauthorizedAccessException => Facts(
            CapabilityFailureKind.Unauthorized, CrestErrorCodes.AuthUnauthorized, "当前请求未认证。"),
        InvalidOperationException invalidOperation => Facts(
            CapabilityFailureKind.InvalidRequest, CrestErrorCodes.OperationInvalid, "当前操作无效。",
            invalidOperation.Message),
        CrestException crest => ClassifyCrestException(crest),
        _ => Facts(CapabilityFailureKind.Unknown, CrestErrorCodes.InternalError, "服务器内部错误。"),
    };

    private static CapabilityFailureFacts ClassifyCapabilityFailure(CapabilityFailureException exception)
    {
        var details = exception.Issues.Count == 0
            ? null
            : string.Join("; ", exception.Issues.Select(issue => issue.Message));
        return exception.ErrorCode switch
        {
            "UNAUTHORIZED" => Facts(
                CapabilityFailureKind.Unauthorized, CrestErrorCodes.AuthUnauthorized, "当前请求未认证。", details),
            "CAPABILITY_FORBIDDEN" or "CAPABILITY_INVOCATION_SOURCE_FORBIDDEN" => Facts(
                CapabilityFailureKind.Forbidden, CrestErrorCodes.AuthForbidden, "没有权限执行当前操作。", details),
            "CAPABILITY_CONTEXT_REQUIRED" => Facts(
                CapabilityFailureKind.Forbidden, CrestErrorCodes.AuthForbidden, "没有可用的受信租户与用户上下文。"),
            "CAPABILITY_RESOURCE_NOT_FOUND" or "CAPABILITY_NOT_FOUND" => Facts(
                CapabilityFailureKind.NotFound, CrestErrorCodes.EntityNotFound, "资源不存在。", details),
            "CAPABILITY_VALIDATION_FAILED" => Facts(
                CapabilityFailureKind.ValidationFailed, CrestErrorCodes.ValidationFailed, "数据验证失败。", details),
            "CAPABILITY_DECISION_CONFLICT" or "CAPABILITY_DECISION_STATE_INVALID" => Facts(
                CapabilityFailureKind.Conflict, CrestErrorCodes.ConcurrencyConflict, "并发冲突。"),
            _ => Facts(
                CapabilityFailureKind.InvalidRequest, exception.ErrorCode, exception.Message, details),
        };
    }

    private static CapabilityFailureFacts ClassifyCrestException(CrestException exception)
        => exception.HttpStatusCode switch
        {
            401 => Facts(CapabilityFailureKind.Unauthorized, exception.ErrorCode, "当前请求未认证。"),
            403 => Facts(CapabilityFailureKind.Forbidden, exception.ErrorCode, "没有权限执行当前操作。", exception.Details),
            404 => Facts(CapabilityFailureKind.NotFound, exception.ErrorCode, "资源不存在。", exception.Details),
            409 => Facts(CapabilityFailureKind.Conflict, exception.ErrorCode, "并发冲突。", exception.Details),
            428 => Facts(CapabilityFailureKind.PreconditionRequired, exception.ErrorCode, "缺少并发前置条件。", exception.Details),
            400 => Facts(CapabilityFailureKind.InvalidRequest, exception.ErrorCode, exception.Message, exception.Details),
            _ => Facts(CapabilityFailureKind.Unknown, exception.ErrorCode, "服务器内部错误。"),
        };

    private static CapabilityFailureFacts Facts(
        CapabilityFailureKind kind, string errorCode, string safeMessage, string? safeDetails = null)
        => new(kind, errorCode, safeMessage, safeDetails);
}
