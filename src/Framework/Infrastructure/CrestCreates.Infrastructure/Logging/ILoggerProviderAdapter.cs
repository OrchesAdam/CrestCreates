using Microsoft.Extensions.Logging;

namespace CrestCreates.Infrastructure.Logging
{
    public interface ILoggerProviderAdapter : ILoggerProvider
    {
        void Configure();
        void SetLogLevel(string categoryName, LogLevel logLevel);
        LogLevel GetLogLevel(string categoryName);
        void SetGlobalLogLevel(LogLevel logLevel);
        LogLevel GetGlobalLogLevel();
        void UpdateConfiguration(LoggingConfiguration configuration);
    }
}