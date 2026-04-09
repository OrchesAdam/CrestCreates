using CrestCreates.Logging.Middlewares;
using Microsoft.AspNetCore.Builder;

namespace CrestCreates.Logging.Extensions;

public static class ApplicationBuilderExtensions
{
    public static IApplicationBuilder UseCrestRequestLogging(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<RequestLoggingMiddleware>();
    }
}
