using System.Linq;
using FluentAssertions;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using Xunit;
using CompatibilityGen = CrestCreates.CodeGenerator.AppServiceCompatibilityGenerator.AppServiceCompatibilityGenerator;

namespace CrestCreates.CodeGenerator.Tests.AppServiceCompatibilityGenerator;

public sealed class AppServiceCompatibilityGeneratorTests
{
    [Fact]
    public void ClassLevelProjection_GeneratesCapabilityDescriptors()
    {
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            [CapabilityCompatibilityProjection]
            public class BookAppService
            {
                public System.Threading.Tasks.Task<string> CreateAsync(string input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult(input);
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        var generated = result.GetSourceByFileName("GeneratedAppServiceCompatibilityCapabilities_Book.g.cs");
        generated.Should().NotBeNull();
        generated!.SourceText.Should().Contain("compat.appservice.book.create");
        generated.SourceText.Should().Contain("CapabilityProjectionKind.AppServiceCompatibility");
        generated.SourceText.Should().Contain("DescriptorProviderRegistry.Register");

        result.CompilationSuccess.Should().BeTrue("generated code must compile successfully");
    }

    [Fact]
    public void ClassLevelProjection_GeneratesEndpointDescriptors()
    {
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            [CapabilityCompatibilityProjection]
            public class BookAppService
            {
                public System.Threading.Tasks.Task<string> CreateAsync(string input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult(input);
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        var endpoints = result.GetSourceByFileName("GeneratedAppServiceCompatibilityEndpoints_Book.g.cs");
        endpoints.Should().NotBeNull();
        endpoints!.SourceText.Should().Contain("endpoint:compat.appservice.book.create");
        endpoints.SourceText.Should().Contain("ICapabilityEndpointDescriptorProvider");

        var bindings = result.GetSourceByFileName("GeneratedAppServiceCompatibilityBindings_Book.g.cs");
        bindings.Should().NotBeNull();
        bindings!.SourceText.Should().Contain("CapabilityEndpointBindingRegistry.Register");

        result.CompilationSuccess.Should().BeTrue("generated code must compile successfully");
    }

    [Fact]
    public void ClassLevelProjection_GeneratesCompatibilityInvokers()
    {
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            [CapabilityCompatibilityProjection]
            public class BookAppService
            {
                public System.Threading.Tasks.Task<string> CreateAsync(string input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult(input);
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        var invokers = result.GetSourceByFileName("GeneratedAppServiceCompatibilityInvokers_Book.g.cs");
        invokers.Should().NotBeNull();
        invokers!.SourceText.Should().Contain("ICapabilityContextAwareHandlerInvoker");
        invokers.SourceText.Should().Contain("context.ServiceProvider.GetRequiredService");
        invokers.SourceText.Should().Contain("CapabilityHandlerResolverProvider.Register");
        invokers.SourceText.Should().NotContain("new CapabilityHandlerResolver()");
        invokers.SourceText.Should().NotContain("SetResolver");

        result.CompilationSuccess.Should().BeTrue("generated code must compile successfully");
    }

    [Fact]
    public void ClassLevelProjection_GeneratesManifest()
    {
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            [CapabilityCompatibilityProjection]
            public class BookAppService
            {
                public System.Threading.Tasks.Task<string> CreateAsync(string input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult(input);
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        var manifest = result.GetSourceByFileName("GeneratedAppServiceCompatibilityManifest_Book.g.cs");
        manifest.Should().NotBeNull();
        manifest!.SourceText.Should().Contain("AppServiceCompatibilityProjectionEntry");
        manifest.SourceText.Should().Contain("compat.appservice.book.create");
        manifest.SourceText.Should().Contain("CapabilityProjectionKind.AppServiceCompatibility");

        result.CompilationSuccess.Should().BeTrue("generated code must compile successfully");
    }

    [Fact]
    public void MultiParameterMethod_GeneratesEnvelopeType()
    {
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            [CapabilityCompatibilityProjection]
            public class BookAppService
            {
                public System.Threading.Tasks.Task<string> UpdateAsync(System.Guid id, UpdateBookDto input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult("updated");
            }

            public class UpdateBookDto
            {
                public string Title { get; set; } = "";
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        // Should have envelope type
        result.ContainsSource("BookAppService_Update_CompatibilityInput").Should().BeTrue();

        var invokers = result.GetSourceByFileName("GeneratedAppServiceCompatibilityInvokers_Book.g.cs");
        invokers.Should().NotBeNull();
        invokers!.SourceText.Should().Contain("envelope");

        result.CompilationSuccess.Should().BeTrue("generated code must compile successfully");
    }

    [Fact]
    public void MethodLevelProjection_OnlyProjectsMarkedMethods()
    {
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            public class BookAppService
            {
                [CapabilityCompatibilityProjection]
                public System.Threading.Tasks.Task<string> CreateAsync(string input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult(input);

                public System.Threading.Tasks.Task<string> GetAsync(System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult("not-projected");
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        // Should project Create but not Get
        var capabilities = result.GetSourceByFileName("GeneratedAppServiceCompatibilityCapabilities_Book.g.cs");
        capabilities.Should().NotBeNull();
        capabilities!.SourceText.Should().Contain("compat.appservice.book.create");
        capabilities.SourceText.Should().NotContain("compat.appservice.book.get");

        result.CompilationSuccess.Should().BeTrue("generated code must compile successfully");
    }

    [Fact]
    public void CompatibilityIgnore_ExcludesMethod()
    {
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            [CapabilityCompatibilityProjection]
            public class BookAppService
            {
                public System.Threading.Tasks.Task<string> CreateAsync(string input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult(input);

                [CapabilityCompatibilityIgnore]
                public System.Threading.Tasks.Task<string> GetAsync(System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult("ignored");
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        var capabilities = result.GetSourceByFileName("GeneratedAppServiceCompatibilityCapabilities_Book.g.cs");
        capabilities.Should().NotBeNull();
        capabilities!.SourceText.Should().Contain("compat.appservice.book.create");
        capabilities.SourceText.Should().NotContain("compat.appservice.book.get");

        result.CompilationSuccess.Should().BeTrue("generated code must compile successfully");
    }

    [Fact]
    public void CapabilityIdPrefixOverride_ChangesGeneratedId()
    {
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            [CapabilityCompatibilityProjection(CapabilityIdPrefix = "custom.books")]
            public class BookAppService
            {
                public System.Threading.Tasks.Task<string> CreateAsync(string input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult(input);
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        var capabilities = result.GetSourceByFileName("GeneratedAppServiceCompatibilityCapabilities_Book.g.cs");
        capabilities.Should().NotBeNull();
        capabilities!.SourceText.Should().Contain("custom.books.create");
        capabilities.SourceText.Should().NotContain("compat.appservice.book.create");

        result.CompilationSuccess.Should().BeTrue("generated code must compile successfully");
    }

    [Fact]
    public void CEP031_ProjectionWithDynamicApiIgnore_ReportsDiagnostic()
    {
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            [CapabilityCompatibilityProjection]
            public class BookAppService
            {
                [CapabilityCompatibilityProjection]
                [DynamicApiIgnore]
                public System.Threading.Tasks.Task<string> CreateAsync(string input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult(input);
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        result.Diagnostics.Should().Contain(d => d.Id == "CEP031");

        if (!result.CompilationSuccess)
        {
            var errors = result.GetErrors().ToList();
            throw new System.Exception($"CEP031 Compilation failed with {errors.Count} errors. Errors:\n{string.Join("\n", errors.Select(e => $"{e.Id}: {e.GetMessage()} at {e.Location}"))}");
        }
        result.CompilationSuccess.Should().BeTrue("generated code must compile successfully");
    }

    [Fact]
    public void BindingCode_DoesNotUseDictionaryFallback()
    {
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            [CapabilityCompatibilityProjection]
            public class BookAppService
            {
                public System.Threading.Tasks.Task<string> UpdateAsync(System.Guid id, string input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult("updated");
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        var bindings = result.GetSourceByFileName("GeneratedAppServiceCompatibilityBindings_Book.g.cs");
        bindings.Should().NotBeNull();
        bindings!.SourceText.Should().NotContain("Dictionary<string, object?>");

        result.CompilationSuccess.Should().BeTrue("generated code must compile successfully");
    }

    [Fact]
    public void BindingCode_UsesJsonTypeInfoForBodyRead()
    {
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            [CapabilityCompatibilityProjection]
            public class BookAppService
            {
                public System.Threading.Tasks.Task<string> CreateAsync(CreateBookDto input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult("created");
            }

            public class CreateBookDto
            {
                public string Title { get; set; } = "";
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        var bindings = result.GetSourceByFileName("GeneratedAppServiceCompatibilityBindings_Book.g.cs");
        bindings.Should().NotBeNull();
        // P0-1: Removed CompatibilityJsonContext; now uses generic ReadBodyAsync<T> overload.
        bindings!.SourceText.Should().Contain("ReadBodyAsync");
        bindings.SourceText.Should().NotContain("CompatibilityJsonContext");
        bindings.SourceText.Should().NotContain("JsonSerializable");

        result.CompilationSuccess.Should().BeTrue("generated code must compile successfully");
    }

    [Fact]
    public void GeneratedCode_DoesNotReferenceLegacyDynamicApi()
    {
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            [CapabilityCompatibilityProjection]
            public class BookAppService
            {
                public System.Threading.Tasks.Task<string> CreateAsync(string input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult(input);
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        foreach (var generated in result.GeneratedSources)
        {
            generated.SourceText.Should().NotContain("DynamicApiGeneratedRegistryStore");
            generated.SourceText.Should().NotContain("IDynamicApiGeneratedProvider");
            generated.SourceText.Should().NotContain("DynamicApiGeneratedRuntime");
        }

        result.CompilationSuccess.Should().BeTrue("generated code must compile successfully");
    }

    [Fact]
    public void CEP030_ProjectionOnNonCrestService_ReportsError()
    {
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CapabilityCompatibilityProjection]
            public class BookAppService
            {
                public System.Threading.Tasks.Task<string> CreateAsync(string input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult(input);
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        result.Diagnostics.Should().Contain(d =>
            d.Id == "CEP030" &&
            d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    }

    private static string[] BuildCompatibilityStubs()
    {
        return new[]
        {
            // Attribute stubs
            """
            namespace CrestCreates.Domain.Shared.Attributes
            {
                [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = false)]
                public class CrestServiceAttribute : System.Attribute { }

                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method, AllowMultiple = false)]
                public sealed class CapabilityCompatibilityProjectionAttribute : System.Attribute
                {
                    public string? CapabilityIdPrefix { get; init; }
                    public string? RoutePrefix { get; init; }
                }

                [System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = false)]
                public sealed class CapabilityCompatibilityIgnoreAttribute : System.Attribute { }

                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method, AllowMultiple = false)]
                public sealed class DynamicApiIgnoreAttribute : System.Attribute { }

                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Interface, AllowMultiple = true)]
                public sealed class DynamicApiRouteAttribute : System.Attribute
                {
                    public DynamicApiRouteAttribute(string template) { Template = template; }
                    public string Template { get; }
                }
            }
            """,
            // CapabilityDescriptor stub
            """
            using CrestCreates.Metadata.Abstractions;
            using CrestCreates.Metadata.Abstractions.DescriptorCapability;
            using CrestCreates.Schema.Abstractions;

            namespace CrestCreates.Metadata
            {
                public sealed class CapabilityDescriptor : IDescriptor, IVersionedDescriptor
                {
                    public string Namespace { get; init; } = "capability";
                    public string Id { get; init; } = string.Empty;
                    public string Name { get; init; } = string.Empty;
                    public DescriptorKind Kind => DescriptorKind.Capability;
                    public DescriptorState State { get; init; } = DescriptorState.Active;
                    public string? SupersededById { get; init; }
                    public int Version { get; init; }
                    public System.Collections.Generic.IReadOnlyList<string> Categories { get; init; } = System.Array.Empty<string>();
                    public System.Collections.Generic.IReadOnlyList<EventRef> Produces { get; init; } = System.Array.Empty<EventRef>();
                    public System.Collections.Generic.IReadOnlyList<EventRef> Consumes { get; init; } = System.Array.Empty<EventRef>();
                    public System.Collections.Generic.IReadOnlyList<string> SemanticTags { get; init; } = System.Array.Empty<string>();
                    public CapabilityKind CapabilityKind { get; init; }
                    public VersionedDescriptorRef<SchemaDescriptor>? InputSchema { get; init; }
                    public VersionedDescriptorRef<SchemaDescriptor>? OutputSchema { get; init; }
                    public System.Collections.Generic.IReadOnlyList<string> Permissions { get; init; } = System.Array.Empty<string>();
                    public CapabilityRiskLevel RiskLevel { get; init; } = CapabilityRiskLevel.Medium;
                    public CapabilityProjectionKind ProjectionKind { get; init; } = CapabilityProjectionKind.Native;
                }

                public readonly record struct EventRef(string Namespace, string Id, int? Version = null) : IDescriptorRef;
            }
            """,
            // SchemaDescriptor stub (needed for CapabilityDescriptor.InputSchema type)
            """
            namespace CrestCreates.Schema.Abstractions
            {
                public sealed class SchemaDescriptor : CrestCreates.Metadata.Abstractions.IDescriptor, CrestCreates.Metadata.Abstractions.IVersionedDescriptor
                {
                    public string Namespace { get; init; } = "schema";
                    public string Id { get; init; } = string.Empty;
                    public string Name { get; init; } = string.Empty;
                    public CrestCreates.Metadata.Abstractions.DescriptorKind Kind => CrestCreates.Metadata.Abstractions.DescriptorKind.Schema;
                    public CrestCreates.Metadata.Abstractions.DescriptorState State { get; init; } = CrestCreates.Metadata.Abstractions.DescriptorState.Active;
                    public string? SupersededById { get; init; }
                    public int Version { get; init; }
                }
            }
            """,
            // DescriptorProviderRegistry stub
            """
            using System.Collections.Concurrent;
            using System.Collections.Generic;
            using System.Linq;
            using CrestCreates.Metadata.Abstractions;

            namespace CrestCreates.Metadata
            {
                public static class DescriptorProviderRegistry
                {
                    private static readonly ConcurrentBag<object> _providers = new();

                    public static void Register<T>(IDescriptorProvider<T> provider) where T : class, IDescriptor
                        => _providers.Add(provider);

                    public static IReadOnlyList<IDescriptorProvider<T>> GetProviders<T>() where T : class, IDescriptor
                        => _providers.OfType<IDescriptorProvider<T>>().ToList();
                }
            }
            """,
            // IDescriptorProvider stub
            """
            using System.Collections.Generic;
            using CrestCreates.Metadata.Abstractions;

            namespace CrestCreates.Metadata.Abstractions
            {
                public interface IDescriptorProvider<TDescriptor>
                    where TDescriptor : IDescriptor
                {
                    IReadOnlyList<TDescriptor> GetDescriptors();
                }
            }
            """,
            // Metadata.Abstractions stubs
            """
            namespace CrestCreates.Metadata.Abstractions
            {
                public interface IDescriptor
                {
                    string Namespace { get; }
                    string Id { get; }
                    string Name { get; }
                    DescriptorKind Kind { get; }
                    DescriptorState State { get; }
                    string? SupersededById { get; }
                }

                public interface IVersionedDescriptor : IDescriptor
                {
                    int Version { get; }
                }

                public interface IDescriptorRef { }

                public enum DescriptorKind
                {
                    Capability = 1,
                    Schema = 2,
                    DynamicApiEndpoint = 3,
                    Workflow = 4,
                    HumanTask = 5,
                    Interaction = 6
                }

                public enum DescriptorState
                {
                    Draft = 0,
                    Active = 1,
                    Deprecated = 2,
                    Removed = 3
                }

                public readonly record struct VersionedDescriptorRef<TDescriptor>(
                    string Id,
                    int Version,
                    VersionSelectionMode SelectionMode = VersionSelectionMode.Exact,
                    string? ExpectedContractHash = null
                ) where TDescriptor : IVersionedDescriptor;

                public enum VersionSelectionMode
                {
                    Exact = 0,
                    Latest = 1,
                    ExactOrLatest = 2
                }
            }
            """,
            // DescriptorCapability stubs
            """
            namespace CrestCreates.Metadata.Abstractions.DescriptorCapability
            {
                public enum CapabilityKind
                {
                    Query = 1,
                    Command = 2
                }

                public enum CapabilityRiskLevel
                {
                    Low = 0,
                    Medium = 1,
                    High = 2,
                    Critical = 3
                }

                public enum CapabilityProjectionKind
                {
                    Native = 0,
                    AppServiceCompatibility = 1
                }
            }
            """,
            // Capability.Abstractions stubs
            """
            namespace CrestCreates.Capability.Abstractions
            {
                public sealed class CapabilityExecutionContext
                {
                    public string CapabilityId { get; init; } = string.Empty;
                    public string CapabilityName { get; init; } = string.Empty;
                    public int CapabilityVersion { get; init; }
                    public string CapabilityContractHash { get; init; } = string.Empty;
                    public InvocationSource InvocationSource { get; set; }
                    public string CorrelationId { get; init; } = System.Guid.NewGuid().ToString("N");
                    public string? CausationId { get; set; }
                    public string? TenantId { get; set; }
                    public string? UserId { get; set; }
                    public string IdempotencyKey { get; set; } = System.Guid.NewGuid().ToString("N");
                    public object? Input { get; set; }
                    public System.DateTimeOffset StartedAt { get; set; } = System.DateTimeOffset.UtcNow;
                    public System.Collections.Generic.IDictionary<string, object?> Items { get; init; } = new System.Collections.Generic.Dictionary<string, object?>();
                    public System.Collections.Generic.IReadOnlyList<string> RequiredPermissions { get; set; } = System.Array.Empty<string>();
                    public System.Threading.CancellationToken CancellationToken { get; init; }
                    public System.IServiceProvider ServiceProvider { get; init; } = null!;
                }

                public enum InvocationSource
                {
                    Http = 0,
                    Message = 1,
                    Scheduled = 2,
                    Internal = 3,
                    Agent = 4
                }

                public interface ICapabilityHandlerInvoker
                {
                    System.Threading.Tasks.Task<object?> InvokeAsync(object? input, System.Threading.CancellationToken ct);
                }

                public interface ICapabilityContextAwareHandlerInvoker : ICapabilityHandlerInvoker
                {
                    System.Threading.Tasks.Task<object?> InvokeAsync(CapabilityExecutionContext context, System.Threading.CancellationToken ct);
                }
            }
            """,
            // CapabilityHandlerResolverProvider stub
            """
            namespace CrestCreates.Capability.Abstractions
            {
                public static class CapabilityHandlerResolverProvider
                {
                    private static readonly System.Collections.Generic.Dictionary<string, CrestCreates.Capability.Abstractions.ICapabilityHandlerInvoker> _handlers = new();

                    public static void Register(string capabilityId, CrestCreates.Capability.Abstractions.ICapabilityHandlerInvoker invoker)
                    {
                        _handlers[capabilityId] = invoker;
                    }
                }
            }
            """,

            // CapabilityEndpoint types stubs
            """
            namespace CrestCreates.DynamicApi
            {
                public sealed class CapabilityEndpointDescriptor : CrestCreates.Metadata.Abstractions.IDescriptor, CrestCreates.Metadata.Abstractions.IVersionedDescriptor
                {
                    public string Namespace => "dynamic-api-endpoint";
                    public CrestCreates.Metadata.Abstractions.DescriptorKind Kind => CrestCreates.Metadata.Abstractions.DescriptorKind.DynamicApiEndpoint;
                    public string Id { get; init; } = string.Empty;
                    public string Name { get; init; } = string.Empty;
                    public int Version { get; init; }
                    public CrestCreates.Metadata.Abstractions.DescriptorState State { get; init; } = CrestCreates.Metadata.Abstractions.DescriptorState.Active;
                    public string? SupersededById { get; init; }
                    public required CrestCreates.Metadata.Abstractions.VersionedDescriptorRef<CrestCreates.Metadata.CapabilityDescriptor> Capability { get; init; }
                    public CapabilityEndpointHttpMethod HttpMethod { get; init; }
                    public string RoutePattern { get; init; } = string.Empty;
                    public CapabilityEndpointAuthorizationMode AuthorizationMode { get; init; } = CapabilityEndpointAuthorizationMode.InheritCapability;
                    public System.Collections.Generic.IReadOnlyList<CapabilityEndpointInputBinding> InputBindings { get; init; } = System.Array.Empty<CapabilityEndpointInputBinding>();
                    public CapabilityEndpointOutputMapping OutputMapping { get; init; } = new();
                    public CapabilityEndpointProjectionMetadata Projection { get; init; } = new();
                }

                public interface ICapabilityEndpointDescriptorProvider : CrestCreates.Metadata.Abstractions.IDescriptorProvider<CapabilityEndpointDescriptor> { }

                public enum CapabilityEndpointHttpMethod
                {
                    None = 0,
                    Get = 1,
                    Post = 2,
                    Put = 3,
                    Patch = 4,
                    Delete = 5
                }

                public enum CapabilityEndpointParameterSource
                {
                    Route = 0,
                    Query = 1,
                    Header = 2,
                    Body = 3
                }

                public enum CapabilityEndpointAuthorizationMode
                {
                    InheritCapability = 0,
                    Explicit = 1,
                    Anonymous = 2
                }

                public sealed record CapabilityEndpointInputBinding
                {
                    public string Name { get; init; } = string.Empty;
                    public CapabilityEndpointParameterSource Source { get; init; }
                    public string? CapabilityInputPath { get; init; }
                    public bool Required { get; init; } = true;
                }

                public sealed record CapabilityEndpointOutputMapping
                {
                    public int SuccessStatusCode { get; init; } = 200;
                    public string? ContentType { get; init; }
                }

                public sealed record CapabilityEndpointProjectionMetadata
                {
                    public string? OperationId { get; init; }
                    public string? GroupName { get; init; }
                    public System.Collections.Generic.IReadOnlyList<string> Tags { get; init; } = System.Array.Empty<string>();
                    public string? Summary { get; init; }
                    public string? Description { get; init; }
                    public bool Deprecated { get; init; }
                    public CapabilityEndpointVisibility Visibility { get; init; } = CapabilityEndpointVisibility.Public;
                }

                public enum CapabilityEndpointVisibility
                {
                    Public = 0,
                    Internal = 1,
                    Private = 2
                }

                public sealed record CapabilityEndpointBindingContract(
                    string EndpointId,
                    int EndpointVersion,
                    System.Func<Microsoft.AspNetCore.Http.HttpContext, System.Threading.CancellationToken, System.Threading.Tasks.ValueTask<object?>> BindInputAsync);

                public static class CapabilityEndpointBindingRegistry
                {
                    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string, int), CapabilityEndpointBindingContract> _bindings = new();

                    public static void Register(CapabilityEndpointBindingContract contract)
                    {
                        _bindings.TryAdd((contract.EndpointId, contract.EndpointVersion), contract);
                    }
                }

                public static class CapabilityEndpointJsonRuntime
                {
                    public static async System.Threading.Tasks.ValueTask<T?> ReadBodyAsync<T>(
                        Microsoft.AspNetCore.Http.HttpContext context, bool optional, System.Threading.CancellationToken ct = default)
                    {
                        return default;
                    }

                    public static async System.Threading.Tasks.ValueTask<T?> ReadBodyAsync<T>(
                        Microsoft.AspNetCore.Http.HttpContext context, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> jsonTypeInfo, bool optional, System.Threading.CancellationToken ct = default)
                    {
                        return default;
                    }
                }
            }
            """,
            // AppServiceCompatibilityProjectionEntry stub
            """
            using CrestCreates.Metadata.Abstractions.DescriptorCapability;

            namespace CrestCreates.DynamicApi.Abstractions
            {
                public sealed record AppServiceCompatibilityProjectionEntry
                {
                    public string SourceService { get; init; } = string.Empty;
                    public string SourceMethod { get; init; } = string.Empty;
                    public string CapabilityId { get; init; } = string.Empty;
                    public string EndpointId { get; init; } = string.Empty;
                    public string HttpMethod { get; init; } = string.Empty;
                    public string RoutePattern { get; init; } = string.Empty;
                    public System.Collections.Generic.IReadOnlyList<string> PermissionNames { get; init; } = System.Array.Empty<string>();
                    public string InvokerTypeName { get; init; } = string.Empty;
                    public CapabilityProjectionKind ProjectionKind { get; init; }
                }
            }
            """,
            // IAppServiceCompatibilityProjectionManifestProvider stub
            """
            using System.Collections.Generic;

            namespace CrestCreates.DynamicApi.Abstractions
            {
                public interface IAppServiceCompatibilityProjectionManifestProvider
                {
                    IReadOnlyList<AppServiceCompatibilityProjectionEntry> GetEntries();
                }
            }
            """,
            // System.Text.Json stubs (replace real assembly to avoid partial class issues with JsonSerializerContext)
            """
            namespace System.Text.Json
            {
                public class JsonSerializerOptions { }
            }
            """,
            """
            namespace System.Text.Json.Serialization
            {
                [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
                public sealed class JsonSerializableAttribute : System.Attribute
                {
                    public JsonSerializableAttribute(System.Type type) { }
                    public string? TypeInfoPropertyName { get; set; }
                }

                public class JsonSerializerContext
                {
                    protected JsonSerializerContext(System.Text.Json.JsonSerializerOptions? options = null) { }
                    public System.Text.Json.JsonSerializerOptions? Options { get; set; }
                    public virtual System.Text.Json.JsonSerializerOptions GeneratedSerializerOptions => Options!;
                    public virtual System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(System.Type type) => null;
                }
            }
            """,
            """
            namespace System.Text.Json.Serialization.Metadata
            {
                public class JsonTypeInfo { }
                public sealed class JsonTypeInfo<T> : JsonTypeInfo { }
            }
            """,
        };
    }

    [Fact]
    public void QueryObject_WithParameterNameCollidingInternalVarName_CompilesSuccessfully()
    {
        // Regression: parameter named "qv0" must not collide with internal out-var naming (qv0, qv1, ...)
        // Two parameters (route + query DTO) forces the multi-param path where
        // localVarName = Camelize("Qv0") = "qv0" would collide with old out var qv0.
        // Old implementation: CS0128 "A local variable named 'qv0' is already defined".
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            [CapabilityCompatibilityProjection]
            public class SearchAppService
            {
                public System.Threading.Tasks.Task<string> GetListAsync(System.Guid id, MyApp.SearchFilter qv0, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult("ok");
            }

            public class SearchFilter
            {
                public string? Keyword { get; set; }
                public int PageSize { get; set; } = 10;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        result.CompilationSuccess.Should().BeTrue();
    }
}
