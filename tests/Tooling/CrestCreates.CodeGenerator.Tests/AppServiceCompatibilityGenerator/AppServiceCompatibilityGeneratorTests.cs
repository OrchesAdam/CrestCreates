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

        // Result contracts should also be generated
        var contracts = result.GetSourceByFileName("GeneratedAppServiceCompatibilityResultContracts_Book.g.cs");
        contracts.Should().NotBeNull();
        contracts!.SourceText.Should().Contain("CapabilityEndpointResultContractRegistration.Register");
        contracts.SourceText.Should().Contain("CompatibilityHttpResultMapper.WrapResult");

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
        manifest.SourceText.Should().Contain("[ModuleInitializer]");
        manifest.SourceText.Should().Contain("AppServiceCompatibilityProjectionManifestRegistry.Register");
        manifest.SourceText.Should().Contain("System.Runtime.CompilerServices");

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
        // P1-4: Compatibility binding uses CapabilityEndpointBodyReader.ReadCompatibilityBodyAsync for AOT-safe body binding.
        bindings!.SourceText.Should().Contain("CapabilityEndpointBodyReader.ReadCompatibilityBodyAsync");
        bindings.SourceText.Should().NotContain("CompatibilityBodyReader.ReadBodyAsync");
        bindings.SourceText.Should().NotContain("CapabilityEndpointJsonRuntime");
        bindings.SourceText.Should().NotContain("CompatibilityJsonContext");

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
            // All generated code must reference only the neutral CompatibilityHttpResultMapper
            // and CompatibilityBodyReader, never the legacy DynamicApiGeneratedRuntime.
            generated.SourceText.Should().NotContain("DynamicApiGeneratedRuntime");
            generated.SourceText.Should().NotContain("DynamicApiGeneratedRegistryStore");
            generated.SourceText.Should().NotContain("IDynamicApiGeneratedProvider");
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

                public static class CapabilityEndpointResultContractRegistration
                {
                    public static void Register(string endpointId, int version, System.Func<EndpointExecutionContext, object> mapResult) { }
                }

                public sealed class EndpointExecutionContext
                {
                    public object? Output { get; init; }
                }

                public static class CompatibilityHttpResultMapper
                {
                    public static object WrapResult(object? value) => null!;
                    public static object WrapGetResult(object? value) => null!;
                    public static object WrapVoidResult() => null!;
                }

                public static class CompatibilityBodyReader
                {
                    [System.Obsolete("Use CapabilityEndpointBodyReader instead.")]
                    public static async System.Threading.Tasks.Task<T?> ReadBodyAsync<T>(
                        Microsoft.AspNetCore.Http.HttpContext context, bool optional)
                        where T : new()
                    {
                        return default;
                    }
                }

                // AOT-safe body binding components (8d compatibility)
                public static class CapabilityEndpointJsonTypeInfoResolver
                {
                    public static System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>? Resolve<T>(
                        Microsoft.AspNetCore.Http.HttpContext context) => null;
                }

                public static class CapabilityEndpointBodyReader
                {
                    public static async System.Threading.Tasks.ValueTask<T?> ReadNativeBodyAsync<T>(
                        Microsoft.AspNetCore.Http.HttpContext context,
                        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> jsonTypeInfo,
                        bool optional,
                        System.Threading.CancellationToken ct = default)
                    {
                        return default;
                    }

                    public static async System.Threading.Tasks.ValueTask<T?> ReadCompatibilityBodyAsync<T>(
                        Microsoft.AspNetCore.Http.HttpContext context,
                        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> jsonTypeInfo,
                        System.Func<T> emptyBodyFactory,
                        bool optional,
                        System.Threading.CancellationToken ct = default)
                    {
                        return default;
                    }
                }

                public static class CapabilityEndpointJsonContractRegistry
                {
                    public static void RegisterBodyType(System.Type bodyType) { }
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
            // AppServiceCompatibilityProjectionManifestRegistry stub
            """
            using System.Collections.Generic;

            namespace CrestCreates.DynamicApi.Abstractions
            {
                public static class AppServiceCompatibilityProjectionManifestRegistry
                {
                    private static readonly List<IAppServiceCompatibilityProjectionManifestProvider> _providers = new();

                    public static void Register(IAppServiceCompatibilityProjectionManifestProvider provider)
                    {
                        _providers.Add(provider);
                    }

                    public static IReadOnlyList<IAppServiceCompatibilityProjectionManifestProvider> GetProviders() => _providers;
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

    [Fact]
    public void DynamicApiRouteOnInterface_ResolvedByCompatibilityGenerator()
    {
        // P1-1: [DynamicApiRoute] on the interface should be picked up by the
        // compatibility generator via ResolveServiceRoute fallback.
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [DynamicApiRoute("custom-books-route")]
            public interface ICustomRouteAppService
            {
                System.Threading.Tasks.Task<string> CreateAsync(string input, System.Threading.CancellationToken ct);
            }

            [CrestService]
            [CapabilityCompatibilityProjection]
            public class CustomRouteAppService : ICustomRouteAppService
            {
                public System.Threading.Tasks.Task<string> CreateAsync(string input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult(input);
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        result.CompilationSuccess.Should().BeTrue();

        var endpoints = result.GetSourceByFileName("GeneratedAppServiceCompatibilityEndpoints_CustomRoute.g.cs");
        endpoints.Should().NotBeNull();
        endpoints!.SourceText.Should().Contain("custom-books-route",
            "route from interface [DynamicApiRoute] should be used instead of default api/");
        endpoints.SourceText.Should().NotContain("api/custom-route",
            "should not use default 'api/' prefix when custom route is on interface");
    }

    [Fact]
    public void CEP035_DefaultRoutePrefix_EmitsWarning()
    {
        // P1-1: When no explicit RoutePrefix is set and no [DynamicApiRoute] attribute
        // is present, the generator uses the default "api/" prefix. CEP035 should warn
        // that this may not match a custom DynamicApiOptions.DefaultRoutePrefix.
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            [CapabilityCompatibilityProjection]
            public class SimpleAppService
            {
                public System.Threading.Tasks.Task<string> CreateAsync(string input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult(input);
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        result.Diagnostics.Should().Contain(d =>
            d.Id == "CEP035" &&
            d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Warning,
            "CEP035 should be emitted when default 'api/' prefix is used");

        result.CompilationSuccess.Should().BeTrue();
    }

    [Fact]
    public void CEP035_NotEmittedWhenRoutePrefixExplicit()
    {
        // CEP035 should NOT be emitted when RoutePrefix is explicitly set.
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            [CapabilityCompatibilityProjection(RoutePrefix = "v2/books")]
            public class ExplicitRouteAppService
            {
                public System.Threading.Tasks.Task<string> CreateAsync(string input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult(input);
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        result.Diagnostics.Should().NotContain(d => d.Id == "CEP035",
            "CEP035 should not be emitted when RoutePrefix is explicitly set");
        result.CompilationSuccess.Should().BeTrue();
    }

    [Fact]
    public void CEP035_NotEmittedWhenDynamicApiRoutePresent()
    {
        // CEP035 should NOT be emitted when [DynamicApiRoute] is on the class.
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            [CapabilityCompatibilityProjection]
            [DynamicApiRoute("custom-prefix")]
            public class CustomPrefixAppService
            {
                public System.Threading.Tasks.Task<string> CreateAsync(string input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult(input);
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        result.Diagnostics.Should().NotContain(d => d.Id == "CEP035",
            "CEP035 should not be emitted when [DynamicApiRoute] provides custom prefix");
        result.CompilationSuccess.Should().BeTrue();
    }

    [Fact]
    public void CEP034_OverloadedMethods_ReportsError()
    {
        // Overloaded methods create duplicate CapabilityId, EndpointId,
        // binding method names, and invoker class names. The generator must
        // detect this and report CEP034 errors, suppressing generation.
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            [CapabilityCompatibilityProjection]
            public class BookAppService
            {
                public System.Threading.Tasks.Task<string> CreateAsync(string name, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult(name);

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

        // Should produce CEP034 errors — at least 2 (one per overloaded method)
        result.Diagnostics.Should().Contain(d => d.Id == "CEP034");
        result.Diagnostics.Count(d => d.Id == "CEP034").Should().BeGreaterThanOrEqualTo(2);

        // Compilation should succeed (no broken code generated)
        if (!result.CompilationSuccess)
        {
            var errors = result.GetErrors().ToList();
            throw new System.Exception($"CEP034 Compilation failed with {errors.Count} errors. Errors:\n{string.Join("\n", errors.Select(e => $"{e.Id}: {e.GetMessage()} at {e.Location}"))}");
        }
        result.CompilationSuccess.Should().BeTrue("generated code must compile successfully");
    }

    [Fact]
    public void CEP036_MethodLevelCapabilityIdPrefix_EmitsWarning()
    {
        // CapabilityIdPrefix and RoutePrefix are service-level properties.
        // Setting them on a method-level [CapabilityCompatibilityProjection] should
        // produce CEP036 warning, and the value should be ignored.
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            public class BookAppService
            {
                [CapabilityCompatibilityProjection(CapabilityIdPrefix = "catalog.book")]
                public System.Threading.Tasks.Task<string> CreateAsync(string input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult(input);
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        result.Diagnostics.Should().Contain(d => d.Id == "CEP036"
            && d.GetMessage().Contains("CapabilityIdPrefix"),
            "CEP036 should warn when CapabilityIdPrefix is set on method-level attribute");
    }

    [Fact]
    public void CEP036_MethodLevelRoutePrefix_EmitsWarning()
    {
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            public class BookAppService
            {
                [CapabilityCompatibilityProjection(RoutePrefix = "v2/books")]
                public System.Threading.Tasks.Task<string> CreateAsync(string input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult(input);
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        result.Diagnostics.Should().Contain(d => d.Id == "CEP036"
            && d.GetMessage().Contains("RoutePrefix"),
            "CEP036 should warn when RoutePrefix is set on method-level attribute");
    }

    [Fact]
    public void CEP036_NotEmittedForClassLevelProperties()
    {
        // Class-level CapabilityIdPrefix/RoutePrefix should NOT produce CEP036.
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            [CapabilityCompatibilityProjection(CapabilityIdPrefix = "catalog.book", RoutePrefix = "v2/books")]
            public class BookAppService
            {
                public System.Threading.Tasks.Task<string> CreateAsync(string input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult(input);
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        result.Diagnostics.Should().NotContain(d => d.Id == "CEP036",
            "CEP036 should not be emitted for class-level CapabilityIdPrefix/RoutePrefix");
    }

    [Fact]
    public void CEP036_ClassAndMethodLevel_Coexist_EmitsWarning()
    {
        // When class-level projection exists and a method also has [CapabilityCompatibilityProjection]
        // with RoutePrefix/CapabilityIdPrefix, CEP036 should still fire.
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            [CapabilityCompatibilityProjection]
            public class BookAppService
            {
                [CapabilityCompatibilityProjection(RoutePrefix = "v2/books")]
                public System.Threading.Tasks.Task<string> CreateAsync(string input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult(input);
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources:BuildCompatibilityStubs());

        result.Diagnostics.Should().Contain(d => d.Id == "CEP036"
            && d.GetMessage().Contains("RoutePrefix"),
            "CEP036 should fire even when class-level projection also exists");
    }

    [Fact]
    public void ClassProjection_InterfaceCapabilityCompatibilityIgnore_SkipsMethod()
    {
        // [CapabilityCompatibilityIgnore] on an interface method should be respected
        // by the compatibility generator, even when the implementation method has no attribute.
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            public interface IBookAppService
            {
                [CapabilityCompatibilityIgnore]
                System.Threading.Tasks.Task<string> InternalSyncAsync(System.Threading.CancellationToken ct);
            }

            [CrestService]
            [CapabilityCompatibilityProjection]
            public class BookAppService : IBookAppService
            {
                public System.Threading.Tasks.Task<string> InternalSyncAsync(System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult("sync");
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        result.CompilationSuccess.Should().BeTrue();
        // InternalSyncAsync should NOT appear in generated code
        foreach (var generated in result.GeneratedSources)
        {
            generated.SourceText.Should().NotContain("InternalSync",
                "Interface [CapabilityCompatibilityIgnore] should suppress generation");
        }
    }

    [Fact]
    public void ClassProjection_InterfaceDynamicApiIgnore_SkipsMethod()
    {
        // [DynamicApiIgnore] on an interface method should be respected
        // by the compatibility generator, even when the implementation method has no attribute.
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            public interface IBookAppService
            {
                [DynamicApiIgnore]
                System.Threading.Tasks.Task<string> RebuildIndexAsync(System.Threading.CancellationToken ct);
            }

            [CrestService]
            [CapabilityCompatibilityProjection]
            public class BookAppService : IBookAppService
            {
                public System.Threading.Tasks.Task<string> RebuildIndexAsync(System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult("rebuilt");
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        result.CompilationSuccess.Should().BeTrue();
        foreach (var generated in result.GeneratedSources)
        {
            generated.SourceText.Should().NotContain("RebuildIndex",
                "Interface [DynamicApiIgnore] should suppress generation");
        }
    }

    [Fact]
    public void CEP031_InterfaceProjectionWithDynamicApiIgnore_ReportsConflict()
    {
        // When a method has both [CapabilityCompatibilityProjection] and [DynamicApiIgnore]
        // on the interface, CEP031 should be reported.
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            public interface IBookAppService
            {
                [CapabilityCompatibilityProjection]
                [DynamicApiIgnore]
                System.Threading.Tasks.Task<string> ConflictedAsync(System.Threading.CancellationToken ct);
            }

            [CrestService]
            public class BookAppService : IBookAppService
            {
                public System.Threading.Tasks.Task<string> ConflictedAsync(System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult("conflict");
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        result.Diagnostics.Should().Contain(d => d.Id == "CEP031",
            "CEP031 should fire when interface method has both [CapabilityCompatibilityProjection] and [DynamicApiIgnore]");
    }

    [Fact]
    public void CEP037_RecordWithPrimaryConstructor_ReportsError()
    {
        // Records with primary constructors have no implicit parameterless constructor,
        // so they don't satisfy the new() constraint required by CompatibilityBodyReader.
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            [CapabilityCompatibilityProjection]
            public class BookAppService
            {
                public System.Threading.Tasks.Task<string> CreateAsync(CreateBookRequest input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult(input.Title);
            }

            public sealed record CreateBookRequest(string Title);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        result.Diagnostics.Should().Contain(d => d.Id == "CEP037"
            && d.GetMessage().Contains("CreateBookRequest"),
            "CEP037 should fire for record with primary constructor (no parameterless ctor)");
        // Fail-closed: no code should be generated for actions with CEP037
        result.GeneratedSources.Should().NotContain(x =>
            x.SourceText.Contains("ReadCompatibilityBodyAsync<CreateBookRequest>"),
            "CEP037 actions should not generate ReadCompatibilityBodyAsync calls");
    }

    [Fact]
    public void CEP037_AbstractBodyType_ReportsError()
    {
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            [CapabilityCompatibilityProjection]
            public class BookAppService
            {
                public System.Threading.Tasks.Task<string> CreateAsync(AbstractRequest input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult("ok");
            }

            public abstract class AbstractRequest { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        result.Diagnostics.Should().Contain(d => d.Id == "CEP037"
            && d.GetMessage().Contains("AbstractRequest"),
            "CEP037 should fire for abstract body type");
        // Fail-closed: no code should be generated for actions with CEP037
        result.GeneratedSources.Should().NotContain(x =>
            x.SourceText.Contains("ReadCompatibilityBodyAsync<AbstractRequest>"),
            "CEP037 actions should not generate ReadCompatibilityBodyAsync calls");
    }

    [Fact]
    public void CEP037_NotEmittedForValidBodyType()
    {
        // A class with a parameterless constructor should NOT produce CEP037.
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            [CapabilityCompatibilityProjection]
            public class BookAppService
            {
                public System.Threading.Tasks.Task<string> CreateAsync(CreateBookDto input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult(input.Title);
            }

            public class CreateBookDto
            {
                public string Title { get; set; } = "";
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        result.Diagnostics.Should().NotContain(d => d.Id == "CEP037",
            "CEP037 should not be emitted for body type with parameterless constructor");
    }

    [Fact]
    public void CEP037_ClosedGenericBodyType_NoDiagnostic()
    {
        // Closed generic types like List<BookDto> have public parameterless constructors
        // and satisfy the new() constraint.
        var source = """
            using System.Collections.Generic;
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            [CapabilityCompatibilityProjection]
            public class BookAppService
            {
                public System.Threading.Tasks.Task<string> ImportAsync(List<BookDto> input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult("ok");
            }

            public class BookDto
            {
                public string Title { get; set; } = "";
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        result.Diagnostics.Should().NotContain(d => d.Id == "CEP037",
            "CEP037 should not be emitted for closed generic body type (List<BookDto>)");
        result.CompilationSuccess.Should().BeTrue(
            "closed generic body type should produce compilable generated code");
    }

    [Fact]
    public void CEP037_ClosedGenericDtoBodyType_NoDiagnostic()
    {
        // A custom generic DTO with a concrete type argument and parameterless ctor
        // should also pass the new() constraint check.
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            [CapabilityCompatibilityProjection]
            public class BookAppService
            {
                public System.Threading.Tasks.Task<string> SaveAsync(CreateRequest<BookDto> input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult("ok");
            }

            public class CreateRequest<T>
            {
                public T? Data { get; set; }
            }

            public class BookDto
            {
                public string Title { get; set; } = "";
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        result.Diagnostics.Should().NotContain(d => d.Id == "CEP037",
            "CEP037 should not be emitted for closed generic DTO body type (CreateRequest<BookDto>)");
        result.CompilationSuccess.Should().BeTrue(
            "closed generic DTO body type should produce compilable generated code");
    }

    [Fact]
    public void CEP037_ArrayBodyType_NoDiagnostic()
    {
        // Single-dimensional arrays now pass because the generator uses
        // Array.Empty<T>() instead of new T[] for the emptyBodyFactory.
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            [CapabilityCompatibilityProjection]
            public class BookAppService
            {
                public System.Threading.Tasks.Task<string> AddAsync(BookDto[] input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult("ok");
            }

            public class BookDto
            {
                public string Title { get; set; } = "";
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        result.Diagnostics.Should().NotContain(d => d.Id == "CEP037",
            "CEP037 should not fire for single-dimensional array body type");
        result.CompilationSuccess.Should().BeTrue(
            "single-dimensional array body type should produce compilable generated code");
    }

    [Fact]
    public void CEP037_InterfaceBodyType_ReportsError()
    {
        // Interface types cannot satisfy the new() constraint.
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            [CapabilityCompatibilityProjection]
            public class BookAppService
            {
                public System.Threading.Tasks.Task<string> CreateAsync(IBookRequest input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult("ok");
            }

            public interface IBookRequest
            {
                string Title { get; set; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        result.Diagnostics.Should().Contain(d => d.Id == "CEP037"
            && d.GetMessage().Contains("IBookRequest"),
            "CEP037 should fire for interface body type");
        // Fail-closed: no code should be generated for actions with CEP037
        result.GeneratedSources.Should().NotContain(x =>
            x.SourceText.Contains("ReadCompatibilityBodyAsync<IBookRequest>"),
            "CEP037 actions should not generate ReadCompatibilityBodyAsync calls");
    }

    [Fact]
    public void ServiceLevelFailClosed_ErrorDiagnosticSkipsEntireService()
    {
        // When a service has one method with CEP037 (Error-level) and one valid method,
        // the entire service's code generation should be skipped (fail-closed).
        // This freezes the current behavior: service-level fail-closed, not per-action.
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            [CapabilityCompatibilityProjection]
            public class BookAppService
            {
                public System.Threading.Tasks.Task<string> GetAsync(string name, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult(name);

                public System.Threading.Tasks.Task<string> AddAsync(AbstractRequest input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult("ok");
            }

            public abstract class AbstractRequest { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        result.Diagnostics.Should().Contain(d => d.Id == "CEP037",
            "CEP037 should fire for the abstract body type method");
        // Service-level fail-closed: no generated code for the entire service
        result.GeneratedSources.Should().NotContain(x =>
            x.SourceText.Contains("BookAppService") ||
            x.SourceText.Contains("GetAsync") ||
            x.SourceText.Contains("AddAsync"),
            "entire service should be skipped when any Error diagnostic exists");
    }

    [Fact]
    public void NoParamMethod_DoesNotGenerateEnvelopeClass()
    {
        // Regression: methods with only a CancellationToken parameter should not
        // produce empty envelope class declarations (CS1001).
        // The filter in the endpoint emitter must use EnvelopeTypeName is not null.
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            [CapabilityCompatibilityProjection]
            public class BookAppService
            {
                public System.Threading.Tasks.Task<string> ListAsync(System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult("ok");
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        result.CompilationSuccess.Should().BeTrue("generated code must compile successfully");

        var bindings = result.GetSourceByFileName("GeneratedAppServiceCompatibilityBindings_Book.g.cs");
        bindings.Should().NotBeNull();
        // Must not contain empty class declaration: "internal sealed class" would be
        // followed by the class name if there was one. A no-name class is the bug.
        bindings!.SourceText.Should().NotContain("internal sealed class ");

        // Result contracts should still be generated for no-param methods
        var contracts = result.GetSourceByFileName("GeneratedAppServiceCompatibilityResultContracts_Book.g.cs");
        contracts.Should().NotBeNull();
        contracts!.SourceText.Should().Contain("CapabilityEndpointResultContractRegistration.Register");
    }

    [Fact]
    public void ResultContracts_GeneratedForAllActionTypes()
    {
        // Verify that result contract registration is generated for all action types:
        // POST non-void, GET non-void, void return.
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            [CapabilityCompatibilityProjection]
            public class BookAppService
            {
                public System.Threading.Tasks.Task<string> CreateAsync(string input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult(input);

                public System.Threading.Tasks.Task<string> GetAsync(string name, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult(name);

                public System.Threading.Tasks.Task DeleteAsync(string name, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.CompletedTask;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        result.CompilationSuccess.Should().BeTrue();

        var contracts = result.GetSourceByFileName("GeneratedAppServiceCompatibilityResultContracts_Book.g.cs");
        contracts.Should().NotBeNull();
        contracts!.SourceText.Should().Contain("CompatibilityHttpResultMapper.WrapResult(ctx.Output)",
            "POST non-void actions should use WrapResult");
        contracts.SourceText.Should().Contain("CompatibilityHttpResultMapper.WrapGetResult(ctx.Output)",
            "GET non-void actions should use WrapGetResult");
        contracts.SourceText.Should().Contain("CompatibilityHttpResultMapper.WrapVoidResult()",
            "void-return actions should use WrapVoidResult");
    }

    [Fact]
    public void MethodLevelProjection_OnInterfaceMethod_ProjectsAction()
    {
        // P0-2 fix: [CapabilityCompatibilityProjection] on interface method should be discovered
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            public interface IBookAppService
            {
                [CapabilityCompatibilityProjection]
                System.Threading.Tasks.Task<string> CreateAsync(string input, System.Threading.CancellationToken ct);

                System.Threading.Tasks.Task<string> GetAsync(string name, System.Threading.CancellationToken ct);
            }

            [CrestService]
            public class BookAppService : IBookAppService
            {
                public System.Threading.Tasks.Task<string> CreateAsync(string input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult(input);

                public System.Threading.Tasks.Task<string> GetAsync(string name, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult(name);
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        // Create should be projected (attribute on interface method), Get should not
        var capabilities = result.GetSourceByFileName("GeneratedAppServiceCompatibilityCapabilities_Book.g.cs");
        capabilities.Should().NotBeNull();
        capabilities!.SourceText.Should().Contain("compat.appservice.book.create",
            "attribute on interface method should be discovered");
        capabilities.SourceText.Should().NotContain("compat.appservice.book.get");

        result.CompilationSuccess.Should().BeTrue();
    }

    [Fact]
    public void MethodLevelProjection_OnImplementationMethod_ProjectsAction()
    {
        // P0-2 fix: [CapabilityCompatibilityProjection] on implementation method should be discovered
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            public interface IBookAppService
            {
                System.Threading.Tasks.Task<string> CreateAsync(string input, System.Threading.CancellationToken ct);

                System.Threading.Tasks.Task<string> GetAsync(string name, System.Threading.CancellationToken ct);
            }

            [CrestService]
            public class BookAppService : IBookAppService
            {
                [CapabilityCompatibilityProjection]
                public System.Threading.Tasks.Task<string> CreateAsync(string input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult(input);

                public System.Threading.Tasks.Task<string> GetAsync(string name, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult(name);
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        // Create should be projected (attribute on implementation method), Get should not
        var capabilities = result.GetSourceByFileName("GeneratedAppServiceCompatibilityCapabilities_Book.g.cs");
        capabilities.Should().NotBeNull();
        capabilities!.SourceText.Should().Contain("compat.appservice.book.create",
            "attribute on implementation method should be discovered");
        capabilities.SourceText.Should().NotContain("compat.appservice.book.get");

        result.CompilationSuccess.Should().BeTrue();
    }

    [Fact]
    public void NullableBodyType_GeneratesValidFactory()
    {
        // Nullable reference type body parameter should strip the ? suffix
        // in the emptyBodyFactory — "new T?()" is illegal C#.
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            [CapabilityCompatibilityProjection]
            public class BookAppService
            {
                public System.Threading.Tasks.Task<string> CreateAsync(BookDto? input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult("ok");
            }

            public class BookDto
            {
                public string Title { get; set; } = "";
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        result.Diagnostics.Should().NotContain(d => d.Id == "CEP037",
            "nullable reference type body should not trigger CEP037");
        result.CompilationSuccess.Should().BeTrue(
            "nullable reference type body should produce compilable generated code");
        var bindings = result.GetSourceByFileName("GeneratedAppServiceCompatibilityBindings_Book.g.cs");
        bindings.Should().NotBeNull();
        bindings!.SourceText.Should().Contain("new global::MyApp.BookDto()",
            "nullable ? suffix should be stripped in factory expression");
        bindings.SourceText.Should().NotContain("new global::MyApp.BookDto?()",
            "new T?() is illegal C# and must not appear in generated code");
    }

    [Fact]
    public void NullableClosedGenericBodyType_GeneratesValidFactory()
    {
        // Nullable closed generic body parameter — e.g., List<BookDto>?
        var source = """
            using CrestCreates.Domain.Shared.Attributes;
            using System.Collections.Generic;

            namespace MyApp;

            [CrestService]
            [CapabilityCompatibilityProjection]
            public class BookAppService
            {
                public System.Threading.Tasks.Task<string> CreateAsync(List<BookDto>? input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult("ok");
            }

            public class BookDto
            {
                public string Title { get; set; } = "";
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        result.Diagnostics.Should().NotContain(d => d.Id == "CEP037",
            "nullable closed generic body should not trigger CEP037");
        result.CompilationSuccess.Should().BeTrue(
            "nullable closed generic body should produce compilable generated code");
        var bindings = result.GetSourceByFileName("GeneratedAppServiceCompatibilityBindings_Book.g.cs");
        bindings.Should().NotBeNull();
        bindings!.SourceText.Should().Contain("new global::System.Collections.Generic.List<global::MyApp.BookDto>()",
            "nullable ? suffix should be stripped in factory expression");
    }

    [Fact]
    public void NullableArrayBodyType_GeneratesValidFactory()
    {
        // Nullable array body parameter — e.g., BookDto[]?
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            [CapabilityCompatibilityProjection]
            public class BookAppService
            {
                public System.Threading.Tasks.Task<string> AddAsync(BookDto[]? input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult("ok");
            }

            public class BookDto
            {
                public string Title { get; set; } = "";
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            source, additionalSources: BuildCompatibilityStubs());

        result.Diagnostics.Should().NotContain(d => d.Id == "CEP037",
            "nullable array body should not trigger CEP037");
        result.CompilationSuccess.Should().BeTrue(
            "nullable array body should produce compilable generated code");
        var bindings = result.GetSourceByFileName("GeneratedAppServiceCompatibilityBindings_Book.g.cs");
        bindings.Should().NotBeNull();
        bindings!.SourceText.Should().Contain("System.Array.Empty<global::MyApp.BookDto>()",
            "nullable array factory should use Array.Empty<T> with stripped ? suffix");
    }
}
