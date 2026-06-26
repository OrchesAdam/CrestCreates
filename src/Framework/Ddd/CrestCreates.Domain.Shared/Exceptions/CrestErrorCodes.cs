using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Domain.Shared.Exceptions;

public static class CrestErrorCodes
{
    private const string InternalErrorValue = "Crest.InternalError";
    public static ErrorCode InternalError { get; } = new(InternalErrorValue);

    private const string AuthUnauthorizedValue = "Crest.Auth.Unauthorized";
    public static ErrorCode AuthUnauthorized { get; } = new(AuthUnauthorizedValue);

    private const string AuthForbiddenValue = "Crest.Auth.Forbidden";
    public static ErrorCode AuthForbidden { get; } = new(AuthForbiddenValue);

    private const string ValidationFailedValue = "Crest.Validation.Failed";
    public static ErrorCode ValidationFailed { get; } = new(ValidationFailedValue);

    private const string ConcurrencyConflictValue = "Crest.Concurrency.Conflict";
    public static ErrorCode ConcurrencyConflict { get; } = new(ConcurrencyConflictValue);

    private const string ConcurrencyPreconditionRequiredValue = "Crest.Concurrency.PreconditionRequired";
    public static ErrorCode ConcurrencyPreconditionRequired { get; } = new(ConcurrencyPreconditionRequiredValue);

    private const string EntityNotFoundValue = "Crest.Entity.NotFound";
    public static ErrorCode EntityNotFound { get; } = new(EntityNotFoundValue);

    private const string InvalidArgumentValue = "Crest.Request.InvalidArgument";
    public static ErrorCode InvalidArgument { get; } = new(InvalidArgumentValue);

    private const string OperationInvalidValue = "Crest.Operation.Invalid";
    public static ErrorCode OperationInvalid { get; } = new(OperationInvalidValue);
}
