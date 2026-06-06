using Microsoft.AspNetCore.Http;

namespace CrestCreates.DynamicApi;

public abstract class CrestApiController
{
    public IResult Ok<T>(T value)
    {
        return Results.Ok(value);
    }

    public IResult NotFound()
    {
        return Results.NotFound();
    }

    public IResult NoContent()
    {
        return Results.NoContent();
    }
}
