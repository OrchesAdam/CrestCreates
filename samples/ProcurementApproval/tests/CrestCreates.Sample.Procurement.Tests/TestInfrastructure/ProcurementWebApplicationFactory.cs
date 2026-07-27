using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CrestCreates.Sample.Procurement.Tests.TestInfrastructure;

public sealed class ProcurementWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var hostAssembly = typeof(Program).Assembly;
        var hostDir = Path.GetDirectoryName(hostAssembly.Location)!;
        builder.UseContentRoot(hostDir);
    }
}
