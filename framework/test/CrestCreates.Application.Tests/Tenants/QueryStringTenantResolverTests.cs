using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.MultiTenancy;
using CrestCreates.MultiTenancy.Resolvers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Tenants;

public class QueryStringTenantResolverTests
{
    private readonly Mock<ITenantRepository> _tenantRepositoryMock;
    private readonly TenantIdentifierNormalizer _normalizer;
    private readonly Mock<ILogger<QueryStringTenantResolver>> _loggerMock;
    private readonly IOptions<MultiTenancyOptions> _options;

    public QueryStringTenantResolverTests()
    {
        _tenantRepositoryMock = new Mock<ITenantRepository>();
        _normalizer = new TenantIdentifierNormalizer();
        _loggerMock = new Mock<ILogger<QueryStringTenantResolver>>();
        _options = Options.Create(new MultiTenancyOptions());
    }

    private QueryStringTenantResolver CreateResolver()
    {
        return new QueryStringTenantResolver(_options, _tenantRepositoryMock.Object, _normalizer, _loggerMock.Object);
    }

    private static DefaultHttpContext CreateHttpContextWithQueryString(string key, string value)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = QueryString.Create(key, value);
        return context;
    }

    [Fact]
    public async Task ResolveAsync_WithValidTenantInQueryString_ReturnsSuccess()
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

        var context = CreateHttpContextWithQueryString("tenantId", "ACME");
        var resolver = CreateResolver();

        // Act
        var result = await resolver.ResolveAsync(context);

        // Assert
        result.IsResolved.Should().BeTrue();
        result.TenantName.Should().Be("ACME");
        result.ResolvedBy.Should().Be("QueryString");
    }

    [Fact]
    public async Task ResolveAsync_WithoutTenantInQueryString_ReturnsNotResolved()
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

    [Fact]
    public async Task ResolveAsync_WithUnknownTenant_ReturnsNotFound()
    {
        // Arrange
        _tenantRepositoryMock
            .Setup(r => r.FindByNameAsync("ACME", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        var context = CreateHttpContextWithQueryString("tenantId", "ACME");
        var resolver = CreateResolver();

        // Act
        var result = await resolver.ResolveAsync(context);

        // Assert
        result.IsResolved.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error.Code.Should().Be("TENANT_NOT_FOUND");
    }
}
