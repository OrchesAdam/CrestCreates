using Microsoft.Extensions.Logging;

namespace CrestCreates.Logging.Options;

public class CrestLoggingOptions
{
    public const string SectionName = "CrestLogging";

    public string? ApplicationName { get; set; }

    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    public Dictionary<string, LogLevel> Overrides { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft"] = LogLevel.Warning,
        ["Microsoft.AspNetCore"] = LogLevel.Warning,
        ["System"] = LogLevel.Warning
    };

    public bool EnableConsole { get; set; } = true;

    public bool EnableFile { get; set; } = true;

    public string FilePath { get; set; } = "logs/log-.txt";

    public long? FileSizeLimitBytes { get; set; } = 10 * 1024 * 1024;

    public int? RetainedFileCountLimit { get; set; } = 7;

    public bool EnableSeq { get; set; }

    public string? SeqServerUrl { get; set; }

    public string? SeqApiKey { get; set; }

    public bool EnableSqlServer { get; set; }

    public string? SqlServerConnectionString { get; set; }

    public string SqlServerTableName { get; set; } = "Logs";

    public string OutputTemplate { get; set; } =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj} {Properties:j}{NewLine}{Exception}";
}
