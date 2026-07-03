using System.Reflection;
using System.Text.Json.Serialization;
using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Abstractions.Json;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Authoring.Tests;

public sealed class AuthoringSerializerCoverageTests
{
    [Fact]
    public void AllPublicSerializableTypes_HaveJsonTypeInfoInContext()
    {
        // Get all [JsonSerializable] target types from the context
        var registeredTypes = typeof(DescriptorAuthoringJsonSerializerContext)
            .CustomAttributes
            .Where(a => a.AttributeType == typeof(JsonSerializableAttribute))
            .Select(a => a.ConstructorArguments.FirstOrDefault().Value as Type)
            .Where(t => t is not null)
            .Select(t => t!)
            .ToHashSet();

        // Get all public types in the Abstractions assembly that are DTOs
        var abstractionsAssembly = typeof(DescriptorAuthoringResult).Assembly;

        var dtoTypes = abstractionsAssembly.GetTypes()
            .Where(t => t.IsPublic && !t.IsAbstract && !t.IsInterface)
            .Where(t => t.IsSealed || t.IsEnum || t.IsValueType)
            .Where(t => t.Namespace?.StartsWith("CrestCreates.Agent.Authoring.Abstractions") == true)
            .Where(t => t.Name != "DescriptorAuthoringJsonSerializerContext")
            .Where(t => !t.IsNested)
            .Where(t => !t.IsGenericTypeDefinition)
            .ToList();

        var missing = dtoTypes
            .Where(t => !registeredTypes.Contains(t))
            .Select(t => t.FullName ?? t.Name)
            .OrderBy(n => n)
            .ToList();

        missing.Should().BeEmpty(
            $"all public DTO types in Authoring.Abstractions should have a [JsonSerializable] entry. " +
            $"Missing: {string.Join(", ", missing)}");
    }
}
