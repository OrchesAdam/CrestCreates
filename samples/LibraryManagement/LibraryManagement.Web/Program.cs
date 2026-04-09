using LibraryManagement.Web.Modules;
using CrestCreates.AuditLogging.Middlewares;
using CrestCreates.AuditLogging.Options;
using CrestCreates.Logging.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CrestCreates.Web.Middlewares;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseCrestSerilog();
builder.Services.AddCrestLogging(builder.Configuration);
builder.Services.Configure<AuditLoggingOptions>(
    builder.Configuration.GetSection(AuditLoggingOptions.SectionName));

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register all modules using the module discovery system
LibraryManagement.Web.Modules.ModuleAutoInitializer.RegisterAllModules(builder.Services);

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCrestRequestLogging();
app.UseExceptionHandling();
app.UseAuditLogging();
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// Initialize all modules
LibraryManagement.Web.Modules.ModuleAutoInitializer.InitializeAllModules(app.Services);

app.Run();
