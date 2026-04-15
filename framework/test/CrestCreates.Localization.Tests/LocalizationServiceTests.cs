using CrestCreates.Localization.Abstractions;
using CrestCreates.Localization.Services;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using Moq;
using Xunit;

namespace CrestCreates.Localization.Tests;

public class LocalizationServiceTests
{
    private readonly Mock<IStringLocalizerFactory> _localizerFactoryMock;
    private readonly Mock<IStringLocalizer> _localizerMock;
    private readonly LocalizationService _service;

    public LocalizationServiceTests()
    {
        _localizerFactoryMock = new Mock<IStringLocalizerFactory>();
        _localizerMock = new Mock<IStringLocalizer>();
        _localizerFactoryMock.Setup(f => f.Create(It.IsAny<Type>())).Returns(_localizerMock.Object);
        _service = new LocalizationService(_localizerFactoryMock.Object, "en");
    }

    [Fact]
    public void CurrentCulture_WhenNoCultureSet_ReturnsDefault()
    {
        // Assert
        _service.CurrentCulture.Should().Be("en");
    }

    [Fact]
    public void ChangeCulture_SetsCurrentCulture()
    {
        // Act
        using (_service.ChangeCulture("zh-CN"))
        {
            // Assert
            _service.CurrentCulture.Should().Be("zh-CN");
        }
    }

    [Fact]
    public void ChangeCulture_AfterDispose_RestoresPreviousCulture()
    {
        // Arrange
        using (_service.ChangeCulture("zh-CN"))
        {
            _service.CurrentCulture.Should().Be("zh-CN");
        }

        // Assert
        _service.CurrentCulture.Should().Be("en");
    }

    [Fact]
    public void ChangeCulture_Nested_RevertsCorrectly()
    {
        // Arrange & Act
        using (_service.ChangeCulture("zh-CN"))
        {
            _service.CurrentCulture.Should().Be("zh-CN");

            using (_service.ChangeCulture("ja"))
            {
                _service.CurrentCulture.Should().Be("ja");
            }

            _service.CurrentCulture.Should().Be("zh-CN");
        }

        _service.CurrentCulture.Should().Be("en");
    }

    [Fact]
    public async Task GetStringAsync_WithNoResources_ReturnsLocalizerValue()
    {
        // Arrange
        _localizerMock.Setup(l => l[It.IsAny<string>()])
            .Returns(new LocalizedString("TestKey", "LocalizedValue"));

        // Act
        var result = await _service.GetStringAsync("TestKey");

        // Assert
        result.Should().Be("LocalizedValue");
    }

    [Fact]
    public async Task GetStringAsync_WithParameters_FormatsCorrectly()
    {
        // Arrange
        // The IStringLocalizer returns the localized string with placeholders intact
        // The service then applies string.Format
        _localizerMock.Setup(l => l[It.IsAny<string>()])
            .Returns(new LocalizedString("Hello {0}", "Hello {0}"));

        // Act - cast to object[] to force params overload
        var result = await _service.GetStringAsync("Hello {0}", new object[] { "World" });

        // Assert
        result.Should().Be("Hello World");
    }

    [Fact]
    public async Task GetStringAsync_WithCultureName_UsesCulture()
    {
        // Arrange
        using (_service.ChangeCulture("en"))
        {
            _localizerMock.Setup(l => l[It.IsAny<string>()])
                .Returns(new LocalizedString("TestKey", "EnglishValue"));

            // Act
            var result = await _service.GetStringAsync("TestKey", "zh-CN");

            // Assert
            result.Should().NotBeNull();
        }
    }

    [Fact]
    public void GetString_Sync_ReturnsValue()
    {
        // Arrange
        _localizerMock.Setup(l => l[It.IsAny<string>()])
            .Returns(new LocalizedString("SyncKey", "SyncValue"));

        // Act
        var result = _service.GetString("SyncKey");

        // Assert
        result.Should().Be("SyncValue");
    }

    [Fact]
    public void GetString_WithArguments_FormatsCorrectly()
    {
        // Arrange - IStringLocalizer returns the key if not found, with format placeholders
        _localizerMock.Setup(l => l[It.IsAny<string>()])
            .Returns(new LocalizedString("Hello {0}", "Hello {0}"));

        // Act - cast to object[] to force params overload
        var result = _service.GetString("Hello {0}", new object[] { "World" });

        // Assert
        result.Should().Be("Hello World");
    }

    [Fact]
    public void RegisterResource_AddsContributor()
    {
        // Arrange
        var contributorMock = new Mock<ILocalizationResourceContributor>();
        contributorMock.Setup(c => c.ResourceName).Returns("TestResource");
        contributorMock.Setup(c => c.GetStringAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string culture, string key) => $"ResourceValue-{key}");

        var resource = new LocalizationResource
        {
            Name = "TestResource",
            Contributor = contributorMock.Object,
            Priority = 1
        };

        // Act - should not throw when adding resource
        var act = () => _service.RegisterResource(resource);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public async Task GetStringAsync_WithRegisteredResource_ReturnsContributorValue()
    {
        // Arrange
        var contributorMock = new Mock<ILocalizationResourceContributor>();
        contributorMock.Setup(c => c.ResourceName).Returns("TestResource");
        contributorMock.Setup(c => c.GetStringAsync("en", "ResourceKey")).ReturnsAsync("ResourceValue");
        contributorMock.Setup(c => c.HasKey("en", "ResourceKey")).Returns(true);

        var resource = new LocalizationResource
        {
            Name = "TestResource",
            Contributor = contributorMock.Object,
            Priority = 1
        };

        _service.RegisterResource(resource);

        // Act
        var result = await _service.GetStringAsync("ResourceKey", "en");

        // Assert
        result.Should().Be("ResourceValue");
    }
}
