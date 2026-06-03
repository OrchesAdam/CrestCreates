using System.Net;
using CrestCreates.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SaaSHelpdesk.Tests.Authorization;

public class AuthorizationStartupTests
{
    [Fact]
    public async Task UseAuthorization_ShouldProcessRequest_WhenAddCrestAuthorizationRegistersServices()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        builder.WebHost.UseTestServer();
        builder.Services.AddCrestAuthorization();

        var app = builder.Build();
        app.UseRouting();
        app.UseAuthorization();
        app.MapGet("/", () => Results.Ok()).AllowAnonymous();

        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await app.StopAsync();
    }
}
