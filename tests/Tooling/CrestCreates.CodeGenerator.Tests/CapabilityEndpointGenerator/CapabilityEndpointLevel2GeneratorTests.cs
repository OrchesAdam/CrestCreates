using System;
using System.Linq;
using CrestCreates.CodeGenerator.CapabilityEndpointGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.CapabilityEndpointGenerator;

public sealed class CapabilityEndpointLevel2GeneratorTests
{
    #region Stubs

    /// <summary>
    /// Provides minimal stub types for Level 2 attributes in the CrestCreates.DynamicApi namespace.
    /// </summary>
    private static string BuildLevel2Stubs()
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
    public sealed class CapabilityEndpointSetAttribute : Attribute
    {
        public string? RoutePrefix { get; init; }
        public string? GroupName { get; init; }
        public string[]? Tags { get; init; }
        public string? Summary { get; init; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class PostAttribute : Attribute
    {
        public PostAttribute(string capabilityId, string route = """")
        {
            CapabilityId = capabilityId;
            Route = route;
        }

        public string CapabilityId { get; }
        public string Route { get; }

        public Type? Body { get; init; }
        public int CapabilityVersion { get; init; }
        public CapabilityEndpointAuthorizationMode Auth { get; init; }
            = CapabilityEndpointAuthorizationMode.InheritCapability;
        public int SuccessStatusCode { get; init; }
        public string? OperationId { get; init; }
        public string? Summary { get; init; }
        public string? Description { get; init; }
        public bool Deprecated { get; init; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class GetAttribute : Attribute
    {
        public GetAttribute(string capabilityId, string route = """")
        {
            CapabilityId = capabilityId;
            Route = route;
        }

        public string CapabilityId { get; }
        public string Route { get; }

        public Type? Input { get; init; }
        public string? InputName { get; init; }
        public int CapabilityVersion { get; init; }
        public CapabilityEndpointAuthorizationMode Auth { get; init; }
            = CapabilityEndpointAuthorizationMode.InheritCapability;
        public string? OperationId { get; init; }
        public string? Summary { get; init; }
        public string? Description { get; init; }
        public bool Deprecated { get; init; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class PutAttribute : Attribute
    {
        public PutAttribute(string capabilityId, string route = """")
        {
            CapabilityId = capabilityId;
            Route = route;
        }

        public string CapabilityId { get; }
        public string Route { get; }

        public Type? Body { get; init; }
        public Type? Input { get; init; }
        public string? InputName { get; init; }
        public int CapabilityVersion { get; init; }
        public CapabilityEndpointAuthorizationMode Auth { get; init; }
            = CapabilityEndpointAuthorizationMode.InheritCapability;
        public int SuccessStatusCode { get; init; }
        public string? OperationId { get; init; }
        public string? Summary { get; init; }
        public string? Description { get; init; }
        public bool Deprecated { get; init; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DeleteAttribute : Attribute
    {
        public DeleteAttribute(string capabilityId, string route = """")
        {
            CapabilityId = capabilityId;
            Route = route;
        }

        public string CapabilityId { get; }
        public string Route { get; }

        public Type? Input { get; init; }
        public string? InputName { get; init; }
        public int CapabilityVersion { get; init; }
        public CapabilityEndpointAuthorizationMode Auth { get; init; }
            = CapabilityEndpointAuthorizationMode.InheritCapability;
        public string? OperationId { get; init; }
        public string? Summary { get; init; }
        public string? Description { get; init; }
        public bool Deprecated { get; init; }
    }

    // AOT-safe body binding components (8a native)
    public static class CapabilityEndpointJsonTypeInfoResolver
    {
        public static object? Resolve<T>(object context) => null;
    }

    public static class CapabilityEndpointBodyReader
    {
        public static ValueTask<T?> ReadNativeBodyAsync<T>(object context, object? jsonTypeInfo, bool optional, object ct = null) => default;
    }

    public static class CapabilityEndpointJsonContractRegistry
    {
        public static void RegisterBodyType(Type bodyType) { }
    }

    // Binding registry stub
    public static class CapabilityEndpointBindingRegistry
    {
        public static void Register(object contract) { }
    }

    public sealed class CapabilityEndpointBindingContract
    {
        public CapabilityEndpointBindingContract(string id, int version, object handler) { }
    }
}
";
    }

    #endregion

    private static SourceGeneratorResult Run(string source)
    {
        return SourceGeneratorTestHelper.RunGenerator<CodeGenerator.CapabilityEndpointGenerator.CapabilityEndpointGenerator>(
            source,
            additionalSources: new[] { BuildLevel2Stubs() });
    }

    [Fact]
    public void PostAttribute_NormalizesToSpecRecord_GeneratesProviderAndBindings()
    {
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    public sealed class CreateItemBody
    {
        public string Name { get; set; } = string.Empty;
    }

    [CapabilityEndpointSet]
    public static partial class ItemApi
    {
        [Post(""create-item"", ""items"", Body = typeof(CreateItemBody))]
        public sealed partial class CreateItem { }
    }
}
";

        var result = Run(source);

        var providerFile = result.GetSourceByFileName("ItemApi_Provider.g.cs");
        Assert.NotNull(providerFile);
        Assert.Contains("create-item", providerFile!.SourceText);
        Assert.Contains("HttpMethod = CapabilityEndpointHttpMethod.Post", providerFile.SourceText);
        Assert.Contains("RoutePattern = \"/items\"", providerFile.SourceText);

        var bindingFile = result.GetSourceByFileName("ItemApi_Bindings.g.cs");
        Assert.NotNull(bindingFile);
        Assert.Contains("CapabilityEndpointBodyReader.ReadNativeBodyAsync<global::TestNs.CreateItemBody>", bindingFile!.SourceText);
    }

    [Fact]
    public void GetAttribute_WithRouteToken_ExtractsInputBinding()
    {
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    [CapabilityEndpointSet]
    public static partial class ItemApi
    {
        [Get(""get-item"", ""items/{id}"", Input = typeof(int))]
        public sealed partial class GetItem { }
    }
}
";

        var result = Run(source);

        var providerFile = result.GetSourceByFileName("ItemApi_Provider.g.cs");
        Assert.NotNull(providerFile);
        Assert.Contains("get-item", providerFile!.SourceText);
        Assert.Contains("HttpMethod = CapabilityEndpointHttpMethod.Get", providerFile.SourceText);

        var bindingFile = result.GetSourceByFileName("ItemApi_Bindings.g.cs");
        Assert.NotNull(bindingFile);
        Assert.Contains("int.Parse", bindingFile!.SourceText);
    }

    [Fact]
    public void CapabilityEndpointSet_ContainerDefaults_AppliedToSpecs()
    {
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    public sealed class CreateBody
    {
        public string Name { get; set; } = string.Empty;
    }

    [CapabilityEndpointSet(RoutePrefix = ""api/v2"", GroupName = ""Items"", Tags = new[] { ""items"", ""management"" })]
    public static partial class ItemApi
    {
        [Post(""create-item"", ""items"", Body = typeof(CreateBody))]
        public sealed partial class CreateItem { }
    }
}
";

        var result = Run(source);

        var providerFile = result.GetSourceByFileName("ItemApi_Provider.g.cs");
        Assert.NotNull(providerFile);
        var text = providerFile!.SourceText;

        // RoutePrefix "api/v2" + "items" → "/api/v2/items"
        Assert.Contains("RoutePattern = \"/api/v2/items\"", text);

        // GroupName applied
        Assert.Contains("GroupName = \"Items\"", text);

        // Tags applied
        Assert.Contains("Tags = new[] { \"items\", \"management\" }", text);
    }

    [Fact]
    public void RouteBodySplice_RouteTokensMappedToBodyPropertiesByName()
    {
        // When [Put] has both Body and Input + matching route token,
        // the binding emitter generates code to read body and splice route
        // values into matching body properties by name.
        // NOTE: Uses System.Guid to avoid C# keyword alias mismatch in
        // FullyQualifiedFormat (see RouteOnlyGetEndpoint comment for details).
        var source = @"
using System;
using CrestCreates.DynamicApi;

namespace TestNs
{
    public sealed class UpdateBody
    {
        public string Name { get; set; } = string.Empty;
        public Guid Id { get; set; }
    }

    [CapabilityEndpointSet]
    public static partial class ItemApi
    {
        [Put(""update-item"", ""items/{id}"", Body = typeof(UpdateBody), Input = typeof(Guid), InputName = ""Id"")]
        public sealed partial class UpdateItem { }
    }
}
";

        var result = Run(source);

        var bindingFile = result.GetSourceByFileName("ItemApi_Bindings.g.cs");
        Assert.NotNull(bindingFile);
        var text = bindingFile!.SourceText;

        // Should read body
        Assert.Contains("CapabilityEndpointBodyReader.ReadNativeBodyAsync<global::TestNs.UpdateBody>", text);

        // Should assign route value to model property (InputName="Id" → "model.Id = ...")
        Assert.Contains("model.Id = Guid.Parse(", text);
    }

    [Fact]
    public void GetAttribute_RouteWithConstraint_NormalizesTokenName()
    {
        // Route constraint {id:int} should normalize to token "id", not "id:int".
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    [CapabilityEndpointSet]
    public static partial class ItemApi
    {
        [Get(""get-item"", ""items/{id:int}"", Input = typeof(int))]
        public sealed partial class GetItem { }
    }
}
";

        var result = Run(source);

        var bindingFile = result.GetSourceByFileName("ItemApi_Bindings.g.cs");
        Assert.NotNull(bindingFile);
        var text = bindingFile!.SourceText;

        // Should extract and bind "id" (not "id:int")
        Assert.DoesNotContain("id:int", text);
        Assert.Contains("int.Parse", text);
    }
}
