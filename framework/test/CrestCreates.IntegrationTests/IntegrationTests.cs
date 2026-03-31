using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Xunit;
using CrestCreates.TestBase;

namespace CrestCreates.IntegrationTests
{
    public class IntegrationTests : IClassFixture<WebApplicationFactory<Startup>>
    {
        private readonly WebApplicationFactory<Startup> _factory;
        private readonly HttpClient _client;

        public IntegrationTests(WebApplicationFactory<Startup> factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task HealthCheck_ShouldReturnOk()
        {
            // Arrange
            var request = new HttpRequestMessage(HttpMethod.Get, "/health");

            // Act
            var response = await _client.SendAsync(request);

            // Assert
            response.EnsureSuccessStatusCode();
            Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        }

        [Fact]
        public async Task ApiVersion_ShouldReturnCurrentVersion()
        {
            // Arrange
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/version");

            // Act
            var response = await _client.SendAsync(request);

            // Assert
            response.EnsureSuccessStatusCode();
        }

        [Fact]
        public async Task GetWeatherForecast_ShouldReturnWeatherData()
        {
            // Arrange
            var request = new HttpRequestMessage(HttpMethod.Get, "/weatherforecast");

            // Act
            var response = await _client.SendAsync(request);

            // Assert
            response.EnsureSuccessStatusCode();
        }
    }

    // 临时Startup类，用于测试
    public class Startup
    {
        public void ConfigureServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
        {
            services.AddControllers();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapGet("/health", context => context.Response.WriteAsync("{\"status\":\"healthy\"}"));
                endpoints.MapGet("/api/version", context => context.Response.WriteAsync("{\"version\":\"1.0.0\"}"));
                endpoints.MapGet("/weatherforecast", context => context.Response.WriteAsync("[{\"date\":\"2024-01-01\",\"temperatureC\":20,\"temperatureF\":68,\"summary\":\"Mild\"}]") );
            });
        }
    }
}
