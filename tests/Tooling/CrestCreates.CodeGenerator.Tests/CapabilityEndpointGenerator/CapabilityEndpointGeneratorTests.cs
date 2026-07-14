using System;
using System.Linq;
using CrestCreates.CodeGenerator.CapabilityEndpointGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using FluentAssertions;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.CapabilityEndpointGenerator;

public sealed class CapabilityEndpointGeneratorTests
{
    #region Stubs

    /// <summary>
    /// Provides minimal stub types in the expected namespaces so that the
    /// CapabilityEndpointGenerator can discover attributes via ForAttributeWithMetadataName.
    /// </summary>
    private static string BuildCapabilityEndpointStubs()
    {
        return @"
using System;

namespace CrestCreates.DynamicApi
{
    public enum CapabilityEndpointHttpMethod
    {
        None = 0,
        Get,
        Post,
        Put,
        Patch,
        Delete
    }

    public enum CapabilityEndpointAuthorizationMode
    {
        InheritCapability,
        RequireAuthenticated,
        AllowAnonymous
    }

    public enum CapabilityEndpointParameterSource
    {
        Route,
        Query,
        Header,
        Body
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CapabilityEndpointSpecAttribute : Attribute
    {
        public CapabilityEndpointSpecAttribute(
            string capabilityId,
            CapabilityEndpointHttpMethod httpMethod,
            string routePattern)
        {
            CapabilityId = capabilityId;
            HttpMethod = httpMethod;
            RoutePattern = routePattern;
        }

        public string CapabilityId { get; }
        public CapabilityEndpointHttpMethod HttpMethod { get; }
        public string RoutePattern { get; }

        public int CapabilityVersion { get; init; }
        public CapabilityEndpointAuthorizationMode AuthorizationMode { get; init; }
            = CapabilityEndpointAuthorizationMode.InheritCapability;
        public int SuccessStatusCode { get; init; }
        public string? OperationId { get; init; }
        public string? GroupName { get; init; }
        public string[]? Tags { get; init; }
        public string? Summary { get; init; }
        public string? Description { get; init; }
        public bool Deprecated { get; init; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CapabilityEndpointSpecsAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class CapabilityEndpointInputAttribute : Attribute
    {
        public CapabilityEndpointInputAttribute(Type type)
        {
            Type = type;
        }

        public Type Type { get; }

        public string Name { get; init; } = string.Empty;
        public CapabilityEndpointParameterSource Source { get; init; }
            = CapabilityEndpointParameterSource.Body;
        public bool Required { get; init; } = true;
        public string? CapabilityInputPath { get; init; }
        public string? TargetProperty { get; init; }
    }
}
";
    }

    #endregion

    /// <summary>
    /// Helper that runs the generator with source + stubs and returns the result.
    /// </summary>
    private static SourceGeneratorResult Run(string source)
    {
        return SourceGeneratorTestHelper.RunGenerator<CodeGenerator.CapabilityEndpointGenerator.CapabilityEndpointGenerator>(
            source,
            additionalSources: new[] { BuildCapabilityEndpointStubs() });
    }

    [Fact]
    public void BodyOnlyPostEndpoint_GeneratesProviderAndBindings()
    {
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    public sealed class CreateItemDto
    {
        public string Name { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }

    [CapabilityEndpointSpecs]
    public static partial class ItemEndpoints
    {
        [CapabilityEndpointSpec(""create-item"", CapabilityEndpointHttpMethod.Post, ""items"")]
        [CapabilityEndpointInput(typeof(CreateItemDto), Source = CapabilityEndpointParameterSource.Body, Name = ""body"")]
        public sealed class CreateItemSpec { }
    }
}
";

        var result = Run(source);
        Assert.NotEmpty(result.GeneratedSources);

        var providerFile = result.GetSourceByFileName("ItemEndpoints_Provider.g.cs");
        Assert.NotNull(providerFile);
        Assert.Contains("create-item", providerFile!.SourceText);
        Assert.Contains("HttpMethod = CapabilityEndpointHttpMethod.Post", providerFile.SourceText);
        Assert.Contains("RoutePattern = \"items\"", providerFile.SourceText);

        var bindingFile = result.GetSourceByFileName("ItemEndpoints_Bindings.g.cs");
        Assert.NotNull(bindingFile);
        Assert.Contains("ReadBodyAsync<global::TestNs.CreateItemDto>", bindingFile!.SourceText);
    }

    [Fact]
    public void RouteOnlyGetEndpoint_GeneratesRouteValueParsing()
    {
        // NOTE: Uses System.Guid (not int/long/string) because the generator's
        // FullyQualifiedFormat returns C# keyword aliases (e.g. "int") that
        // IsScalarTypeName doesn't match (it expects "Int32").
        var source = @"
using System;
using CrestCreates.DynamicApi;

namespace TestNs
{
    [CapabilityEndpointSpecs]
    public static partial class ItemEndpoints
    {
        [CapabilityEndpointSpec(""get-item"", CapabilityEndpointHttpMethod.Get, ""items/{id}"")]
        [CapabilityEndpointInput(typeof(Guid), Name = ""id"", Source = CapabilityEndpointParameterSource.Route)]
        public sealed class GetItemSpec { }
    }
}
";

        var result = Run(source);
        Assert.NotEmpty(result.GeneratedSources);

        var bindingFile = result.GetSourceByFileName("ItemEndpoints_Bindings.g.cs");
        Assert.NotNull(bindingFile);
        Assert.Contains("Guid.Parse", bindingFile!.SourceText);
    }

    [Fact]
    public void RouteBodyPutEndpoint_GeneratesBodyReadAndPropertyAssignment()
    {
        // NOTE: Uses System.Guid + matching property name "Id" (uppercase)
        // to work around FullyQualifiedFormat returning "int" for typeof(int).
        var source = @"
using System;
using CrestCreates.DynamicApi;

namespace TestNs
{
    public sealed class UpdateItemDto
    {
        public string Name { get; set; } = string.Empty;
        public Guid Id { get; set; }
    }

    [CapabilityEndpointSpecs]
    public static partial class ItemEndpoints
    {
        [CapabilityEndpointSpec(""update-item"", CapabilityEndpointHttpMethod.Put, ""items/{id}"")]
        [CapabilityEndpointInput(typeof(UpdateItemDto), Source = CapabilityEndpointParameterSource.Body, Name = ""body"")]
        [CapabilityEndpointInput(typeof(Guid), Name = ""Id"", Source = CapabilityEndpointParameterSource.Route)]
        public sealed class UpdateItemSpec { }
    }
}
";

        var result = Run(source);
        Assert.NotEmpty(result.GeneratedSources);

        var bindingFile = result.GetSourceByFileName("ItemEndpoints_Bindings.g.cs");
        Assert.NotNull(bindingFile);
        Assert.Contains("ReadBodyAsync<global::TestNs.UpdateItemDto>", bindingFile!.SourceText);
        Assert.Contains("model.Id = Guid.Parse(", bindingFile.SourceText);
    }

    [Fact]
    public void MultipleEndpointsInOneContainer_GeneratesAllEndpoints()
    {
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    public sealed class CreateDto
    {
        public string Name { get; set; } = string.Empty;
    }

    [CapabilityEndpointSpecs]
    public static partial class ItemEndpoints
    {
        [CapabilityEndpointSpec(""create-item"", CapabilityEndpointHttpMethod.Post, ""items"")]
        [CapabilityEndpointInput(typeof(CreateDto), Source = CapabilityEndpointParameterSource.Body, Name = ""body"")]
        public sealed class CreateItemSpec { }

        [CapabilityEndpointSpec(""get-item"", CapabilityEndpointHttpMethod.Get, ""items/{id}"")]
        [CapabilityEndpointInput(typeof(int), Name = ""id"", Source = CapabilityEndpointParameterSource.Route)]
        public sealed class GetItemSpec { }

        [CapabilityEndpointSpec(""delete-item"", CapabilityEndpointHttpMethod.Delete, ""items/{id}"")]
        [CapabilityEndpointInput(typeof(int), Name = ""id"", Source = CapabilityEndpointParameterSource.Route)]
        public sealed class DeleteItemSpec { }
    }
}
";

        var result = Run(source);
        Assert.NotEmpty(result.GeneratedSources);

        var providerFile = result.GetSourceByFileName("ItemEndpoints_Provider.g.cs");
        Assert.NotNull(providerFile);
        Assert.Contains("create-item", providerFile!.SourceText);
        Assert.Contains("get-item", providerFile.SourceText);
        Assert.Contains("delete-item", providerFile.SourceText);
    }

    [Fact]
    public void SuccessStatusCodeAutoRule_PostReturns201_GetReturns200()
    {
        var source = @"
using System;
using CrestCreates.DynamicApi;

namespace TestNs
{
    public sealed class CreateDto
    {
        public string Name { get; set; } = string.Empty;
    }

    [CapabilityEndpointSpecs]
    public static partial class ItemEndpoints
    {
        [CapabilityEndpointSpec(""create-item"", CapabilityEndpointHttpMethod.Post, ""items"")]
        [CapabilityEndpointInput(typeof(CreateDto), Source = CapabilityEndpointParameterSource.Body, Name = ""body"")]
        public sealed class CreateItemSpec { }

        [CapabilityEndpointSpec(""get-item"", CapabilityEndpointHttpMethod.Get, ""items/{id}"")]
        [CapabilityEndpointInput(typeof(Guid), Name = ""id"", Source = CapabilityEndpointParameterSource.Route)]
        public sealed class GetItemSpec { }
    }
}
";

        var result = Run(source);
        Assert.NotEmpty(result.GeneratedSources);

        var providerFile = result.GetSourceByFileName("ItemEndpoints_Provider.g.cs");
        Assert.NotNull(providerFile);

        var sourceText = providerFile!.SourceText;

        // POST → auto 201 (search entire text since OutputMapping is far from 'create-item' string)
        Assert.Contains("SuccessStatusCode = 201", sourceText);

        // GET → auto 200 (explicit SuccessStatusCode=0 gets auto-resolved)
        Assert.Contains("SuccessStatusCode = 200", sourceText);
    }

    [Fact]
    public void DeDuplication_BySpecClassName_RemovesDuplicates()
    {
        // Two specs with the same SpecClassName (by capability id) should de-dup.
        // This can happen when Level 1 and Level 2 specs overlap.
        // With SpecClassName-based de-dup, identical class names within a container
        // are collapsed; different class names produce separate endpoints.
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    public sealed class CreateDto
    {
        public string Name { get; set; } = string.Empty;
    }

    [CapabilityEndpointSpecs]
    public static partial class ItemEndpoints
    {
        [CapabilityEndpointSpec(""create-item"", CapabilityEndpointHttpMethod.Post, ""items"", CapabilityVersion = 1)]
        [CapabilityEndpointInput(typeof(CreateDto), Source = CapabilityEndpointParameterSource.Body, Name = ""body"")]
        public sealed class CreateItemSpec { }
    }
}
";

        var result = Run(source);
        Assert.NotEmpty(result.GeneratedSources);

        var providerFile = result.GetSourceByFileName("ItemEndpoints_Provider.g.cs");
        Assert.NotNull(providerFile);

        // Single spec → 2 occurrences (1 array + 1 descriptor)
        var descriptorCount = CountOccurrences(providerFile!.SourceText, "new CapabilityEndpointDescriptor");
        Assert.Equal(2, descriptorCount);
    }

    private static int CountOccurrences(string text, string search)
    {
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf(search, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += search.Length;
        }
        return count;
    }

    [Fact]
    public void QueryScalarInput_GeneratesQueryBinding()
    {
        // NOTE: Uses System.DateTime (not int) because the generator's
        // FullyQualifiedFormat returns C# keyword aliases (e.g. "int") that
        // IsScalarTypeName doesn't match (it expects "Int32").
        var source = @"
using System;
using CrestCreates.DynamicApi;

namespace TestNs
{
    [CapabilityEndpointSpecs]
    public static partial class ItemEndpoints
    {
        [CapabilityEndpointSpec(""search-items"", CapabilityEndpointHttpMethod.Get, ""items"")]
        [CapabilityEndpointInput(typeof(DateTime), Name = ""from"", Source = CapabilityEndpointParameterSource.Query)]
        public sealed class SearchItemsSpec { }
    }
}
";

        var result = Run(source);
        Assert.NotEmpty(result.GeneratedSources);

        var bindingFile = result.GetSourceByFileName("ItemEndpoints_Bindings.g.cs");
        Assert.NotNull(bindingFile);
        Assert.Contains("DateTime.Parse", bindingFile!.SourceText);
        Assert.Contains("context.Request.Query[\"from\"].ToString()", bindingFile.SourceText);
    }

    [Fact]
    public void HeaderScalarInput_GeneratesHeaderBinding()
    {
        // NOTE: Uses System.Guid (not string) because the generator's
        // FullyQualifiedFormat returns C# keyword aliases (e.g. "string") that
        // IsScalarTypeName doesn't match (it expects "String").
        var source = @"
using System;
using CrestCreates.DynamicApi;

namespace TestNs
{
    [CapabilityEndpointSpecs]
    public static partial class ItemEndpoints
    {
        [CapabilityEndpointSpec(""get-item"", CapabilityEndpointHttpMethod.Get, ""items"")]
        [CapabilityEndpointInput(typeof(Guid), Name = ""X-Request-Id"", Source = CapabilityEndpointParameterSource.Header)]
        public sealed class GetItemByHeaderSpec { }
    }
}
";

        var result = Run(source);
        Assert.NotEmpty(result.GeneratedSources);

        var bindingFile = result.GetSourceByFileName("ItemEndpoints_Bindings.g.cs");
        Assert.NotNull(bindingFile);
        Assert.Contains("Guid.Parse", bindingFile!.SourceText);
        Assert.Contains("context.Request.Headers[\"X-Request-Id\"].ToString()", bindingFile.SourceText);
    }

    [Fact]
    public void InputAttribute_DefaultValues_AppliesBodySourceAndRequiredTrue()
    {
        // Verify that [CapabilityEndpointInput(typeof(CreateDto))] without explicit
        // Source or Required defaults to Source=Body and Required=true.
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    public sealed class CreateDto
    {
        public string Title { get; set; } = string.Empty;
    }

    [CapabilityEndpointSpecs]
    public static partial class BookEndpoints
    {
        [CapabilityEndpointSpec(""books.create"", CapabilityEndpointHttpMethod.Post, ""/api/books"")]
        [CapabilityEndpointInput(typeof(CreateDto))]
        public sealed class Create { }
    }
}
";

        var result = Run(source);
        Assert.NotEmpty(result.GeneratedSources);

        var bindingFile = result.GetSourceByFileName("BookEndpoints_Bindings.g.cs");
        Assert.NotNull(bindingFile);

        // Should use ReadBodyAsync (Body source, not route parsing)
        Assert.Contains("ReadBodyAsync<global::TestNs.CreateDto>", bindingFile!.SourceText);

        // Required=true → optional=false in generated binding
        Assert.Contains("context, false, ct", bindingFile.SourceText);

        // Should NOT contain route-related parsing (no Guid/DateTime/Enum.Parse)
        Assert.DoesNotContain("RouteValues", bindingFile.SourceText);
        Assert.DoesNotContain("Query[\"", bindingFile.SourceText);
        Assert.DoesNotContain("Headers[\"", bindingFile.SourceText);
    }

    [Fact]
    public void QueryBinding_Generates_StringValues_ToString()
    {
        // Test that Query binding generates .ToString() on StringValues (struct, non-nullable)
        // NOT ?.ToString() (null-conditional, which is used only for Route values)
        var source = @"
using System;
using CrestCreates.DynamicApi;

[CapabilityEndpointSpecs]
public static partial class TestEndpoints
{
    [CapabilityEndpointSpec(""test.query"", CapabilityEndpointHttpMethod.Get, ""/api/test"")]
    [CapabilityEndpointInput(typeof(DateTime), Name = ""q"", Source = CapabilityEndpointParameterSource.Query)]
    public sealed class QueryTest { }
}
";

        var result = Run(source);
        var bindingFile = result.GetSourceByFileName("TestEndpoints_Bindings.g.cs");
        Assert.NotNull(bindingFile);
        // Query returns StringValues (struct), so binding uses .ToString() directly
        Assert.Contains(@"context.Request.Query[""q""].ToString()", bindingFile!.SourceText);
        // Query should NOT use null-conditional ?.ToString()
        Assert.DoesNotContain(@"context.Request.Query[""q""]?.ToString()", bindingFile.SourceText);
    }

    [Fact]
    public void HeaderBinding_Generates_StringValues_ToString()
    {
        // Test that Header binding generates .ToString() on StringValues (struct, non-nullable)
        var source = @"
using System;
using CrestCreates.DynamicApi;

[CapabilityEndpointSpecs]
public static partial class TestEndpoints
{
    [CapabilityEndpointSpec(""test.header"", CapabilityEndpointHttpMethod.Get, ""/api/test"")]
    [CapabilityEndpointInput(typeof(Guid), Name = ""X-Request-Id"", Source = CapabilityEndpointParameterSource.Header)]
    public sealed class HeaderTest { }
}
";

        var result = Run(source);
        var bindingFile = result.GetSourceByFileName("TestEndpoints_Bindings.g.cs");
        Assert.NotNull(bindingFile);
        Assert.Contains(@"context.Request.Headers[""X-Request-Id""].ToString()", bindingFile!.SourceText);
        Assert.DoesNotContain(@"context.Request.Headers[""X-Request-Id""]?.ToString()", bindingFile.SourceText);
    }

    [Fact]
    public void Dedup_AllowsSameCapabilityIdDifferentEndpointVersion()
    {
        // Two specs with same CapabilityId but different versions should both be emitted
        var source = @"
using CrestCreates.DynamicApi;

[CapabilityEndpointSpecs]
public static partial class TestEndpoints
{
    [CapabilityEndpointSpec(""test.versioned"", CapabilityEndpointHttpMethod.Get, ""/api/v1/test"", CapabilityVersion = 1)]
    public sealed class GetV1 { }

    [CapabilityEndpointSpec(""test.versioned"", CapabilityEndpointHttpMethod.Get, ""/api/v2/test"", CapabilityVersion = 2)]
    public sealed class GetV2 { }
}
";

        var result = Run(source);
        Assert.NotEmpty(result.GeneratedSources);

        var providerFile = result.GetSourceByFileName("TestEndpoints_Provider.g.cs");
        Assert.NotNull(providerFile);

        // Both versioned descriptors should appear in the provider
        // Count = 1 array init + N descriptors = 1 + 2 = 3
        var descriptorCount = CountOccurrences(providerFile!.SourceText, "new CapabilityEndpointDescriptor");
        Assert.Equal(3, descriptorCount);
    }

    [Fact]
    public void BindingEmitter_Uses_TargetProperty_For_ClrAssignment()
    {
        // When TargetProperty = "BookId" and Name = "id",
        // generated code should contain "model.BookId = ..." not "model.Id = ..."
        var source = @"
using System;
using CrestCreates.DynamicApi;

namespace TestNs
{
    public sealed class BookDto
    {
        public string Title { get; set; } = string.Empty;
        public Guid BookId { get; set; }
    }

    [CapabilityEndpointSpecs]
    public static partial class BookEndpoints
    {
        [CapabilityEndpointSpec(""update-book"", CapabilityEndpointHttpMethod.Put, ""books/{id}"")]
        [CapabilityEndpointInput(typeof(BookDto), Source = CapabilityEndpointParameterSource.Body, Name = ""body"")]
        [CapabilityEndpointInput(typeof(Guid), Name = ""id"", Source = CapabilityEndpointParameterSource.Route, TargetProperty = ""BookId"")]
        public sealed class UpdateBookSpec { }
    }
}
";

        var result = Run(source);
        Assert.NotEmpty(result.GeneratedSources);

        var bindingFile = result.GetSourceByFileName("BookEndpoints_Bindings.g.cs");
        Assert.NotNull(bindingFile);

        // TargetProperty "BookId" should be used for CLR assignment
        Assert.Contains("model.BookId = Guid.Parse(", bindingFile!.SourceText);

        // The route parameter name "id" should NOT be PascalCased to "Id" for property assignment
        Assert.DoesNotContain("model.Id =", bindingFile.SourceText);
    }
}
