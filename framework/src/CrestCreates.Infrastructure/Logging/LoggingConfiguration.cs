using Microsoft.Extensions.Logging;

namespace CrestCreates.Infrastructure.Logging
{
    public class LoggingConfiguration
    {
        public string OutputTemplate { get; set; } = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";
        public LogLevel MinimumLevel { get; set; } = LogLevel.Information;
        public bool EnableConsole { get; set; } = true;
        public bool EnableFile { get; set; } = true;
        public string FilePath { get; set; } = "logs/log-.txt";
        public int FileRollingInterval { get; set; } = 1;
        public long FileSizeLimitBytes { get; set; } = 10485760;
        public int RetainedFileCountLimit { get; set; } = 7;
        public bool EnableSqlServer { get; set; } = false;
        public string ConnectionString { get; set; } = string.Empty;
        public string TableName { get; set; } = "Logs";
        public bool EnableSeq { get; set; } = false;
        public string SeqServerUrl { get; set; } = string.Empty;
        public string SeqApiKey { get; set; } = string.Empty;
    }
}