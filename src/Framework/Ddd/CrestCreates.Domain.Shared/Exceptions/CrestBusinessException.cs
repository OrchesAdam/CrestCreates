using System;

namespace CrestCreates.Domain.Shared.Exceptions;

public class CrestBusinessException : CrestException
{
    public CrestBusinessException(
        string errorCode,
        string message,
        string? details = null,
        Exception? innerException = null)
        : base(errorCode, 400, message, details, innerException)
    {
    }
}
