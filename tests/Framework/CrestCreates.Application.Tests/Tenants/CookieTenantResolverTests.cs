using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.MultiTenancy;
using CrestCreates.MultiTenancy.Resolvers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Tenants;

public class CookieTenantResolverTests
{
    private readonly Mock<ITenantRepository> _tenantRepositoryMock;
    private readonly TenantIdentifierNormalizer _normalizer;
    private readonly Mock<ILogger<CookieTenantResolver>> _loggerMock;
    private readonly IOptions<MultiTenancyOptions> _options;

    public CookieTenantResolverTests()
    {
        _tenantRepositoryMock = new Mock<ITenantRepository>();
        _normalizer = new TenantIdentifierNormalizer();
        _loggerMock = new Mock<ILogger<CookieTenantResolver>>();
        _options = Options.Create(new MultiTenancyOptions());
    }

    private CookieTenantResolver CreateResolver()
    {
        return new CookieTenantResolver(_options, _tenantRepositoryMock.Object, _normalizer, _loggerMock.Object);
    }

    private static DefaultHttpContext CreateHttpContextWithCookie(string name, string value)
    {
        var context = new DefaultHttpContext();

        // DefaultHttpContext.Request.Cookies is an IRequestCookieCollection backed by
        // the IRequestCookiesFeature. We set up a feature that returns our desired cookies.
        var cookiesMock = new Mock<IRequestCookieCollection>();
        cookiesMock
            .Setup(c => c.TryGetValue(name, out value))
            .Returns(true);
        cookiesMock
            .Setup(c => c[name])
            .Returns(value);

        var cookiesFeatureMock = new Mock<IRequestCookiesFeature>();
        cookiesFeatureMock.SetupGet(f => f.Cookies).Returns(cookiesMock.Object);

        context.Features.Set(cookiesFeatureMock.Object);

        return context;
    }

    [Fact]
    public async Task ResolveAsync_WithValidTenantInCookie_ReturnsSuccess()
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

        var context = CreateHttpContextWithCookie("TenantId", "ACME");
        var resolver = CreateResolver();

        // Act
        var result = await resolver.ResolveAsync(context);

        // Assert
        result.IsResolved.Should().BeTrue();
        result.TenantName.Should().Be("ACME");
        result.ResolvedBy.Should().Be("Cookie");
    }

    [Fact]
    public async Task ResolveAsync_WithoutTenantCookie_ReturnsNotResolved()
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
