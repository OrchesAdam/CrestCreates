using System;
using System.Collections.Generic;
using System.Linq;
using CrestCreates.CodeGenerator.CrudServiceGenerator;
using CrestCreates.Domain.Shared.Attributes;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.CrudServiceGenerator;

/// <summary>
/// #128 S1 baselines for the formal entry point: a real, referenced
/// <c>[GenerateCrudService(typeof(Entity))]</c> partial service declaration (never the removed
/// test-helper stub). The harness builds its own compilation closure and runs one real generator
/// driver — the helper's stub-injection and serial-driver path is deliberately not used. The two
/// red baselines assert the designed contract that the current generator does not satisfy yet.
/// </summary>
public class CrudServiceRealDeclarationBaselineTests
{
    private const string DeclarationSource = """
        using System;
        using CrestCreates.Domain.Shared.Attributes;

        namespace Shop.Domain.Entities
        {
            public class Product
            {
                public Guid Id { get; set; }
                public string Name { get; set; } = string.Empty;
                public decimal Price { get; set; }
            }
        }

        namespace Shop.Application.Products
        {
            using CrestCreates.Domain.Shared.Attributes;

            [GenerateCrudService(typeof(Shop.Domain.Entities.Product), RoutePrefix = "/api/products")]
            public partial class ProductAppService
            {
            }
        }
        """;

    private static readonly string[] DeclaredReferenceNames =
    [
        "System.Runtime",
        "netstandard",
        "System.Collections",
        "System.Collections.Concurrent",
        "System.Linq",
        "System.Linq.Expressions",
        "System.Threading.Tasks",
        "System.ComponentModel",
        "System.ComponentModel.Annotations",
        "CrestCreates.Domain",
        "CrestCreates.Authorization.Abstractions",
        "CrestCreates.Application.Contracts",
        "CrestCreates.Core.Abstractions",
        "CrestCreates.DynamicApi",
        "CrestCreates.Validation",
        "Microsoft.AspNetCore.Http",
        "Microsoft.AspNetCore.Routing",
        "Microsoft.AspNetCore.Mvc.Core",
        "Rougamo",
    ];

    private static readonly SyntaxTree[] AuthoredTrees = [CSharpSyntaxTree.ParseText(DeclarationSource)];

    private static (Compilation Compilation, GeneratorDriverRunResult RunResult) RunRealDriver()
    {
        var referencePaths = new List<string> { typeof(object).Assembly.Location };
        referencePaths.Add(typeof(GenerateCrudServiceAttribute).Assembly.Location);
        foreach (var referenceName in DeclaredReferenceNames)
        {
            try
            {
                referencePaths.Add(System.Reflection.Assembly.Load(referenceName).Location);
            }
            catch (Exception)
            {
                // Unavailable optional reference: the probe documents support only for the closure
                // that is actually present.
            }
        }

        // The ASP.NET shared-framework assemblies are not copied into the test output; resolve
        // them deterministically from the runtime directory so the closure really contains the
        // types the dynamic-API support check requires.
        var aspNetCoreDirectory = System.IO.Path.GetDirectoryName(
            typeof(Microsoft.AspNetCore.Http.HttpContext).Assembly.Location);
        if (!string.IsNullOrEmpty(aspNetCoreDirectory))
        {
            foreach (var fileName in new[]
                     {
                         "Microsoft.AspNetCore.Http.dll",
                         "Microsoft.AspNetCore.Http.Abstractions.dll",
                         "Microsoft.AspNetCore.Routing.dll",
                         "Microsoft.AspNetCore.Routing.Abstractions.dll",
                         "Microsoft.AspNetCore.Mvc.Core.dll",
                     })
            {
                var candidate = System.IO.Path.Combine(aspNetCoreDirectory, fileName);
                if (System.IO.File.Exists(candidate))
                {
                    referencePaths.Add(candidate);
                }
            }
        }

        var references = referencePaths
            .Distinct()
            .Select(path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "RealCrudDeclarationBaseline",
            AuthoredTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new CrudServiceSourceGenerator());
        // R3-CRUD128-02: Capture the updated compilation so symbol checks can see generated types
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
        return (updatedCompilation, driver.GetRunResult());
    }

    [Fact]
    public void Real_referenced_attribute_declaration_compiles_from_a_real_assembly_reference()
    {
        // The formal entry must be usable from a real Domain.Shared reference (the helper stub is
        // not the public API). Only errors located in the authored trees count: generated-tree
        // errors are the object of the other baselines.
        var (updated, _) = RunRealDriver();

        var authoredErrors = updated.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                                 && diagnostic.Location.SourceTree is { } tree
                                 && AuthoredTrees.Contains(tree))
            .Select(diagnostic => diagnostic.ToString())
            .ToList();
        authoredErrors.Should().BeEmpty(
            "the typeof(Entity) partial-service declaration must compile against real references: "
            + string.Join(" | ", authoredErrors));
    }

    [Fact]
    public void Generated_dtos_must_be_named_after_the_entity_not_the_service()
    {
        // Designed behavior: the annotated class is the service; ProductDto (derived from
        // typeof(Product)) is the generated contract. The current generator treats the annotated
        // class itself as the entity.
        var (_, runResult) = RunRealDriver();
        var generated = string.Join("\n", runResult.GeneratedTrees.Select(tree => tree.ToString()));

        generated.Should().NotBeEmpty("the formal declaration must generate the CRUD bundle");
        generated.Should().Contain("class ProductDto",
            "DTOs are derived from typeof(Product), never from the annotated service class");
        generated.Should().NotContain("class ProductAppServiceDto",
            "the annotated service class must not be treated as the entity");
    }

    [Fact]
    public void Formal_crud_request_binding_must_not_route_through_the_legacy_body_helper()
    {
        // Designed behavior: the formal chain binds bodies through the typed
        // CapabilityEndpointBodyReader contract with required-body semantics (empty/whitespace/null
        // /bad JSON → 400, zero business). The current emission routes through the legacy
        // DynamicApiGeneratedRuntime helper whose empty-body semantics materialize a default DTO.
        var (closure, runResult) = RunRealDriver();
        string[] requiredTypes =
        [
            "CrestCreates.DynamicApi.IDynamicApiGeneratedProvider",
            "CrestCreates.Validation.Modules.IValidationService",
            "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder",
            "Microsoft.AspNetCore.Http.HttpContext",
            "Microsoft.AspNetCore.Mvc.FromServicesAttribute",
        ];
        var missing = requiredTypes.Where(type => closure.GetTypeByMetadataName(type) is null).ToArray();
        missing.Should().BeEmpty("the harness closure must satisfy HasDynamicApiSupport so this baseline cannot pass vacuously; missing: "
            + string.Join(", ", missing));

        var generated = string.Join("\n", runResult.GeneratedTrees.Select(tree => tree.ToString()));

        generated.Should().NotContain("DynamicApiGeneratedRuntime.ReadBodyAsync",
            "formal CRUD must never bind request bodies through the legacy helper");
    }

    [Fact]
    public void Entity_with_private_setter_on_input_property_must_emit_CCCRUD004()
    {
        // Designed behavior: entities with input properties (not Id, not excluded) that lack public
        // setters cannot be statically mapped by the generated mapper. The generator must emit
        // CCCRUD004 and skip generation for that entity rather than producing an incomplete mapping.
        var privateSetterSource = """
            using System;
            using CrestCreates.Domain.Shared.Attributes;

            namespace Shop.Domain.Entities
            {
                public class Product
                {
                    public Guid Id { get; protected set; }
                    public string Name { get; private set; } = string.Empty;
                    public decimal Price { get; set; }
                }
            }

            namespace Shop.Application.Products
            {
                using CrestCreates.Domain.Shared.Attributes;

                [GenerateCrudService(typeof(Shop.Domain.Entities.Product), RoutePrefix = "/api/products")]
                public partial class ProductAppService
                {
                }
            }
            """;

        var (_, runResult) = RunRealDriverWithSource(privateSetterSource);

        var diagnostics = runResult.Diagnostics
            .Where(d => d.Id == CrudServiceDiagnosticCodes.StaticClosureUnsupportedValue)
            .ToList();

        diagnostics.Should().NotBeEmpty("an entity with private setters on input properties must trigger CCCRUD004");
        diagnostics.Should().ContainSingle("one diagnostic per entity");
        diagnostics[0].GetMessage().Should().Contain("Name",
            "the diagnostic must identify the unmappable input property");
    }

    [Fact]
    public void Entity_with_protected_id_setter_must_not_emit_CCCRUD004()
    {
        // Designed behavior: Id is output-only (not written from Create/Update), so a protected
        // setter on Id is acceptable and must not trigger CCCRUD004.
        var protectedIdSource = """
            using System;
            using CrestCreates.Domain.Shared.Attributes;

            namespace Shop.Domain.Entities
            {
                public class Product
                {
                    public Guid Id { get; protected set; }
                    public string Name { get; set; } = string.Empty;
                    public decimal Price { get; set; }
                }
            }

            namespace Shop.Application.Products
            {
                using CrestCreates.Domain.Shared.Attributes;

                [GenerateCrudService(typeof(Shop.Domain.Entities.Product), RoutePrefix = "/api/products")]
                public partial class ProductAppService
                {
                }
            }
            """;

        var (_, runResult) = RunRealDriverWithSource(protectedIdSource);

        var diagnostics = runResult.Diagnostics
            .Where(d => d.Id == CrudServiceDiagnosticCodes.StaticClosureUnsupportedValue)
            .ToList();

        diagnostics.Should().BeEmpty("Id with protected setter is output-only and must not trigger CCCRUD004");
    }

    [Fact]
    public void Entity_with_excluded_private_setter_must_not_emit_CCCRUD004()
    {
        // Designed behavior: properties listed in ExcludeProperties do not participate in input
        // mapping, so private setters on them must not trigger CCCRUD004.
        var excludedSource = """
            using System;
            using CrestCreates.Domain.Shared.Attributes;

            namespace Shop.Domain.Entities
            {
                public class Product
                {
                    public Guid Id { get; set; }
                    public string Name { get; set; } = string.Empty;
                    public string Secret { get; private set; } = string.Empty;
                }
            }

            namespace Shop.Application.Products
            {
                using CrestCreates.Domain.Shared.Attributes;

                [GenerateCrudService(typeof(Shop.Domain.Entities.Product), RoutePrefix = "/api/products", ExcludeProperties = new[] { "Secret" })]
                public partial class ProductAppService
                {
                }
            }
            """;

        var (_, runResult) = RunRealDriverWithSource(excludedSource);

        var diagnostics = runResult.Diagnostics
            .Where(d => d.Id == CrudServiceDiagnosticCodes.StaticClosureUnsupportedValue)
            .ToList();

        diagnostics.Should().BeEmpty("excluded properties must not trigger CCCRUD004");
    }

    [Fact]
    public void Entity_with_private_setter_on_platform_fields_must_not_emit_CCCRUD004()
    {
        // R2-CRUD128-02: Platform-managed fields (TenantId, CreationTime, IsDeleted, etc.) do not
        // participate in Create/Update input mapping, so private setters on them must not trigger CCCRUD004.
        var platformFieldsSource = """
            using System;
            using CrestCreates.Domain.Shared.Attributes;

            namespace Shop.Domain.Entities
            {
                public class Product
                {
                    public Guid Id { get; set; }
                    public string Name { get; set; } = string.Empty;
                    public Guid TenantId { get; private set; }
                    public DateTime CreationTime { get; private set; }
                    public bool IsDeleted { get; private set; }
                }
            }

            namespace Shop.Application.Products
            {
                using CrestCreates.Domain.Shared.Attributes;

                [GenerateCrudService(typeof(Shop.Domain.Entities.Product), RoutePrefix = "/api/products")]
                public partial class ProductAppService
                {
                }
            }
            """;

        var (_, runResult) = RunRealDriverWithSource(platformFieldsSource);

        var diagnostics = runResult.Diagnostics
            .Where(d => d.Id == CrudServiceDiagnosticCodes.StaticClosureUnsupportedValue)
            .ToList();

        diagnostics.Should().BeEmpty("platform-managed fields must not trigger CCCRUD004");
    }

    [Fact]
    public void Entity_without_parameterless_constructor_must_emit_CCCRUD004()
    {
        // Designed behavior: entities without a public parameterless constructor cannot be
        // constructed by the generated mapper. The generator must emit CCCRUD004.
        var noCtorSource = """
            using System;
            using CrestCreates.Domain.Shared.Attributes;

            namespace Shop.Domain.Entities
            {
                public class Product
                {
                    public Product(string name) { Name = name; }
                    public Guid Id { get; set; }
                    public string Name { get; set; }
                    public decimal Price { get; set; }
                }
            }

            namespace Shop.Application.Products
            {
                using CrestCreates.Domain.Shared.Attributes;

                [GenerateCrudService(typeof(Shop.Domain.Entities.Product), RoutePrefix = "/api/products")]
                public partial class ProductAppService
                {
                }
            }
            """;

        var (_, runResult) = RunRealDriverWithSource(noCtorSource);

        var diagnostics = runResult.Diagnostics
            .Where(d => d.Id == CrudServiceDiagnosticCodes.StaticClosureUnsupportedValue)
            .ToList();

        diagnostics.Should().NotBeEmpty("an entity without a parameterless constructor must trigger CCCRUD004");
        diagnostics.Should().ContainSingle("one diagnostic per entity");
        diagnostics[0].GetMessage().Should().Contain("parameterless constructor",
            "the diagnostic must identify the missing constructor");
    }

    [Fact]
    public void Entity_with_protected_only_parameterless_constructor_must_emit_CCCRUD004()
    {
        // Designed behavior (R1-CRUD128-04): the mapper is a static type that does `new TTarget { ... }`,
        // which cannot call protected constructors. Only public parameterless constructors are accepted.
        var protectedCtorSource = """
            using System;
            using CrestCreates.Domain.Shared.Attributes;

            namespace Shop.Domain.Entities
            {
                public class Product
                {
                    protected Product() { }
                    public Guid Id { get; set; }
                    public string Name { get; set; } = string.Empty;
                    public decimal Price { get; set; }
                }
            }

            namespace Shop.Application.Products
            {
                using CrestCreates.Domain.Shared.Attributes;

                [GenerateCrudService(typeof(Shop.Domain.Entities.Product), RoutePrefix = "/api/products")]
                public partial class ProductAppService
                {
                }
            }
            """;

        var (_, runResult) = RunRealDriverWithSource(protectedCtorSource);

        var diagnostics = runResult.Diagnostics
            .Where(d => d.Id == CrudServiceDiagnosticCodes.StaticClosureUnsupportedValue)
            .ToList();

        diagnostics.Should().NotBeEmpty("an entity with only a protected parameterless constructor must trigger CCCRUD004");
        diagnostics.Should().ContainSingle("one diagnostic per entity");
        diagnostics[0].GetMessage().Should().Contain("public parameterless constructor",
            "the diagnostic must specify that a public constructor is required");
    }

    private static (Compilation Compilation, GeneratorDriverRunResult RunResult) RunRealDriverWithSource(string source)
    {
        var referencePaths = new List<string> { typeof(object).Assembly.Location };
        referencePaths.Add(typeof(GenerateCrudServiceAttribute).Assembly.Location);
        foreach (var referenceName in DeclaredReferenceNames)
        {
            try
            {
                referencePaths.Add(System.Reflection.Assembly.Load(referenceName).Location);
            }
            catch (Exception)
            {
                // Unavailable optional reference
            }
        }

        var aspNetCoreDirectory = System.IO.Path.GetDirectoryName(
            typeof(Microsoft.AspNetCore.Http.HttpContext).Assembly.Location);
        if (!string.IsNullOrEmpty(aspNetCoreDirectory))
        {
            foreach (var fileName in new[]
                     {
                         "Microsoft.AspNetCore.Http.dll",
                         "Microsoft.AspNetCore.Http.Abstractions.dll",
                         "Microsoft.AspNetCore.Routing.dll",
                         "Microsoft.AspNetCore.Routing.Abstractions.dll",
                         "Microsoft.AspNetCore.Mvc.Core.dll",
                     })
            {
                var candidate = System.IO.Path.Combine(aspNetCoreDirectory, fileName);
                if (System.IO.File.Exists(candidate))
                {
                    referencePaths.Add(candidate);
                }
            }
        }

        var references = referencePaths
            .Distinct()
            .Select(path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToArray();

        var trees = new[] { CSharpSyntaxTree.ParseText(source) };
        var compilation = CSharpCompilation.Create(
            "RealCrudDeclarationBaseline",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new CrudServiceSourceGenerator());
        // R3-CRUD128-02: Capture the updated compilation so symbol checks can see generated types
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
        return (updatedCompilation, driver.GetRunResult());
    }

    [Fact]
    public void Formal_crud_must_emit_typed_input_envelopes_for_update_and_delete()
    {
        // Designed behavior (§7.1): Update and Delete actions must emit typed input envelopes
        // that combine route/body/header parameters. The CapabilityEndpointMapper performs
        // SerializeToElement on the actual input, so the envelope type must be a JSON root.
        // The current generator does not emit these typed envelopes.
        // R2-CRUD128-03: Use Roslyn symbols to verify actual type declarations, not just regex.
        var (compilation, runResult) = RunRealDriver();
        runResult.GeneratedTrees.Should().NotBeEmpty("the formal declaration must generate the CRUD bundle");

        // Parse the generated code and verify actual type declarations exist
        var generatedTypes = compilation.GetSymbolsWithName("UpdateProductInput", SymbolFilter.Type).ToList();
        generatedTypes.Should().NotBeEmpty("UpdateProductInput must be declared as an actual type, not just mentioned in comments");
        generatedTypes.Should().OnlyContain(t => t is INamedTypeSymbol, "envelope must be a class/record/struct");

        generatedTypes = compilation.GetSymbolsWithName("DeleteProductInput", SymbolFilter.Type).ToList();
        generatedTypes.Should().NotBeEmpty("DeleteProductInput must be declared as an actual type, not just mentioned in comments");
        generatedTypes.Should().OnlyContain(t => t is INamedTypeSymbol, "envelope must be a class/record/struct");
    }

    [Fact]
    public void Formal_crud_must_emit_typed_response_envelopes_as_json_roots()
    {
        // Designed behavior (§7.1-7.4): All response envelopes must be emitted as JSON roots:
        // - Get: DynamicApiResponse<ProductDto> (typed DTO, not void)
        // - Search: DynamicApiResponse<PagedResultDto<ProductDto>>
        // - Create/Update: DynamicApiResponse<ProductDto> (typed DTO, not void — R1-CRUD128-05)
        // - Delete: DynamicApiResponse (void)
        // - Get not-found: DynamicApiResponse (void/404)
        // The current generator wraps through object-typed ctx.Output instead.
        // R3-CRUD128-03: Use symbol-based assertions, not regex on source text.
        var (compilation, runResult) = RunRealDriver();
        runResult.GeneratedTrees.Should().NotBeEmpty("the formal declaration must generate the CRUD bundle");

        // R3-CRUD128-03: Verify JsonTypeInfo fields exist with correct type parameters using symbols.
        // Get all fields whose type is JsonTypeInfo<T> and check their type arguments.
        var allTypes = compilation.GetSymbolsWithName(_ => true, SymbolFilter.Type)
            .OfType<INamedTypeSymbol>()
            .ToList();

        // Look for fields in generated types that are JsonTypeInfo<DynamicApiResponse<...>>
        var jsonTypeInfoFields = allTypes
            .SelectMany(t => t.GetMembers().OfType<IFieldSymbol>())
            .Where(f => f.Type is INamedTypeSymbol namedType &&
                        namedType.Name == "JsonTypeInfo" &&
                        namedType.TypeArguments.Length == 1)
            .Select(f => (Field: f, TypeArg: ((INamedTypeSymbol)f.Type).TypeArguments[0]))
            .ToList();

        // Check for typed response envelope: JsonTypeInfo<DynamicApiResponse<ProductDto>>
        var getResponseEnvelope = jsonTypeInfoFields
            .Where(f => f.TypeArg is INamedTypeSymbol namedType &&
                        namedType.Name == "DynamicApiResponse" &&
                        namedType.TypeArguments.Length == 1 &&
                        namedType.TypeArguments[0].Name == "ProductDto")
            .ToList();
        getResponseEnvelope.Should().NotBeEmpty(
            "Get/Create/Update response envelope must have JsonTypeInfo<DynamicApiResponse<ProductDto>> field");

        // Check for paged response envelope: JsonTypeInfo<DynamicApiResponse<PagedResultDto<ProductDto>>>
        var searchResponseEnvelope = jsonTypeInfoFields
            .Where(f => f.TypeArg is INamedTypeSymbol namedType &&
                        namedType.Name == "DynamicApiResponse" &&
                        namedType.TypeArguments.Length == 1 &&
                        namedType.TypeArguments[0] is INamedTypeSymbol pagedType &&
                        pagedType.Name == "PagedResultDto" &&
                        pagedType.TypeArguments.Length == 1 &&
                        pagedType.TypeArguments[0].Name == "ProductDto")
            .ToList();
        searchResponseEnvelope.Should().NotBeEmpty(
            "Search response envelope must have JsonTypeInfo<DynamicApiResponse<PagedResultDto<ProductDto>>> field");

        // Check for void response envelope: JsonTypeInfo<DynamicApiResponse>
        var voidResponseEnvelope = jsonTypeInfoFields
            .Where(f => f.TypeArg is INamedTypeSymbol namedType &&
                        namedType.Name == "DynamicApiResponse" &&
                        namedType.TypeArguments.Length == 0)
            .ToList();
        voidResponseEnvelope.Should().NotBeEmpty(
            "Delete/404 void response envelope must have JsonTypeInfo<DynamicApiResponse> field");
    }
}
