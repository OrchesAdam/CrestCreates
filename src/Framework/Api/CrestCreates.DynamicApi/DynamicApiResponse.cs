using Microsoft.AspNetCore.Http;

namespace CrestCreates.DynamicApi;

public class DynamicApiResponse
{
    public int Code { get; set; } = StatusCodes.Status200OK;

    public string Message { get; set; } = "操作成功";
}

public sealed class DynamicApiResponse<T> : DynamicApiResponse
{
    public T? Data { get; set; }
}
