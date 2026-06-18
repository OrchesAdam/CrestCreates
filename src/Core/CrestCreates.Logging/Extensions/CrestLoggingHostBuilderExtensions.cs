using CrestCreates.Logging.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.MSSqlServer;

namespace CrestCreates.Logging.Extensions;

public static class CrestLoggingHostBuilderExtensions
{
    public static IHostBuilder UseCrestSerilog(this IHostBuilder hostBuilder)
    {
        return hostBuilder.UseSerilog((context, _, loggerConfiguration) =>
        {
            var options = BuildOptions(context.Configuration, context.HostingEnvironment.ApplicationName);
            ConfigureLogger(loggerConfiguration, options, context.HostingEnvironment.EnvironmentName);
        });
    }

    internal static CrestLoggingOptions BuildOptions(IConfiguration configuration, string? applicationName = null)
    {
        var options = new CrestLoggingOptions();
        configuration.GetSection(CrestLoggingOptions.SectionName).Bind(options);

        if (string.IsNullOrWhiteSpace(options.ApplicationName))
        {
            options.ApplicationName = applicationName;
        }

        return options;
    }

    internal static LoggerConfiguration ConfigureLogger(
        LoggerConfiguration loggerConfiguration,
        CrestLoggingOptions options,
        string? environmentName = null)
    {
        loggerConfiguration
            .MinimumLevel.Is(ConvertLogLevel(options.MinimumLevel))
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .Enrich.WithProperty("Application", options.ApplicationName ?? AppDomain.CurrentDomain.FriendlyName)
            .Enrich.WithProperty("Environment", environmentName ?? string.Empty);

        foreach (var item in options.Overrides)
        {
            loggerConfiguration.MinimumLevel.Override(item.Key, ConvertLogLevel(item.Value));
        }

        if (options.EnableConsole)
        {
            loggerConfiguration.WriteTo.Console(
                outputTemplate: options.OutputTemplate,
                restrictedToMinimumLevel: ConvertLogLevel(options.MinimumLevel));
        }

        if (options.EnableFile)
        {
            loggerConfiguration.WriteTo.File(
                path: options.FilePath,
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: options.FileSizeLimitBytes,
                retainedFileCountLimit: options.RetainedFileCountLimit,
                outputTemplate: options.OutputTemplate,
                restrictedToMinimumLevel: ConvertLogLevel(options.MinimumLevel));
        }

        if (options.EnableSeq && !string.IsNullOrWhiteSpace(options.SeqServerUrl))
        {
            loggerConfiguration.WriteTo.Seq(
                serverUrl: options.SeqServerUrl,
                apiKey: options.SeqApiKey,
                restrictedToMinimumLevel: ConvertLogLevel(options.MinimumLevel));
        }

        if (options.EnableSqlServer && !string.IsNullOrWhiteSpace(options.SqlServerConnectionString))
        {
            loggerConfiguration.WriteTo.MSSqlServer(
                connectionString: options.SqlServerConnectionString,
                sinkOptions: new MSSqlServerSinkOptions
                {
                    TableName = options.SqlServerTableName,
                    AutoCreateSqlTable = true
                },
                restrictedToMinimumLevel: ConvertLogLevel(options.MinimumLevel));
        }

        return loggerConfiguration;
    }

    internal static LogEventLevel ConvertLogLevel(Microsoft.Extensions.Logging.LogLevel logLevel)
    {
        return logLevel switch
        {
            Microsoft.Extensions.Logging.LogLevel.Trace => LogEventLevel.Verbose,
            Microsoft.Extensions.Logging.LogLevel.Debug => LogEventLevel.Debug,
            Microsoft.Extensions.Logging.LogLevel.Information => LogEventLevel.Information,
            Microsoft.Extensions.Logging.LogLevel.Warning => LogEventLevel.Warning,
            Microsoft.Extensions.Logging.LogLevel.Error => LogEventLevel.Error,
            Microsoft.Extensions.Logging.LogLevel.Critical => LogEventLevel.Fatal,
            _ => LogEventLevel.Information
        };
    }
}
