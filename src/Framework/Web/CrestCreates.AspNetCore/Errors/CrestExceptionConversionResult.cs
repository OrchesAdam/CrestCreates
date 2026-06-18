using Microsoft.Extensions.Logging;

namespace CrestCreates.AspNetCore.Errors;

public class CrestExceptionConversionResult
{
    public CrestExceptionConversionResult(CrestErrorResponse response, LogLevel logLevel)
    {
        Response = response;
        LogLevel = logLevel;
    }

    public CrestErrorResponse Response { get; }

    public LogLevel LogLevel { get; }
}
