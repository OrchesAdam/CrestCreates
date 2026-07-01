using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Abstractions.Model;
using CrestCreates.Agent.Authoring.Http.OpenAICompatible;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Authoring.Tests;

public sealed class ProviderBoundaryTests
{
    [Fact]
    public void Abstractions_DoNotContain_CredentialProvider()
    {
        typeof(IDescriptorAuthoringModelClient).Assembly
            .GetTypes()
            .Should()
            .NotContain(t => t.Name.Contains("Credential"));
    }

    [Fact]
    public void AuthoringCore_DoesNotReference_HttpProvider()
    {
        // The authoring core assembly should NOT reference the Http provider
        typeof(CrestCreates.Agent.Authoring.Prompting.DefaultDescriptorAuthoringPromptInputFactory).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name)
            .Should()
            .NotContain("CrestCreates.Agent.Authoring.Http");
    }

    [Fact]
    public void HttpProvider_References_OnlyAbstractions()
    {
        typeof(OpenAICompatibleDescriptorAuthoringModelClient).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name)
            .Should()
            .Contain("CrestCreates.Agent.Authoring.Abstractions");

        typeof(OpenAICompatibleDescriptorAuthoringModelClient).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name)
            .Should()
            .NotContain("CrestCreates.Agent.Authoring");
    }
}
