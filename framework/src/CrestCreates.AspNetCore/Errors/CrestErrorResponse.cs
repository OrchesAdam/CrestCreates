namespace CrestCreates.AspNetCore.Errors;

public class CrestErrorResponse
{
    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? Details { get; set; }

    public string? TraceId { get; set; }

    public int StatusCode { get; set; }
}
