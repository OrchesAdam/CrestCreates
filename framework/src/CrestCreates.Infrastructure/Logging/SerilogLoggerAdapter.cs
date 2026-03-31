using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Context;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Sinks.File;
using Serilog.Sinks.MSSqlServer;
using Serilog.Sinks.Seq;

namespace CrestCreates.Infrastructure.Logging
{
    public class SerilogLoggerAdapter : ILoggerProviderAdapter
    {
        private Logger _logger;
        private LoggingConfiguration _configuration;
        private readonly Dictionary<string, LogLevel> _logLevels = new Dictionary<string, LogLevel>();

        public SerilogLoggerAdapter(LoggingConfiguration configuration)
        {
            _configuration = configuration;
            _logger = CreateLogger();
        }

        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName)
        {
            return new SerilogLogger(_logger, categoryName, _logLevels);
        }

        public void Configure()
        {
            // 重新创建日志记录器以应用新配置
            _logger.Dispose();
            _logger = CreateLogger();
        }

        public void UpdateConfiguration(LoggingConfiguration configuration)
        {
            _configuration = configuration;
            Configure();
        }

        public void SetLogLevel(string categoryName, LogLevel logLevel)
        {
            _logLevels[categoryName] = logLevel;
        }

        public LogLevel GetLogLevel(string categoryName)
        {
            if (_logLevels.TryGetValue(categoryName, out var logLevel))
            {
                return logLevel;
            }
            return _configuration.MinimumLevel;
        }

        public void SetGlobalLogLevel(LogLevel logLevel)
        {
            _configuration.MinimumLevel = logLevel;
            Configure();
        }

        public LogLevel GetGlobalLogLevel()
        {
            return _configuration.MinimumLevel;
        }

        public void Dispose()
        {
            _logger.Dispose();
        }

        private Logger CreateLogger()
        {
            var loggerConfiguration = new LoggerConfiguration()
                .MinimumLevel.Is(ConvertLogLevel(_configuration.MinimumLevel))
                .Enrich.FromLogContext();

            if (_configuration.EnableConsole)
            {
                loggerConfiguration = loggerConfiguration.WriteTo.Console(
                    outputTemplate: _configuration.OutputTemplate,
                    restrictedToMinimumLevel: ConvertLogLevel(_configuration.MinimumLevel)
                );
            }

            if (_configuration.EnableFile)
            {
                loggerConfiguration = loggerConfiguration.WriteTo.File(
                    path: _configuration.FilePath,
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: _configuration.FileSizeLimitBytes,
                    retainedFileCountLimit: _configuration.RetainedFileCountLimit,
                    outputTemplate: _configuration.OutputTemplate,
                    restrictedToMinimumLevel: ConvertLogLevel(_configuration.MinimumLevel)
                );
            }

            if (_configuration.EnableSqlServer && !string.IsNullOrEmpty(_configuration.ConnectionString))
            {
                var sinkOptions = new MSSqlServerSinkOptions {
                    TableName = _configuration.TableName,
                    AutoCreateSqlTable = true
                };

                loggerConfiguration = loggerConfiguration.WriteTo.MSSqlServer(
                    connectionString: _configuration.ConnectionString,
                    sinkOptions: sinkOptions,
                    restrictedToMinimumLevel: ConvertLogLevel(_configuration.MinimumLevel)
                );
            }

            if (_configuration.EnableSeq && !string.IsNullOrEmpty(_configuration.SeqServerUrl))
            {
                loggerConfiguration = loggerConfiguration.WriteTo.Seq(
                    serverUrl: _configuration.SeqServerUrl,
                    apiKey: _configuration.SeqApiKey,
                    restrictedToMinimumLevel: ConvertLogLevel(_configuration.MinimumLevel)
                );
            }

            return loggerConfiguration.CreateLogger();
        }

        private LogEventLevel ConvertLogLevel(LogLevel logLevel)
        {
            return logLevel switch
            {
                LogLevel.Trace => LogEventLevel.Verbose,
                LogLevel.Debug => LogEventLevel.Debug,
                LogLevel.Information => LogEventLevel.Information,
                LogLevel.Warning => LogEventLevel.Warning,
                LogLevel.Error => LogEventLevel.Error,
                LogLevel.Critical => LogEventLevel.Fatal,
                _ => LogEventLevel.Information
            };
        }

        private class SerilogLogger : Microsoft.Extensions.Logging.ILogger
        {
            private readonly Serilog.ILogger _serilogLogger;
            private readonly string _categoryName;
            private readonly Dictionary<string, LogLevel> _logLevels;

            public SerilogLogger(Serilog.ILogger serilogLogger, string categoryName, Dictionary<string, LogLevel> logLevels)
            {
                _serilogLogger = serilogLogger;
                _categoryName = categoryName;
                _logLevels = logLevels;
            }

            public IDisposable? BeginScope<TState>(TState state)
            {
                return LogContext.PushProperty("Scope", state);
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                if (_logLevels.TryGetValue(_categoryName, out var configuredLevel))
                {
                    return logLevel >= configuredLevel;
                }
                return true;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, System.Exception? exception, Func<TState, System.Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                var message = formatter(state, exception);
                var serilogLevel = ConvertLogLevel(logLevel);

                _serilogLogger.Write(
                    serilogLevel,
                    exception,
                    "[{Category}] {Message}",
                    _categoryName,
                    message
                );
            }

            private LogEventLevel ConvertLogLevel(LogLevel logLevel)
            {
                return logLevel switch
                {
                    LogLevel.Trace => LogEventLevel.Verbose,
                    LogLevel.Debug => LogEventLevel.Debug,
                    LogLevel.Information => LogEventLevel.Information,
                    LogLevel.Warning => LogEventLevel.Warning,
                    LogLevel.Error => LogEventLevel.Error,
                    LogLevel.Critical => LogEventLevel.Fatal,
                    _ => LogEventLevel.Information
                };
            }
        }
    }
}