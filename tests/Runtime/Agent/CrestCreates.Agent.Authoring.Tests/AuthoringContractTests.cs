using System.Reflection;
using System.Text.Json;
using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Abstractions.Json;
using CrestCreates.Agent.Authoring.Abstractions.Model;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Authoring.Tests;

public sealed class AuthoringContractTests
{
    [Fact]
    public void Contracts_Are_FrameworkNamespace_NotSampleNamespace()
    {
        typeof(IDescriptorAuthoringAgent).Namespace
            .Should().Be("CrestCreates.Agent.Authoring.Abstractions.Authoring");

        typeof(IDescriptorAuthoringAgent).Assembly.GetName().Name
            .Should().Be("CrestCreates.Agent.Authoring.Abstractions");
    }

    [Fact]
    public void DescriptorAuthoringStatus_ContainsAllRequiredValues()
    {
        var names = Enum.GetNames<DescriptorAuthoringStatus>();
        names.Should().Contain("Succeeded");
        names.Should().Contain("SucceededWithDiagnostics");
        names.Should().Contain("Blocked");
        names.Should().Contain("InvalidProviderOutput");
        names.Should().Contain("ProviderUnavailable");
        names.Should().Contain("Failed");
    }

    [Fact]
    public void ProviderProfile_DoesNotExpose_Secrets()
    {
        typeof(DescriptorAuthoringProviderProfile)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(p => p.Name)
            .Should()
            .NotContain(new[] { "ApiKey", "Secret", "Token", "Password" });
    }

    [Fact]
    public void JsonContext_ContainsAuthoringModelProfile()
    {
        var json = JsonSerializer.Serialize(
            new DescriptorAuthoringModelProfile
            {
                ProfileName = "fixture",
                ProviderName = "recorded",
                ModelName = "fixture-model"
            },
            DescriptorAuthoringJsonSerializerContext.Default.DescriptorAuthoringModelProfile);

        json.Should().Contain("\"profileName\":\"fixture\"");
    }
}
