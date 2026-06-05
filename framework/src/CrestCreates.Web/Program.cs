using CrestCreates.Web;
using Microsoft.AspNetCore.Builder;
using CrestCreates.Modularity;

var builder = WebApplication.CreateBuilder(args);
builder.AddCrestWeb();

var app = builder.Build();
app.UseCrestWeb();
app.MapCrestWeb();
await app.InitializeModulesAsync();
app.Run();

public partial class Program;
