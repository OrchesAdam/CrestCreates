using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http;
using Xunit;

namespace CrestCreates.Testing.Integration;

public abstract class IntegrationTestBase<TProgram> : IClassFixture<WebApplicationFactory<TProgram>> where TProgram : class
{
    protected readonly WebApplicationFactory<TProgram> Factory;
    protected readonly HttpClient Client;

    public IntegrationTestBase(WebApplicationFactory<TProgram> factory)
    {
        Factory = factory;
        Client = factory.CreateClient();
    }

    protected virtual WebApplicationFactory<TProgram> CreateFactory(Action<IServiceCollection>? configureServices = null)
    {
        return Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                configureServices?.Invoke(services);
            });
        });
    }

    protected virtual HttpClient CreateClient(Action<IServiceCollection>? configureServices = null)
    {
        var factory = CreateFactory(configureServices);
        return factory.CreateClient();
    }
}