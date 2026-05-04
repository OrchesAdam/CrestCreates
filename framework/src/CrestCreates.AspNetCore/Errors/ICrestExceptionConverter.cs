using Microsoft.AspNetCore.Http;

namespace CrestCreates.AspNetCore.Errors;

public interface ICrestExceptionConverter
{
    CrestExceptionConversionResult Convert(HttpContext context, Exception exception);
}
