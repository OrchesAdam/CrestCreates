using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Data.Sqlite;

namespace CrestCreates.Sample.AssetManagement.Mes;

public static class Program
{
    public static void Main(string[] args) => Build(args).Run();

    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateSlimBuilder(args);
        var settings = builder.Configuration.GetSection("Mes");
        var databasePath = settings["DatabasePath"] ?? "asset-mes.db";
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new InvalidOperationException("Mes:DatabasePath requires a SQLite file path.");

        var evidenceMaxAge = TimeSpan.FromMinutes(30);
        if (settings["EvidenceMaxAge"] is { } configuredAge
            && (!TimeSpan.TryParseExact(configuredAge, "c", CultureInfo.InvariantCulture, out evidenceMaxAge) || evidenceMaxAge <= TimeSpan.Zero))
        {
            throw new InvalidOperationException("Mes:EvidenceMaxAge requires a positive TimeSpan (for example 00:30:00).");
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath, builder.Environment.ContentRootPath),
            ForeignKeys = true,
            DefaultTimeout = 5
        }.ToString();
        builder.Services.AddSingleton(new MesTenantCredentials(settings.GetSection("Tenants")));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(services => new MesStore(connectionString, services.GetRequiredService<TimeProvider>(), evidenceMaxAge));
        builder.Services.AddAuthentication(MesBearerAuthentication.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, MesBearerAuthentication>(MesBearerAuthentication.SchemeName, null);
        builder.Services.AddAuthorization();

        var app = builder.Build();
        var store = app.Services.GetRequiredService<MesStore>();
        store.Initialize();
        app.UseAuthentication();
        app.UseAuthorization();
        MesEndpoints.Map(app, store);
        return app;
    }
}
