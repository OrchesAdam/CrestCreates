using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Xunit;
using FluentAssertions;
using CrestCreates.Infrastructure.Logging;

namespace CrestCreates.Infrastructure.Tests.Logging
{
    public class SerilogLoggerAdapterTests
    {
        [Fact]
        public void CreateLogger_Should_Return_Logger()
        {
            // Arrange
            var configuration = new LoggingConfiguration
            {
                EnableConsole = false,
                EnableFile = false
            };
            var adapter = new SerilogLoggerAdapter(configuration);

            // Act
            var logger = adapter.CreateLogger("TestCategory");

            // Assert
            logger.Should().NotBeNull();
        }

        [Fact]
        public void SetLogLevel_Should_Update_Category_Log_Level()
        {
            // Arrange
            var configuration = new LoggingConfiguration
            {
                EnableConsole = false,
                EnableFile = false
            };
            var adapter = new SerilogLoggerAdapter(configuration);

            // Act
            adapter.SetLogLevel("TestCategory", LogLevel.Debug);
            var logLevel = adapter.GetLogLevel("TestCategory");

            // Assert
            logLevel.Should().Be(LogLevel.Debug);
        }

        [Fact]
        public void GetLogLevel_Should_Return_Global_Level_For_Unconfigured_Category()
        {
            // Arrange
            var configuration = new LoggingConfiguration
            {
                MinimumLevel = LogLevel.Information,
                EnableConsole = false,
                EnableFile = false
            };
            var adapter = new SerilogLoggerAdapter(configuration);

            // Act
            var logLevel = adapter.GetLogLevel("UnconfiguredCategory");

            // Assert
            logLevel.Should().Be(LogLevel.Information);
        }

        [Fact]
        public void SetGlobalLogLevel_Should_Update_Global_Log_Level()
        {
            // Arrange
            var configuration = new LoggingConfiguration
            {
                MinimumLevel = LogLevel.Information,
                EnableConsole = false,
                EnableFile = false
            };
            var adapter = new SerilogLoggerAdapter(configuration);

            // Act
            adapter.SetGlobalLogLevel(LogLevel.Debug);
            var logLevel = adapter.GetGlobalLogLevel();

            // Assert
            logLevel.Should().Be(LogLevel.Debug);
        }

        [Fact]
        public void UpdateConfiguration_Should_Apply_New_Configuration()
        {
            // Arrange
            var originalConfiguration = new LoggingConfiguration
            {
                MinimumLevel = LogLevel.Information,
                EnableConsole = false,
                EnableFile = false
            };
            var adapter = new SerilogLoggerAdapter(originalConfiguration);

            var newConfiguration = new LoggingConfiguration
            {
                MinimumLevel = LogLevel.Debug,
                EnableConsole = true,
                EnableFile = false
            };

            // Act
            adapter.UpdateConfiguration(newConfiguration);
            var logLevel = adapter.GetGlobalLogLevel();

            // Assert
            logLevel.Should().Be(LogLevel.Debug);
        }

        [Fact]
        public void Configure_Should_Rebuild_Logger()
        {
            // Arrange
            var configuration = new LoggingConfiguration
            {
                EnableConsole = false,
                EnableFile = false
            };
            var adapter = new SerilogLoggerAdapter(configuration);

            // Act & Assert
            // This test just verifies that Configure doesn't throw an exception
            Action act = () => adapter.Configure();
            act.Should().NotThrow();
        }
    }
}
