using System;
using System.Collections.Generic;

namespace CrestCreates.Domain.Shared.Exceptions;

public class CrestValidationException : CrestException
{
    public CrestValidationException(
        string message = "Validation failed.",
        IReadOnlyList<string>? errors = null,
        Exception? innerException = null)
        : base("Crest.Validation.Failed", 400, message, errors is { Count: > 0 } ? string.Join("; ", errors) : null, innerException)
    {
        Errors = errors ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> Errors { get; }
}
