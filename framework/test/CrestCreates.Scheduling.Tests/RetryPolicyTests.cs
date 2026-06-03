using System;
using CrestCreates.Scheduling.Services;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Scheduling.Tests;

public class RetryPolicyTests
{
    private static readonly Exception TestException = new("Test error");

    [Fact]
    public void ExponentialBackoff_GetDelay_DoublesEachAttempt()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy(maxRetries: 5, baseDelay: TimeSpan.FromSeconds(1));

        // Act
        var delay0 = policy.GetDelay(0);
        var delay1 = policy.GetDelay(1);
        var delay2 = policy.GetDelay(2);
        var delay3 = policy.GetDelay(3);

        // Assert
        delay0.Should().Be(TimeSpan.FromSeconds(1));  // 1 * 2^0 = 1
        delay1.Should().Be(TimeSpan.FromSeconds(2));  // 1 * 2^1 = 2
        delay2.Should().Be(TimeSpan.FromSeconds(4));  // 1 * 2^2 = 4
        delay3.Should().Be(TimeSpan.FromSeconds(8));  // 1 * 2^3 = 8
    }

    [Fact]
    public void ExponentialBackoff_GetDelay_DoesNotExceedMaxDelay()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy(
            maxRetries: 10,
            baseDelay: TimeSpan.FromSeconds(1),
            maxDelay: TimeSpan.FromSeconds(10));

        // Act
        var delay = policy.GetDelay(10); // 2^10 = 1024 seconds, far exceeds max

        // Assert
        delay.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void ExponentialBackoff_ShouldRetry_ReturnsFalseAfterMaxRetries()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy(maxRetries: 3);

        // Act & Assert
        policy.ShouldRetry(3, TestException).Should().BeFalse();
        policy.ShouldRetry(4, TestException).Should().BeFalse();
    }

    [Fact]
    public void ExponentialBackoff_ShouldRetry_ReturnsTrueBeforeMaxRetries()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy(maxRetries: 3);

        // Act & Assert
        policy.ShouldRetry(0, TestException).Should().BeTrue();
        policy.ShouldRetry(1, TestException).Should().BeTrue();
        policy.ShouldRetry(2, TestException).Should().BeTrue();
    }

    [Fact]
    public void FixedDelay_GetDelay_ReturnsSameDelay()
    {
        // Arrange
        var policy = new FixedDelayRetryPolicy(maxRetries: 5, delay: TimeSpan.FromSeconds(30));

        // Act & Assert
        policy.GetDelay(0).Should().Be(TimeSpan.FromSeconds(30));
        policy.GetDelay(1).Should().Be(TimeSpan.FromSeconds(30));
        policy.GetDelay(2).Should().Be(TimeSpan.FromSeconds(30));
        policy.GetDelay(4).Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void FixedDelay_ShouldRetry_ReturnsFalseAfterMaxRetries()
    {
        // Arrange
        var policy = new FixedDelayRetryPolicy(maxRetries: 3);

        // Act & Assert
        policy.ShouldRetry(3, TestException).Should().BeFalse();
        policy.ShouldRetry(5, TestException).Should().BeFalse();
    }

    [Fact]
    public void NoRetryPolicy_ShouldRetry_ReturnsFalse()
    {
        // Arrange
        var policy = new NoRetryPolicy();

        // Act & Assert
        policy.ShouldRetry(0, TestException).Should().BeFalse();
        policy.ShouldRetry(1, TestException).Should().BeFalse();
        policy.MaxRetries.Should().Be(0);
        policy.GetDelay(0).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void ExponentialBackoff_MaxRetries_ReturnsConfiguredValue()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy(maxRetries: 5);

        // Assert
        policy.MaxRetries.Should().Be(5);
    }

    [Fact]
    public void FixedDelay_MaxRetries_ReturnsConfiguredValue()
    {
        // Arrange
        var policy = new FixedDelayRetryPolicy(maxRetries: 7);

        // Assert
        policy.MaxRetries.Should().Be(7);
    }

    [Fact]
    public void ExponentialBackoff_DefaultValues_AreCorrect()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy();

        // Assert
        policy.MaxRetries.Should().Be(3);
        policy.GetDelay(0).Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void FixedDelay_DefaultValues_AreCorrect()
    {
        // Arrange
        var policy = new FixedDelayRetryPolicy();

        // Assert
        policy.MaxRetries.Should().Be(3);
        policy.GetDelay(0).Should().Be(TimeSpan.FromSeconds(30));
    }
}
