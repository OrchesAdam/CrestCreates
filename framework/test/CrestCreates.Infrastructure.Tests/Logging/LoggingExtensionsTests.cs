using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using FluentAssertions;
using CrestCreates.Infrastructure.Logging;

namespace CrestCreates.Infrastructure.Tests.Logging
{
    public class LoggingExtensionsTests
    {
        [Fact]
        public void AddSerilogLogging_With_Action_Should_Register_Services()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act
            services.AddSerilogLogging(config =>
            {
                config.MinimumLevel = LogLevel.Information;
                config.EnableConsole = true;
                config.EnableFile = false;
            });

            var serviceProvider = services.BuildServiceProvider();

            // Assert
            var configuration = serviceProvider.GetRequiredService<LoggingConfiguration>();
            configuration.Should().NotBeNull();
            configuration.MinimumLevel.Should().Be(LogLevel.Information);
            configuration.EnableConsole.Should().BeTrue();
            configuration.EnableFile.Should().BeFalse();

            var loggerProvider = serviceProvider.GetRequiredService<ILoggerProviderAdapter>();
            loggerProvider.Should().NotBeNull();

            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            loggerFactory.Should().NotBeNull();

            var logger = loggerFactory.CreateLogger<LoggingExtensionsTests>();
            logger.Should().NotBeNull();
        }

        [Fact]
        public void AddSerilogLogging_With_Configuration_Should_Register_Services()
        {
            // Arrange
            var services = new ServiceCollection();
            var configuration = new LoggingConfiguration
            {
                MinimumLevel = LogLevel.Debug,
                EnableConsole = false,
                EnableFile = true,
                FilePath = "logs/test-.txt"
            };

            // Act
            services.AddSerilogLogging(configuration);

            var serviceProvider = services.BuildServiceProvider();

            // Assert
            var registeredConfiguration = serviceProvider.GetRequiredService<LoggingConfiguration>();
            registeredConfiguration.Should().NotBeNull();
            registeredConfiguration.MinimumLevel.Should().Be(LogLevel.Debug);
            registeredConfiguration.EnableConsole.Should().BeFalse();
            registeredConfiguration.EnableFile.Should().BeTrue();
            registeredConfiguration.FilePath.Should().Be("logs/test-.txt");

            var loggerProvider = serviceProvider.GetRequiredService<ILoggerProviderAdapter>();
            loggerProvider.Should().NotBeNull();

            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            loggerFactory.Should().NotBeNull();

            var logger = loggerFactory.CreateLogger<LoggingExtensionsTests>();
            logger.Should().NotBeNull();
        }
    }
}
