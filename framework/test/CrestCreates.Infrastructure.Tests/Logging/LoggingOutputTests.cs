using System; 
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using FluentAssertions;
using CrestCreates.Infrastructure.Logging;

namespace CrestCreates.Infrastructure.Tests.Logging
{
    public class LoggingOutputTests
    {
        [Fact]
        public void Logger_Should_Log_To_Console()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSerilogLogging(config =>
            {
                config.MinimumLevel = LogLevel.Information;
                config.EnableConsole = true;
                config.EnableFile = false;
            });

            var serviceProvider = services.BuildServiceProvider();
            var logger = serviceProvider.GetRequiredService<ILogger<LoggingOutputTests>>();

            // Act & Assert
            // This test just verifies that logging doesn't throw an exception
            Action act = () => logger.LogInformation("Test log message to console");
            act.Should().NotThrow();
        }

        [Fact]
        public void Logger_Should_Log_To_File()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSerilogLogging(config =>
            {
                config.MinimumLevel = LogLevel.Information;
                config.EnableConsole = false;
                config.EnableFile = true;
                // Use a unique file path to avoid conflicts
                config.FilePath = Path.Combine(Path.GetTempPath(), $"test-log-{Guid.NewGuid()}-.txt");
            });

            var serviceProvider = services.BuildServiceProvider();
            var logger = serviceProvider.GetRequiredService<ILogger<LoggingOutputTests>>();

            // Act & Assert
            // This test just verifies that logging to file doesn't throw an exception
            Action act = () => logger.LogInformation("Test log message to file");
            act.Should().NotThrow();

            // Get and dispose the logger provider
            var loggerProvider = serviceProvider.GetRequiredService<ILoggerProviderAdapter>();
            loggerProvider.Dispose();
        }

        [Fact]
        public void Logger_Should_Log_Different_Log_Levels()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSerilogLogging(config =>
            {
                config.MinimumLevel = LogLevel.Trace;
                config.EnableConsole = true;
                config.EnableFile = false;
            });

            var serviceProvider = services.BuildServiceProvider();
            var logger = serviceProvider.GetRequiredService<ILogger<LoggingOutputTests>>();

            // Act & Assert
            // This test just verifies that logging at different levels doesn't throw an exception
            Action act = () =>
            {
                logger.LogTrace("Trace log message");
                logger.LogDebug("Debug log message");
                logger.LogInformation("Information log message");
                logger.LogWarning("Warning log message");
                logger.LogError("Error log message");
                logger.LogCritical("Critical log message");
            };
            act.Should().NotThrow();
        }
    }
}
