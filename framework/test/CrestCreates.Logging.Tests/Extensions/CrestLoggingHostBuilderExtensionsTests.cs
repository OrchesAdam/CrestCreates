using CrestCreates.Logging.Extensions;
using CrestCreates.Logging.Options;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Xunit;

namespace CrestCreates.Logging.Tests.Extensions;

public class CrestLoggingHostBuilderExtensionsTests
{
    [Fact]
    public void BuildOptions_ShouldUseApplicationNameFallback()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection()
            .Build();

        var options = CrestLoggingHostBuilderExtensions.BuildOptions(configuration, "Fallback.App");

        options.ApplicationName.Should().Be("Fallback.App");
    }

    [Fact]
    public void ConvertLogLevel_ShouldMapCriticalToFatal()
    {
        var result = CrestLoggingHostBuilderExtensions.ConvertLogLevel(LogLevel.Critical);

        result.Should().Be(Serilog.Events.LogEventLevel.Fatal);
    }

    [Fact]
    public void ConfigureLogger_ShouldWriteToConfiguredFile()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"crest-logging-{Guid.NewGuid()}-.txt");
        var options = new CrestLoggingOptions
        {
            ApplicationName = "Logging.Tests",
            EnableConsole = false,
            EnableFile = true,
            FilePath = tempFile,
            MinimumLevel = LogLevel.Information
        };

        var logger = CrestLoggingHostBuilderExtensions
            .ConfigureLogger(new LoggerConfiguration(), options, "Test")
            .CreateLogger();

        logger.Information("Hello from logging tests");
        Log.CloseAndFlush();
        (logger as IDisposable)?.Dispose();

        var createdFile = Directory.GetFiles(Path.GetDirectoryName(tempFile)!, Path.GetFileName(tempFile).Replace("-.txt", "*.txt"))
            .Single();

        File.ReadAllText(createdFile).Should().Contain("Hello from logging tests");
    }
}
