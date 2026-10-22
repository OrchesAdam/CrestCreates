using CrestCreates.DynamicApi;
using CrestCreates.Sample.AssetManagement.Application;
using CrestCreates.Sample.AssetManagement.Host.Projections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FluentAssertions;

namespace CrestCreates.Sample.AssetManagement.E2E.Tests;

public sealed class ReviewS23AssetCompositionProbes
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Production_host_business_DI_must_include_the_self_contract(bool requireSelfContract)
    {
        using var factory = new CaptureFactory();
        var failure = Record.Exception(() => factory.CreateClient());
        factory.Captured.Should().NotBeNull("the real Program must reach the DI callback before the intentional stop");
        failure.Should().NotBeNull();
        failure!.ToString().Should().Contain(CaptureFactory.Stop);
        var services = factory.Captured!;
        services.Should().Contain(item => item.ServiceType == typeof(AssetApplicationService),
            "guard: the actual Asset production registration sequence ran");
        services.Should().Contain(item => item.ServiceType == typeof(CapabilityEndpointContribution),
            "guard: AddGeneratedAppServiceHttp contributed the formal surface");
        if (requireSelfContract)
            services.Any(item => item.ServiceType == typeof(AssetCompatibilityProjection)).Should().BeTrue(
                "the formal invoker uses GetRequiredService<AssetCompatibilityProjection>; the business owner must register it");
    }

    private sealed class CaptureFactory : WebApplicationFactory<Program>
    {
        public const string Stop = "REVIEW_STOP_AFTER_PRODUCTION_DI";
        public IServiceCollection? Captured { get; private set; }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(Path.GetDirectoryName(typeof(Program).Assembly.Location)!);
            builder.UseSetting("AssetManagement:RuntimeConnectionString",
                "Host=127.0.0.1;Port=1;Database=review;Username=review;Password=review;Timeout=1");
            builder.UseSetting("AssetManagement:DatabasePath", Path.Combine(Path.GetTempPath(), "review-s23-" + Guid.NewGuid().ToString("N") + ".db"));
            builder.ConfigureServices(services =>
            {
                Captured = services;
                throw new InvalidOperationException(Stop);
            });
        }
    }
}
