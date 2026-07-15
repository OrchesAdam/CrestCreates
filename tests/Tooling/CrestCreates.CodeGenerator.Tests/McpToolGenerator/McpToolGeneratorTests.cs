using System.Linq;
using CrestCreates.CodeGenerator.McpToolGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using FluentAssertions;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.McpToolGenerator;

public sealed class McpToolGeneratorTests
{
    [Fact]
    public void Typed_tool_emits_provider_and_exact_json_bindings()
    {
        var result = Run(@"
namespace Demo;
public sealed class InputDto { public string Name { get; set; } }
public sealed class OutputDto { public string Id { get; set; } }

[CrestCreates.Mcp.McpToolSpecs]
public static partial class OrderTools
{
    [CrestCreates.Mcp.McpToolSpec(
        ""orders.create"",
        InputType = typeof(InputDto),
        OutputType = typeof(OutputDto),
        ToolName = ""orders.create"",
        Description = ""Creates one order."")]
    public sealed class Create { }
}");

        result.Diagnostics.Should().NotContain(diagnostic => diagnostic.Id.StartsWith("MCP"));
        result.GeneratedSources.Should().HaveCount(2);
        var generated = string.Join("\n", result.GeneratedSources.Select(source => source.SourceText));
        generated.Should().Contain("IDescriptorProvider<McpToolDescriptor>");
        generated.Should().Contain("new McpCapabilityReference(");
        generated.Should().NotContain("VersionedDescriptorRef<CapabilityDescriptor>");
        generated.Should().Contain("VersionSelectionMode.Latest");
        generated.Should().Contain("JsonTypeInfo<global::Demo.InputDto>");
        generated.Should().Contain("output.GetType() != typeof(global::Demo.OutputDto)");
        generated.Should().Contain("McpToolBindingRegistry.Register");
        generated.Should().NotContain("DynamicApi");
        generated.Should().NotContain("GetProperties(");
        generated.Should().NotContain("Handler");
    }

    [Fact]
    public void No_input_void_output_emits_null_contracts()
    {
        var result = Run(@"
[CrestCreates.Mcp.McpToolSpecs]
public static partial class MaintenanceTools
{
    [CrestCreates.Mcp.McpToolSpec(""cache.refresh"", Description = ""Refreshes cache."")]
    public sealed class Refresh { }
}");

        var generated = string.Join("\n", result.GeneratedSources.Select(source => source.SourceText));
        generated.Should().Contain("InputType = null");
        generated.Should().Contain("OutputType = null");
        generated.Should().Contain("new ValueTask<object?>((object?)null)");
    }

    [Fact]
    public void Any_container_error_suppresses_all_generated_files()
    {
        var result = Run(@"
[CrestCreates.Mcp.McpToolSpecs]
public static partial class BadTools
{
    [CrestCreates.Mcp.McpToolSpec(""orders.get"", ToolName = ""bad tool"", Description = ""Gets order."")]
    public sealed class Get { }

    [CrestCreates.Mcp.McpToolSpec(""orders.create"", Description = """")]
    public sealed class Create { }
}");

        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Id == "MCP002");
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Id == "MCP004");
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void Invalid_container_and_negative_capability_version_are_diagnostics()
    {
        var result = Run(@"
[CrestCreates.Mcp.McpToolSpecs]
public class BadTools
{
    [CrestCreates.Mcp.McpToolSpec(""orders.get"", CapabilityVersion = -1, Description = ""Gets order."")]
    public sealed class Get { }
}");

        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Id == "MCP010");
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Id == "MCP012");
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void Duplicate_tool_name_and_descriptor_identity_fail_closed()
    {
        var result = Run(@"
[CrestCreates.Mcp.McpToolSpecs]
public static partial class DuplicateTools
{
    [CrestCreates.Mcp.McpToolSpec(""orders.one"", DescriptorId = ""same"", ToolName = ""same"", Description = ""One."")]
    public sealed class One { }
    [CrestCreates.Mcp.McpToolSpec(""orders.two"", DescriptorId = ""same"", ToolName = ""same"", Description = ""Two."")]
    public sealed class Two { }
}");

        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Id == "MCP018");
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void Interface_and_dictionary_contracts_are_rejected()
    {
        var result = Run(@"
using System.Collections.Generic;
public interface IInput { }
[CrestCreates.Mcp.McpToolSpecs]
public static partial class BadTypeTools
{
    [CrestCreates.Mcp.McpToolSpec(""one"", InputType = typeof(IInput), Description = ""One."")]
    public sealed class One { }
    [CrestCreates.Mcp.McpToolSpec(""two"", InputType = typeof(Dictionary<string, object>), Description = ""Two."")]
    public sealed class Two { }
}");

        result.Diagnostics.Count(diagnostic => diagnostic.Id == "MCP006").Should().Be(2);
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void Same_container_name_in_different_namespaces_gets_unique_hint_names()
    {
        var result = Run(@"
namespace Sales
{
[CrestCreates.Mcp.McpToolSpecs]
public static partial class OrderTools
{
    [CrestCreates.Mcp.McpToolSpec(""sales.order"", Description = ""Sales order."" )]
    public sealed class Get { }
}
}
namespace Support
{
[CrestCreates.Mcp.McpToolSpecs]
public static partial class OrderTools
{
    [CrestCreates.Mcp.McpToolSpec(""support.order"", Description = ""Support order."" )]
    public sealed class Get { }
}
}
");

        result.Diagnostics.Should().NotContain(diagnostic => diagnostic.Id.StartsWith("MCP"));
        result.GeneratedSources.Select(source => source.FileName)
            .Should().OnlyHaveUniqueItems();
        result.GeneratedSources.Select(source => source.FileName)
            .Should().Contain(file => file.EndsWith("Sales.OrderTools_McpToolProvider.g.cs"))
            .And.Contain(file => file.EndsWith("Support.OrderTools_McpToolProvider.g.cs"));
    }

    [Fact]
    public void Nested_container_is_rejected_to_keep_generated_identity_unambiguous()
    {
        var result = Run(@"
public static class Feature
{
    [CrestCreates.Mcp.McpToolSpecs]
    public static partial class OrderTools
    {
        [CrestCreates.Mcp.McpToolSpec(""orders.get"", Description = ""Gets order."" )]
        public sealed class Get { }
    }
}
");

        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Id == "MCP010");
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void Non_object_root_framework_types_are_rejected()
    {
        var result = Run(@"
using System;
using System.Collections.Generic;
[CrestCreates.Mcp.McpToolSpecs]
public static partial class BadRootTools
{
    [CrestCreates.Mcp.McpToolSpec(""date-time"", InputType = typeof(DateTime), Description = ""Date-time."" )]
    public sealed class DateTimeTool { }
    [CrestCreates.Mcp.McpToolSpec(""offset"", InputType = typeof(DateTimeOffset), Description = ""Offset."" )]
    public sealed class DateTimeOffsetTool { }
    [CrestCreates.Mcp.McpToolSpec(""list"", InputType = typeof(List<string>), Description = ""List."" )]
    public sealed class ListTool { }
    [CrestCreates.Mcp.McpToolSpec(""read-only-list"", InputType = typeof(IReadOnlyList<string>), Description = ""Read-only list."" )]
    public sealed class ReadOnlyListTool { }
    [CrestCreates.Mcp.McpToolSpec(""nullable-guid"", InputType = typeof(Guid?), Description = ""Nullable Guid."" )]
    public sealed class NullableGuidTool { }
    [CrestCreates.Mcp.McpToolSpec(""nullable-date"", InputType = typeof(DateOnly?), Description = ""Nullable date."" )]
    public sealed class NullableDateTool { }
    [CrestCreates.Mcp.McpToolSpec(""nullable-datetime"", InputType = typeof(DateTime?), Description = ""Nullable date-time."" )]
    public sealed class NullableDateTimeTool { }
}
");

        result.Diagnostics.Count(diagnostic => diagnostic.Id == "MCP006").Should().Be(7);
        result.GeneratedSources.Should().BeEmpty();
    }

    private static SourceGeneratorResult Run(string source)
        => SourceGeneratorTestHelper.RunGenerator<CodeGenerator.McpToolGenerator.McpToolGenerator>(
            source,
            additionalSources: [Stubs]);

    private const string Stubs = @"
using System;
namespace CrestCreates.Mcp
{
    public enum McpBooleanHint { Unspecified = 0, False = 1, True = 2 }
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class McpToolSpecsAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class McpToolSpecAttribute : Attribute
    {
        public McpToolSpecAttribute(string capabilityId) { }
        public string DescriptorId { get; set; }
        public int DescriptorVersion { get; set; } = 1;
        public int CapabilityVersion { get; set; }
        public Type InputType { get; set; }
        public Type OutputType { get; set; }
        public string ToolName { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public McpBooleanHint DestructiveHint { get; set; }
        public McpBooleanHint IdempotentHint { get; set; }
        public McpBooleanHint OpenWorldHint { get; set; }
    }
}
";
}
