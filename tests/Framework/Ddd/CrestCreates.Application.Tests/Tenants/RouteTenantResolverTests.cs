using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.MultiTenancy;
using CrestCreates.MultiTenancy.Resolvers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Tenants;

public class RouteTenantResolverTests
{
    private readonly Mock<ITenantRepository> _tenantRepositoryMock;
    private readonly TenantIdentifierNormalizer _normalizer;
    private readonly Mock<ILogger<RouteTenantResolver>> _loggerMock;
    private readonly IOptions<MultiTenancyOptions> _options;

    public RouteTenantResolverTests()
    {
        _tenantRepositoryMock = new Mock<ITenantRepository>();
        _normalizer = new TenantIdentifierNormalizer();
        _loggerMock = new Mock<ILogger<RouteTenantResolver>>();
        _options = Options.Create(new MultiTenancyOptions());
    }

    private RouteTenantResolver CreateResolver()
    {
        return new RouteTenantResolver(_options, _tenantRepositoryMock.Object, _normalizer, _loggerMock.Object);
    }

    private static DefaultHttpContext CreateHttpContextWithRouteData(string key, string value)
    {
        var context = new DefaultHttpContext();

        var routeData = new RouteData();
        routeData.Values[key] = value;

        var routingFeatureMock = new Mock<IRoutingFeature>();
        routingFeatureMock.SetupGet(f => f.RouteData).Returns(routeData);

        context.Features.Set<IRoutingFeature>(routingFeatureMock.Object);

        return context;
    }

    [Fact]
    public async Task ResolveAsync_WithValidTenantInRoute_ReturnsSuccess()
    {
        // Arrange
        var tenant = new Tenant(Guid.NewGuid(), "ACME")
        {
            IsActive = true,
            LifecycleState = TenantLifecycleState.Active
        };
        _tenantRepositoryMock
            .Setup(r => r.FindByNameAsync("ACME", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        var context = CreateHttpContextWithRouteData("tenantId", "ACME");
        var resolver = CreateResolver();

        // Act
        var result = await resolver.ResolveAsync(context);

        // Assert
        result.IsResolved.Should().BeTrue();
        result.TenantName.Should().Be("ACME");
        result.ResolvedBy.Should().Be("Route");
    }

    [Fact]
    public async Task ResolveAsync_WithoutTenantRouteValue_ReturnsNotResolved()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var resolver = CreateResolver();

        // Act
        var result = await resolver.ResolveAsync(context);

        // Assert
        result.IsResolved.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error.Code.Should().Be("TENANT_NOT_RESOLVED");
    }
}
