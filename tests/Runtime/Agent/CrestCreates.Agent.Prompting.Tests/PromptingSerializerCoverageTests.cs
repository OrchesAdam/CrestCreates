using System.Reflection;
using System.Text.Json.Serialization;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Agent.Prompting.Abstractions.Json;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Prompting.Tests;

public sealed class PromptingSerializerCoverageTests
{
    [Fact]
    public void AllPublicSerializableTypes_HaveJsonTypeInfoInContext()
    {
        // Get all [JsonSerializable] target types from the context.
        // Uses CustomAttributeData to access the constructor argument
        // since JsonSerializableAttribute does not expose a public Type property.
        var registeredTypes = typeof(AgentPromptingJsonSerializerContext)
            .GetCustomAttributesData()
            .Where(d => d.AttributeType == typeof(JsonSerializableAttribute))
            .Select(d => (Type)d.ConstructorArguments[0].Value!)
            .ToHashSet();

        // Get all public types in the Abstractions assembly that are DTOs
        // (sealed records, sealed classes with init properties, enums)
        var abstractionsAssembly = typeof(AgentPromptTemplateId).Assembly;

        var dtoTypes = abstractionsAssembly.GetTypes()
            .Where(t => t.IsPublic && !t.IsAbstract && !t.IsInterface)
            .Where(t => t.IsSealed || t.IsEnum || t.IsValueType)
            .Where(t => !t.IsGenericTypeDefinition)
            .Where(t => t.Namespace?.StartsWith("CrestCreates.Agent.Prompting.Abstractions") == true)
            .Where(t => t.Name != "AgentPromptingJsonSerializerContext") // the context itself
            .Where(t => !t.IsNested) // exclude nested types
            .ToList();

        var missing = dtoTypes
            .Where(t => !registeredTypes.Contains(t))
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToList();

        missing.Should().BeEmpty(
            $"all public DTO types in Prompting.Abstractions should have a [JsonSerializable] entry. " +
            $"Missing: {string.Join(", ", missing)}");
    }
}
