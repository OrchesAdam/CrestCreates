using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace CrestCreates.GeneratedCrud.AotFixture.Tests;

public class Review150FilterInputTests
{
    [Theory]
    [InlineData("2147483648")]
    [InlineData("1.5")]
    public async Task Review150_bad_numeric_filter_operator_must_return_400(string op)
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/api/widgets/search", new StringContent(
            "{\"filters\":[{\"field\":\"Price\",\"operator\":"+op+",\"value\":1}]}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
