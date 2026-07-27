using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.DynamicApi;
using CrestCreates.Sample.Procurement.Application.Handlers;
using CrestCreates.Sample.Procurement.Contracts.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCapabilityRuntime();
builder.Services.AddSingleton<ICapabilityHandlerModule>(new ProcurementCapabilityModule());
builder.Services.AddCrestCapabilityEndpoints();
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
{
    options.SerializerOptions.TypeInfoResolver = ProcurementJsonContext.Default;
});

var app = builder.Build();

app.MapCrestCapabilityEndpoints();

app.Run();

public partial class Program;
