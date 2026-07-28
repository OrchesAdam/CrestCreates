using CrestCreates.Capability.Abstractions;
using CrestCreates.DynamicApi;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace CrestCreates.DynamicApi.Tests;

public class CapabilityEndpointResultMapperTests
{
    [Fact]
    public void Map_SucceededWithOutput_ReturnsOkObjectResult()
    {
        // Arrange
        var output = new { Name = "test" };
        var result = CapabilityExecutionResult.Success(output, TimeSpan.FromMilliseconds(100));
        var mapping = new CapabilityEndpointOutputMapping();

        // Act
        var httpResult = CapabilityEndpointResultMapper.Map(result, mapping);

        // Assert
        httpResult.Should().NotBeNull();
    }

    [Fact]
    public void Map_SucceededWith201_ReturnsJsonWithStatusCode()
    {
        // Arrange
        var output = new { Id = 1 };
        var result = CapabilityExecutionResult.Success(output, TimeSpan.FromMilliseconds(100));
        var mapping = new CapabilityEndpointOutputMapping { SuccessStatusCode = 201 };

        // Act
        var httpResult = CapabilityEndpointResultMapper.Map(result, mapping);

        // Assert
        httpResult.Should().NotBeNull();
    }

    [Fact]
    public void Map_SucceededWithNullOutput_ReturnsStatusCodeResult()
    {
        // Arrange
        var result = CapabilityExecutionResult.Success(null, TimeSpan.FromMilliseconds(100));
        var mapping = new CapabilityEndpointOutputMapping { SuccessStatusCode = 204 };

        // Act
        var httpResult = CapabilityEndpointResultMapper.Map(result, mapping);

        // Assert
        httpResult.Should().NotBeNull();
    }

    [Fact]
    public void Map_Failed_ReturnsProblemResult()
    {
        // Arrange
        var result = CapabilityExecutionResult.Failure(
            "SOME_ERROR",
            "Something went wrong",
            TimeSpan.FromMilliseconds(100));
        var mapping = new CapabilityEndpointOutputMapping();

        // Act
        var httpResult = CapabilityEndpointResultMapper.Map(result, mapping);

        // Assert
        httpResult.Should().NotBeNull();
    }

    [Fact]
    public void Map_TimedOut_Returns504()
    {
        // Arrange
        var result = new CapabilityExecutionResult
        {
            Status = CapabilityExecutionStatus.TimedOut,
            Duration = TimeSpan.FromSeconds(30)
        };
        var mapping = new CapabilityEndpointOutputMapping();

        // Act
        var httpResult = CapabilityEndpointResultMapper.Map(result, mapping);

        // Assert
        httpResult.Should().NotBeNull();
    }

    [Fact]
    public void Map_Compensated_Returns409()
    {
        // Arrange
        var result = new CapabilityExecutionResult
        {
            Status = CapabilityExecutionStatus.Compensated,
            Duration = TimeSpan.FromMilliseconds(200)
        };
        var mapping = new CapabilityEndpointOutputMapping();

        // Act
        var httpResult = CapabilityEndpointResultMapper.Map(result, mapping);

        // Assert
        httpResult.Should().NotBeNull();
    }

    [Fact]
    public void Map_UnauthorizedErrorCode_ReturnsForbid()
    {
        // Arrange
        var result = CapabilityExecutionResult.Failure(
            "UNAUTHORIZED",
            "User is not authorized",
            TimeSpan.FromMilliseconds(50));
        var mapping = new CapabilityEndpointOutputMapping();

        // Act
        var httpResult = CapabilityEndpointResultMapper.Map(result, mapping);

        // Assert
        httpResult.Should().NotBeNull();
    }

    [Fact]
    public void Map_CapabilityNotFound_ReturnsNotFound()
    {
        // Arrange
        var result = CapabilityExecutionResult.Failure(
            "CAPABILITY_NOT_FOUND",
            "Capability was not found",
            TimeSpan.FromMilliseconds(50));
        var mapping = new CapabilityEndpointOutputMapping();

        // Act
        var httpResult = CapabilityEndpointResultMapper.Map(result, mapping);

        // Assert
        httpResult.Should().NotBeNull();
    }

    [Fact]
    public void Map_CapabilityValidationFailed_ReturnsValidationProblem()
    {
        // Arrange
        var result = CapabilityExecutionResult.Failure(
            "CAPABILITY_VALIDATION_FAILED",
            "Validation failed",
            TimeSpan.FromMilliseconds(50));
        var mapping = new CapabilityEndpointOutputMapping();

        // Act
        var httpResult = CapabilityEndpointResultMapper.Map(result, mapping);

        // Assert
        httpResult.Should().NotBeNull();
    }

    [Fact]
    public void Map_CapabilityDependencyUnavailable_Returns503()
    {
        var result = CapabilityExecutionResult.Failure(
            "CAPABILITY_DEPENDENCY_UNAVAILABLE",
            "A required dependency is unavailable",
            TimeSpan.FromMilliseconds(50));
        var httpResult = CapabilityEndpointResultMapper.Map(
            result,
            new CapabilityEndpointOutputMapping());
        httpResult.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }
}
