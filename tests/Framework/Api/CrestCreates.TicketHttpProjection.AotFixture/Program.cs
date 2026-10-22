using CrestCreates.DynamicApi;
using CrestCreates.TicketHttpProjection.AotFixture;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

var gateMode = args.Contains("--ticket-gate");
if (gateMode)
{
    builder.WebHost.UseUrls("http://127.0.0.1:0");
}

builder.Services.AddTicketFixture();

var app = builder.Build();

app.Services.GetRequiredService<TicketMemoryStore>().SeedActiveSlaPolicy();

// The generated per-assembly activation also contributes the source-generated JSON configurator;
// the map-time contract verification therefore sees the generated context on the final chain.
app.MapCrestCapabilityEndpoints();

if (gateMode)
{
    await TicketNativeGate.RunAsync(app, args);
    return;
}

app.Run();

public partial class Program;
