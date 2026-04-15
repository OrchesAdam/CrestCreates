using CrestCreates.Scheduling.Services;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Scheduling.Tests;

public class JobRetryOptionsTests
{
    [Fact]
    public void DefaultOptions_ShouldHaveZeroMaxRetries()
    {
        // Act
        var options = new JobRetryOptions();

        // Assert
        options.MaxRetries.Should().Be(0);
        options.InitialDelay.Should().BeNull();
        options.MaxDelay.Should().BeNull();
        options.BackoffMultiplier.Should().Be(2.0);
    }

    [Fact]
    public void Options_ShouldAllowSettingAllProperties()
    {
        // Arrange & Act
        var options = new JobRetryOptions
        {
            MaxRetries = 5,
            InitialDelay = TimeSpan.FromSeconds(10),
            MaxDelay = TimeSpan.FromMinutes(1),
            BackoffMultiplier = 1.5
        };

        // Assert
        options.MaxRetries.Should().Be(5);
        options.InitialDelay.Should().Be(TimeSpan.FromSeconds(10));
        options.MaxDelay.Should().Be(TimeSpan.FromMinutes(1));
        options.BackoffMultiplier.Should().Be(1.5);
    }

    [Fact]
    public void Options_ShouldUseInitSetters()
    {
        // Arrange
        var options = new JobRetryOptions { MaxRetries = 3 };

        // Assert - init setters allow setting during initialization only
        options.MaxRetries.Should().Be(3);
        options.BackoffMultiplier.Should().Be(2.0); // Default value
    }
}
