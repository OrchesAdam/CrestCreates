using CrestCreates.Capability.Abstractions;
using CrestCreates.DynamicApi;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
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

    [Theory]
    [InlineData(CapabilityFailureKind.InvalidRequest, StatusCodes.Status400BadRequest)]
    [InlineData(CapabilityFailureKind.ValidationFailed, StatusCodes.Status400BadRequest)]
    [InlineData(CapabilityFailureKind.Unauthorized, StatusCodes.Status401Unauthorized)]
    [InlineData(CapabilityFailureKind.Forbidden, StatusCodes.Status403Forbidden)]
    [InlineData(CapabilityFailureKind.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(CapabilityFailureKind.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(CapabilityFailureKind.PreconditionRequired, StatusCodes.Status428PreconditionRequired)]
    [InlineData(CapabilityFailureKind.Cancelled, StatusCodes.Status499ClientClosedRequest)]
    [InlineData(CapabilityFailureKind.ManagedDeadline, StatusCodes.Status504GatewayTimeout)]
    [InlineData(CapabilityFailureKind.NonCaller, StatusCodes.Status500InternalServerError)]
    [InlineData(CapabilityFailureKind.Unknown, StatusCodes.Status500InternalServerError)]
    public void Map_FailureFacts_ReturnsClassifiedStatus(CapabilityFailureKind kind, int expectedStatusCode)
    {
        var result = CapabilityExecutionResult.Failure(
            "ASSET_MAINTENANCE_TASK_MISMATCH",
            "The maintenance task belongs to another asset.",
            TimeSpan.Zero,
            failureFacts: new CapabilityFailureFacts(kind, "ASSET_MAINTENANCE_TASK_MISMATCH", "The maintenance task belongs to another asset."));

        var httpResult = CapabilityEndpointResultMapper.Map(
            result, new CapabilityEndpointOutputMapping(), new DefaultHttpContext());

        httpResult.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(expectedStatusCode);
    }

    [Fact]
    public void Map_UnknownFailureWithoutFacts_DoesNotExposeRawMessage()
    {
        var result = CapabilityExecutionResult.Failure(
            "HANDLER_NOT_FOUND",
            "raw internal capability detail",
            TimeSpan.Zero);

        var httpResult = CapabilityEndpointResultMapper.Map(result, new CapabilityEndpointOutputMapping());

        var problem = httpResult.Should().BeAssignableTo<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Which.ProblemDetails;
        problem.Status.Should().Be(StatusCodes.Status500InternalServerError);
        problem.Detail.Should().Be("服务器内部错误。");
        problem.Extensions["errorCode"].Should().Be("HANDLER_NOT_FOUND");
    }

    [Theory]
    [InlineData(CapabilityFailureKind.Unknown)]
    [InlineData(CapabilityFailureKind.NonCaller)]
    public void Map_InternalFailureFacts_ReturnsSanitizedProblemDetails(CapabilityFailureKind kind)
    {
        var result = CapabilityExecutionResult.Failure(
            "PIPELINE_ERROR",
            "raw persistence detail",
            TimeSpan.Zero,
            failureFacts: new CapabilityFailureFacts(kind, "Crest.InternalError", "服务器内部错误。"));

        var httpResult = CapabilityEndpointResultMapper.Map(
            result, new CapabilityEndpointOutputMapping(), new DefaultHttpContext());

        var problem = httpResult.Should().BeAssignableTo<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Which.ProblemDetails;
        problem.Status.Should().Be(StatusCodes.Status500InternalServerError);
        problem.Detail.Should().Be("服务器内部错误。");
        problem.Extensions["errorCode"].Should().Be("Crest.InternalError");
    }

    [Fact]
    public void Map_CancelledExecution_Returns499()
    {
        var result = CapabilityExecutionResult.Cancelled(TimeSpan.Zero);

        var httpResult = CapabilityEndpointResultMapper.Map(result, new CapabilityEndpointOutputMapping(), new DefaultHttpContext());

        httpResult.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status499ClientClosedRequest);
    }

    [Fact]
    public void Map_AuthenticatedUnauthorizedFacts_ReturnsForbidden()
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([], "test"));
        var result = CapabilityExecutionResult.Failure(
            "UNAUTHORIZED", "Unauthorized", TimeSpan.Zero,
            failureFacts: new CapabilityFailureFacts(CapabilityFailureKind.Unauthorized, "Crest.Auth.Unauthorized", "Unauthorized"));

        var httpResult = CapabilityEndpointResultMapper.Map(result, new CapabilityEndpointOutputMapping(), context);

        httpResult.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
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
