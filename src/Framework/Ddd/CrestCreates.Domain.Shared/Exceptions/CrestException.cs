using System;

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
        HttpStatusCode = httpStatusCode;
        Details = details;
    }

    public string ErrorCode { get; }

    public int HttpStatusCode { get; }

    public string? Details { get; }
}
