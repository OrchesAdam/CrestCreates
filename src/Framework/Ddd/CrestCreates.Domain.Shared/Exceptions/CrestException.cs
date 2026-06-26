using System;
using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Domain.Shared.Exceptions;

public abstract class CrestException : Exception
{
    protected CrestException(
        string errorCode,
        int httpStatusCode,
        string message,
        string? details = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        ErrorCode = errorCode;
        ErrorCodeValue = new ErrorCode(errorCode);
        HttpStatusCode = httpStatusCode;
        Details = details;
    }

    protected CrestException(
        ErrorCode errorCode,
        int httpStatusCode,
        string message,
        string? details = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        ErrorCodeValue = errorCode;
        ErrorCode = errorCode.RequireValue();
        HttpStatusCode = httpStatusCode;
        Details = details;
    }

    public string ErrorCode { get; }

    public ErrorCode ErrorCodeValue { get; }

    public int HttpStatusCode { get; }

    public string? Details { get; }
}
