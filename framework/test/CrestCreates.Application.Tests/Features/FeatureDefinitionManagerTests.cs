using CrestCreates.Domain.Features;
using CrestCreates.Domain.Shared.Features;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Application.Tests.Features;

public class FeatureDefinitionManagerTests
{
    private readonly IFeatureDefinitionManager _featureDefinitionManager;

    public FeatureDefinitionManagerTests()
    {
        _featureDefinitionManager = new FeatureDefinitionManager(
            [new CoreFeatureDefinitionProvider()]);
    }

    [Fact]
    public void GetAll_ShouldReturnAllDefinitions()
    {
        var definitions = _featureDefinitionManager.GetAll();

        definitions.Should().NotBeEmpty();
        definitions.Should().Contain(d => d.Name == "Identity.UserCreationEnabled");
        definitions.Should().Contain(d => d.Name == "FileManagement.Enabled");
        definitions.Should().Contain(d => d.Name == "AuditLogging.Enabled");
        definitions.Should().Contain(d => d.Name == "Storage.MaxFileCount");
        definitions.Should().Contain(d => d.Name == "Ui.Theme");
    }

    [Fact]
    public void GetOrNull_WithValidName_ShouldReturnDefinition()
    {
        var definition = _featureDefinitionManager.GetOrNull("Identity.UserCreationEnabled");

        definition.Should().NotBeNull();
        definition!.Name.Should().Be("Identity.UserCreationEnabled");
    }

    [Fact]
    public void GetOrNull_WithInvalidName_ShouldReturnNull()
    {
        var definition = _featureDefinitionManager.GetOrNull("Unknown.Feature");

        definition.Should().BeNull();
    }

    [Fact]
    public void GetGroups_ShouldReturnAllGroups()
    {
        var groups = _featureDefinitionManager.GetGroups();

        groups.Should().NotBeEmpty();
        groups.Should().Contain(g => g.Name == "Identity");
        groups.Should().Contain(g => g.Name == "FileManagement");
        groups.Should().Contain(g => g.Name == "AuditLogging");
        groups.Should().Contain(g => g.Name == "Storage");
        groups.Should().Contain(g => g.Name == "UI");
    }

    [Fact]
    public void FeatureDefinition_ShouldSupportCorrectScopes()
    {
        var definition = _featureDefinitionManager.GetOrNull("Identity.UserCreationEnabled");

        definition.Should().NotBeNull();
        definition!.SupportsScope(FeatureScope.Global).Should().BeTrue();
        definition.SupportsScope(FeatureScope.Tenant).Should().BeTrue();
    }

    [Fact]
    public void FeatureDefinition_ShouldHaveCorrectValueTypes()
    {
        var boolFeature = _featureDefinitionManager.GetOrNull("Identity.UserCreationEnabled");
        var intFeature = _featureDefinitionManager.GetOrNull("Storage.MaxFileCount");
        var stringFeature = _featureDefinitionManager.GetOrNull("Ui.Theme");

        boolFeature!.ValueType.Should().Be(FeatureValueType.Bool);
        intFeature!.ValueType.Should().Be(FeatureValueType.Int);
        stringFeature!.ValueType.Should().Be(FeatureValueType.String);
    }

    [Fact]
    public void FeatureDefinition_ShouldHaveCorrectDefaultValues()
    {
        var boolFeature = _featureDefinitionManager.GetOrNull("Identity.UserCreationEnabled");
        var intFeature = _featureDefinitionManager.GetOrNull("Storage.MaxFileCount");
        var stringFeature = _featureDefinitionManager.GetOrNull("Ui.Theme");

        boolFeature!.GetNormalizedDefaultValue().Should().Be("true");
        intFeature!.GetNormalizedDefaultValue().Should().Be("100");
        stringFeature!.GetNormalizedDefaultValue().Should().Be("Default");
    }

    [Fact]
    public void FeatureDefinition_ShouldBeCaseInsensitive()
    {
        var definition1 = _featureDefinitionManager.GetOrNull("identity.usercreationenabled");
        var definition2 = _featureDefinitionManager.GetOrNull("IDENTITY.USERCREATIONENABLED");

        definition1.Should().NotBeNull();
        definition2.Should().NotBeNull();
        definition1!.Name.Should().Be(definition2!.Name);
    }

    [Fact]
    public void GetAll_ShouldReturnDefinitionsOrderedByName()
    {
        var definitions = _featureDefinitionManager.GetAll();

        definitions.Should().BeInAscendingOrder(d => d.Name);
    }
}
