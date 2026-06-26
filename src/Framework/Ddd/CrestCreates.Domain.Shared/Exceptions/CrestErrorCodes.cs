using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Domain.Shared.Exceptions;

public static class CrestErrorCodes
{
    public const string InternalErrorValue = "Crest.InternalError";
    public static ErrorCode InternalError { get; } = new(InternalErrorValue);

    public const string AuthUnauthorizedValue = "Crest.Auth.Unauthorized";
    public static ErrorCode AuthUnauthorized { get; } = new(AuthUnauthorizedValue);

    public const string AuthForbiddenValue = "Crest.Auth.Forbidden";
    public static ErrorCode AuthForbidden { get; } = new(AuthForbiddenValue);

    public const string ValidationFailedValue = "Crest.Validation.Failed";
    public static ErrorCode ValidationFailed { get; } = new(ValidationFailedValue);

    public const string ConcurrencyConflictValue = "Crest.Concurrency.Conflict";
    public static ErrorCode ConcurrencyConflict { get; } = new(ConcurrencyConflictValue);

    public const string ConcurrencyPreconditionRequiredValue = "Crest.Concurrency.PreconditionRequired";
    public static ErrorCode ConcurrencyPreconditionRequired { get; } = new(ConcurrencyPreconditionRequiredValue);

    public const string EntityNotFoundValue = "Crest.Entity.NotFound";
    public static ErrorCode EntityNotFound { get; } = new(EntityNotFoundValue);

    public const string InvalidArgumentValue = "Crest.Request.InvalidArgument";
    public static ErrorCode InvalidArgument { get; } = new(InvalidArgumentValue);

    public const string OperationInvalidValue = "Crest.Operation.Invalid";
    public static ErrorCode OperationInvalid { get; } = new(OperationInvalidValue);
}
