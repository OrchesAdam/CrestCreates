using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Sample.Procurement.Tests.TestInfrastructure;

public sealed class ProcurementWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly Action<IServiceCollection>? _configureServices;

    public ProcurementWebApplicationFactory(Action<IServiceCollection>? configureServices = null)
        => _configureServices = configureServices;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var hostAssembly = typeof(Program).Assembly;
        var hostDir = Path.GetDirectoryName(hostAssembly.Location)!;
        builder.UseContentRoot(hostDir);
        builder.ConfigureServices(services => _configureServices?.Invoke(services));
    }
}
