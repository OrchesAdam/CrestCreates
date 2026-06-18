using Microsoft.AspNetCore.Http;

namespace CrestCreates.DynamicApi;

public abstract class CrestApiController
{
    protected IResult Ok<T>(T value)
    {
        return Results.Ok(value);
    }

    protected IResult NotFound()
    {
        return Results.NotFound();
    }

    protected IResult NoContent()
    {
        return Results.NoContent();
    }
}
