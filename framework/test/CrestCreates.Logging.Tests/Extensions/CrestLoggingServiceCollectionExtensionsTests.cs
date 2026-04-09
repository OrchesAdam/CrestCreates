using CrestCreates.Logging.Extensions;
using CrestCreates.Logging.Options;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace CrestCreates.Logging.Tests.Extensions;

public class CrestLoggingServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCrestLogging_WithConfiguration_BindsOptionsAndHttpContextAccessor()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{CrestLoggingOptions.SectionName}:MinimumLevel"] = "Debug",
                [$"{CrestLoggingOptions.SectionName}:EnableConsole"] = "false",
                [$"{CrestLoggingOptions.SectionName}:FilePath"] = "logs/test-.txt"
            })
            .Build();

        var services = new ServiceCollection();

        services.AddCrestLogging(configuration);

        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<CrestLoggingOptions>>().Value;

        options.MinimumLevel.Should().Be(Microsoft.Extensions.Logging.LogLevel.Debug);
        options.EnableConsole.Should().BeFalse();
        options.FilePath.Should().Be("logs/test-.txt");
        serviceProvider.GetRequiredService<IHttpContextAccessor>().Should().NotBeNull();
    }

    [Fact]
    public void AddCrestLogging_WithAction_ConfiguresOptions()
    {
        var services = new ServiceCollection();

        services.AddCrestLogging(options =>
        {
            options.ApplicationName = "Logging.Tests";
            options.EnableFile = false;
        });

        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<CrestLoggingOptions>>().Value;

        options.ApplicationName.Should().Be("Logging.Tests");
        options.EnableFile.Should().BeFalse();
    }
}
