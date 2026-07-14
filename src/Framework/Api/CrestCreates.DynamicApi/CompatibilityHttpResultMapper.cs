using Microsoft.AspNetCore.Http;

namespace CrestCreates.DynamicApi;

/// <summary>
/// Neutral HTTP result factory for compatibility projections.
/// Produces <see cref="DynamicApiResponse"/> / <see cref="DynamicApiResponse{T}"/>
/// envelopes matching legacy AppService HTTP contract semantics.
///
/// Both legacy Dynamic API and compatibility result contracts call this class
/// so that the response envelope is owned by a single authority rather than
/// coupling compatibility projections to <see cref="DynamicApiGeneratedRuntime"/>.
/// </summary>
public static class CompatibilityHttpResultMapper
{
    public static IResult WrapResult<T>(T? value)
    {
        return Results.Ok(new DynamicApiResponse<T?>
        {
            Code = StatusCodes.Status200OK,
            Message = "操作成功",
            Data = value
        });
    }

    public static IResult WrapVoidResult()
    {
        return Results.Ok(new DynamicApiResponse
        {
            Code = StatusCodes.Status200OK,
            Message = "操作成功"
        });
    }

    public static IResult WrapGetResult<T>(T? value)
    {
        if (value is null)
        {
            return Results.NotFound(new DynamicApiResponse
            {
                Code = StatusCodes.Status404NotFound,
                Message = "资源不存在"
            });
        }

        return WrapResult(value);
    }
}
