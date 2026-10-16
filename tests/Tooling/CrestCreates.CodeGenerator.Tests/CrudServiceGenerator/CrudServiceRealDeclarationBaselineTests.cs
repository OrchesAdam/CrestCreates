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
/// driver — the helper's stub-injection and serial-driver path is deliberately not used. The
/// contract baselines assert the designed wire model through <see cref="CrudFormalContractProbe"/>:
/// referenced-assembly symbol identity, producer ownership and required members, so neither comments,
/// same-short-name fakes nor uninitialized members can satisfy them. Each contract also has a
/// reverse control that runs the same checker over a mutation and must report the targeted violation.
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

    // §7.4: Delete composes the route identity with the If-Match concurrency header, so the
    // capability-input baselines use a concurrency entity; the plain probe above stays the input of
    // the naming and request-binding baselines.
    private const string ConcurrencyDeclarationSource = """
        using System;
        using CrestCreates.Domain.Shared.Attributes;
        using CrestCreates.Domain.Shared.Entities.Auditing;

        namespace Shop.Domain.Entities
        {
            public class Product : IHasConcurrencyStamp
            {
                public Guid Id { get; set; }
                public string Name { get; set; } = string.Empty;
                public decimal Price { get; set; }
                public string ConcurrencyStamp { get; set; } = string.Empty;
            }
        }

        namespace Shop.Application.Products
        {
            [GenerateCrudService(typeof(Shop.Domain.Entities.Product), RoutePrefix = "/api/products")]
            public partial class ProductAppService
            {
            }
        }
        """;

    private static readonly CrudFormalContractProbe.Model ContractModel =
        new("ProductAppService", "Product", "Shop.Application.Products");

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
        "System.Text.Json",
        "CrestCreates.Domain",
        "CrestCreates.Authorization.Abstractions",
        "CrestCreates.Application.Contracts",
        "CrestCreates.Core.Abstractions",
        "CrestCreates.DynamicApi",
        "CrestCreates.DynamicApi.Abstractions",
        "CrestCreates.Capability.Abstractions",
        "CrestCreates.Metadata.Abstractions",
        "CrestCreates.Validation",
        "Microsoft.AspNetCore.Http",
        "Microsoft.AspNetCore.Routing",
        "Microsoft.AspNetCore.Mvc.Core",
        "Rougamo",
    ];

    private static IReadOnlyList<MetadataReference> ClosureReferences()
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

        // The ASP.NET shared-framework assemblies are not copied into the test output; the closure
        // closes over the whole framework reference (a real consumer declares
        // <FrameworkReference Include="Microsoft.AspNetCore.App" />), because the required types are
        // split across physical assemblies (for example HttpContext lives in
        // Microsoft.AspNetCore.Http.Abstractions in .NET 10).
        referencePaths.AddRange(CrudConsumerFrameworkClosure.AspNetCoreSharedFrameworkAssemblies());

        return referencePaths
            .Distinct()
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private static (
        Compilation Compilation,
        GeneratorDriverRunResult RunResult,
        IReadOnlyCollection<SyntaxTree> ProducerTrees) RunRealDriver(
            string source = DeclarationSource)
    {
        var compilation = CSharpCompilation.Create(
            "RealCrudDeclarationBaseline",
            [CSharpSyntaxTree.ParseText(source)],
            ClosureReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new CrudServiceSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
        var runResult = driver.GetRunResult();
        return (updatedCompilation, runResult, runResult.GeneratedTrees.ToHashSet());
    }

    /// <summary>
    /// Trees the harness authored (the only non-producer inputs), so error attribution never depends
    /// on parsing the same text twice and getting a different tree instance.
    /// </summary>
    private static IReadOnlyCollection<SyntaxTree> AuthoredTreesOf(
        Compilation compilation,
        IReadOnlyCollection<SyntaxTree> producerTrees) =>
        compilation.SyntaxTrees.Except(producerTrees).ToArray();

    [Fact]
    public void Real_referenced_attribute_declaration_compiles_from_a_real_assembly_reference()
    {
        // The formal entry must be usable from a real Domain.Shared reference (the helper stub is
        // not the public API). Only errors located in the authored trees count: generated-tree
        // errors are the object of the other baselines.
        var (updated, _, producerTrees) = RunRealDriver();
        var authoredTrees = AuthoredTreesOf(updated, producerTrees);
        authoredTrees.Should().HaveCount(1, "the probe authors exactly one declaration tree");

        var authoredErrors = updated.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                                 && diagnostic.Location.SourceTree is { } tree
                                 && authoredTrees.Contains(tree))
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
        // R4/S2-5 migration: the formal surface is materialized by the CRUD precompile producer as
        // ordinary Compile input, so naming is asserted on that output — not on one generator round.
        var producer = CrudPrecompileProducer.Run(DeclarationSource, ClosureReferences());

        producer.Diagnostics.Should().BeEmpty(
            "the formal declaration must build a model without CCCRUD diagnostics: "
            + string.Join(" | ", producer.Diagnostics.Select(issue => issue.ToDisplayString())));
        // Bundle compilation is proven at real-MSBuild level, where the sibling generators run in the
        // same CoreCompile and can supply the mapper members the surface calls. Asserting it here
        // would only measure which references this harness happens to carry.

        var materialized = string.Join("\n", producer.BundleTrees.Select(tree => tree.ToString()));
        materialized.Should().Contain("class ProductDto",
            "DTOs are derived from typeof(Product), never from the annotated service class");
        materialized.Should().Contain("namespace Shop.Application.Products.Contracts",
            "the default DTO namespace is the declaring service namespace plus Contracts (§5.1)");
        materialized.Should().NotContain("class ProductAppServiceDto",
            "the annotated service class must not be treated as the entity");
    }

    [Fact]
    public void Formal_crud_request_binding_must_not_route_through_the_legacy_body_helper()
    {
        // Designed behavior: the formal chain binds bodies through the typed
        // CapabilityEndpointBodyReader contract with required-body semantics (empty/whitespace/null
        // /bad JSON → 400, zero business). The current emission routes through the legacy
        // DynamicApiGeneratedRuntime helper whose empty-body semantics materialize a default DTO.
        var producer = CrudPrecompileProducer.Run(DeclarationSource, ClosureReferences());
        producer.BundleTrees.Should().NotBeEmpty("the formal declaration must materialize the CRUD surface");
        var closure = producer.Compilation;
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

        var materialized = string.Join("\n", producer.BundleTrees.Select(tree => tree.ToString()));

        materialized.Should().NotContain("DynamicApiGeneratedRuntime.ReadBodyAsync",
            "formal CRUD must never bind request bodies through the legacy helper");
        materialized.Should().NotContain("CompatibilityBodyReader",
            "formal CRUD must never bind request bodies through the legacy compatibility reader");
    }

    [Fact]
    public void Formal_crud_must_emit_typed_input_envelopes_for_update_and_delete()
    {
        // Designed contract (§7.1/§7.4): each route/body/header composition is a producer-declared
        // compatibility input envelope owned by service+action, with the route identity and the body
        // member at their static types, registered as a JSON root for the mapper's SerializeToElement.
        var output = CrudPrecompileProducer.Run(ConcurrencyDeclarationSource, ClosureReferences());
        output.BundleTrees.Should().NotBeEmpty("the formal declaration must materialize the CRUD surface");
        output.Diagnostics.Should().BeEmpty(
            "the concurrency CRUD declaration must build a clean model: "
            + string.Join(" | ", output.Diagnostics.Select(issue => issue.ToDisplayString())));
        var compilation = output.Compilation;
        IReadOnlyCollection<SyntaxTree> producerTrees = output.BundleTrees;

        CrudFormalContractProbe.ClosureViolations(compilation).Should().BeEmpty(
            "the harness closure must expose the referenced JSON and envelope contract types");

        CrudFormalContractProbe
            .InputEnvelopeViolations(compilation, producerTrees, ContractModel)
            .Should().BeEmpty(
                "formal CRUD must emit the Update/Delete capability input envelopes and register them as roots");
    }

    [Fact]
    public void Formal_crud_must_emit_typed_response_envelopes_as_json_roots()
    {
        // Designed contract (§7.1/§7.3): the generated bundle declares an official
        // JsonSerializerContext whose roots are the closed formal result envelopes
        // DynamicApiResponse<ProductDto>, DynamicApiResponse<PagedResultDto<ProductDto>>,
        // DynamicApiResponse (void/404) and ProductDto. The current generator wraps through
        // object-typed ctx.Output and registers nothing.
        var output = CrudPrecompileProducer.Run(DeclarationSource, ClosureReferences());
        output.BundleTrees.Should().NotBeEmpty("the formal declaration must materialize the CRUD surface");
        var compilation = output.Compilation;
        IReadOnlyCollection<SyntaxTree> producerTrees = output.BundleTrees;

        CrudFormalContractProbe.ClosureViolations(compilation).Should().BeEmpty(
            "the harness closure must expose the referenced JSON and envelope contract types");

        CrudFormalContractProbe
            .ResponseRootViolations(compilation, producerTrees, ContractModel)
            .Should().BeEmpty(
                "formal CRUD must register the closed typed response envelopes as roots on the official context");
    }

    [Theory]
    [InlineData("empty-shells")]
    [InlineData("wrong-owner")]
    [InlineData("missing-member")]
    [InlineData("wrong-member-type")]
    [InlineData("not-producer-owned")]
    public void Input_envelope_contract_must_reject_structural_mutation(string mutation)
    {
        // R4-CRUD128-02 reverse control: the same checker that drives the contract baseline must
        // report the targeted violation for each structural mutation.
        var (source, expectedReason) = InputEnvelopeMutation(mutation);
        var compilation = CrudFormalContractProbe.AuthoredCompilation(source, ClosureReferences());
        var producerTrees = mutation == "not-producer-owned"
            ? Array.Empty<SyntaxTree>()
            : compilation.SyntaxTrees.ToArray();

        var violations = CrudFormalContractProbe.InputEnvelopeViolations(
            compilation, producerTrees, ContractModel);

        violations.Should().Contain(
            reason => reason.Contains(expectedReason, StringComparison.Ordinal),
            $"mutation '{mutation}' must fail on '{expectedReason}'; observed: {string.Join(" | ", violations)}");
    }

    [Theory]
    [InlineData("fake-contracts")]
    [InlineData("fake-base-context")]
    [InlineData("empty-official-context")]
    [InlineData("object-envelope-root")]
    public void Response_root_contract_must_reject_structural_mutation(string mutation)
    {
        // R4-CRUD128-03 reverse control: same-name fakes, an official context without roots and an
        // object-typed envelope must each be rejected by the identity-based root checks.
        var (source, expectedReason) = ResponseRootMutation(mutation);
        var compilation = CrudFormalContractProbe.AuthoredCompilation(source, ClosureReferences());

        var violations = CrudFormalContractProbe.ResponseRootViolations(
            compilation, compilation.SyntaxTrees.ToArray(), ContractModel);

        violations.Should().Contain(
            reason => reason.Contains(expectedReason, StringComparison.Ordinal),
            $"mutation '{mutation}' must fail on '{expectedReason}'; observed: {string.Join(" | ", violations)}");
    }

    [Fact]
    public void Complete_declared_model_must_satisfy_both_response_and_input_contracts()
    {
        // R5-CRUD128-01 positive control: the checkers must accept a model that registers every
        // required root, so the contract reds cannot be read as "no declaration can ever pass".
        var compilation = ProbeCompilationOf(ResponseRootModelSource());

        CrudFormalContractProbe.ClosureViolations(compilation).Should().BeEmpty();
        compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .Should().BeEmpty("the control model must be a sound compilation, not a broken one");

        CrudFormalContractProbe
            .ResponseRootViolations(compilation, compilation.SyntaxTrees.ToArray(), ContractModel)
            .Should().BeEmpty();
        CrudFormalContractProbe
            .InputEnvelopeViolations(compilation, compilation.SyntaxTrees.ToArray(), ContractModel)
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData("dto", "global::Shop.Application.Products.Contracts.ProductDto")]
    [InlineData("page", "global::CrestCreates.Application.Contracts.DTOs.Common.PagedResultDto<global::Shop.Application.Products.Contracts.ProductDto>")]
    [InlineData("dto-envelope", "global::CrestCreates.DynamicApi.DynamicApiResponse<global::Shop.Application.Products.Contracts.ProductDto>")]
    [InlineData("page-envelope", "global::CrestCreates.DynamicApi.DynamicApiResponse<global::CrestCreates.Application.Contracts.DTOs.Common.PagedResultDto<global::Shop.Application.Products.Contracts.ProductDto>>")]
    [InlineData("void", "global::CrestCreates.DynamicApi.DynamicApiResponse")]
    [InlineData("create-dto", "global::Shop.Application.Products.Contracts.CreateProductDto")]
    [InlineData("update-dto", "global::Shop.Application.Products.Contracts.UpdateProductDto")]
    [InlineData("list-request", "global::Shop.Application.Products.Contracts.ProductListRequestDto")]
    public void Response_root_contract_must_reject_each_single_missing_root(string omittedRoot, string expectedRootText)
    {
        // R5-CRUD128-01: every member of the required-root matrix is individually necessary. The
        // model is complete except for one [JsonSerializable] registration, so the reported reason
        // must name that closed type — a registered inner payload cannot cover an absent envelope.
        var compilation = ProbeCompilationOf(ResponseRootModelSource(except: omittedRoot));

        var violations = CrudFormalContractProbe.ResponseRootViolations(
            compilation, compilation.SyntaxTrees.ToArray(), ContractModel);

        violations.Should().Contain(
            reason => reason.Contains(expectedRootText, StringComparison.Ordinal)
                      && reason.Contains("must be registered as a [JsonSerializable] root", StringComparison.Ordinal),
            $"omitting '{omittedRoot}' must be reported on '{expectedRootText}'; observed: "
            + string.Join(" | ", violations));
    }

    [Theory]
    [InlineData("update-input")]
    [InlineData("delete-input")]
    public void Input_envelope_contract_must_reject_a_missing_capability_input_root(string omittedRoot)
    {
        // §7.1: the composed capability input is serialized by the mapper, so declaring the envelope
        // is not enough — its root registration is separately required.
        var compilation = ProbeCompilationOf(ResponseRootModelSource(except: omittedRoot));

        var violations = CrudFormalContractProbe.InputEnvelopeViolations(
            compilation, compilation.SyntaxTrees.ToArray(), ContractModel);

        violations.Should().Contain(
            reason => reason.Contains("ProductAppService", StringComparison.Ordinal)
                      && reason.Contains("must be registered as a [JsonSerializable] root", StringComparison.Ordinal),
            $"omitting the '{omittedRoot}' root must be reported by the input contract; observed: "
            + string.Join(" | ", violations));
    }

    private static Compilation ProbeCompilationOf(string source) =>
        CrudFormalContractProbe.AuthoredCompilation(source, ClosureReferences());

    /// <summary>
    /// Complete declarative model for the §7.1 root matrix: entity/Create/Update/List DTOs, the
    /// bounded page payload, the typed single-DTO and typed page envelopes, the void/404 envelope and
    /// both composed capability input envelopes, all registered on an official context derived type.
    /// Only the registration named by <paramref name="except"/> is left out. This exercises the
    /// checker; it is not metadata-generation or Feature evidence.
    /// </summary>
    private static string ResponseRootModelSource(string? except = null)
    {
        const string Contracts = "global::Shop.Application.Products.Contracts.";
        const string Generated = "global::CrestCreates.Generated.";
        var page = "global::CrestCreates.Application.Contracts.DTOs.Common.PagedResultDto<" + Contracts + "ProductDto>";
        var dtoEnvelope = "global::CrestCreates.DynamicApi.DynamicApiResponse<" + Contracts + "ProductDto>";
        var pageEnvelope = "global::CrestCreates.DynamicApi.DynamicApiResponse<" + page + ">";

        (string Key, string Type)[] roots =
        [
            ("dto", Contracts + "ProductDto"),
            ("create-dto", Contracts + "CreateProductDto"),
            ("update-dto", Contracts + "UpdateProductDto"),
            ("list-request", Contracts + "ProductListRequestDto"),
            ("page", page),
            ("dto-envelope", dtoEnvelope),
            ("page-envelope", pageEnvelope),
            ("void", "global::CrestCreates.DynamicApi.DynamicApiResponse"),
            ("update-input", Generated + "ProductAppService_Update_CompatibilityInput"),
            ("delete-input", Generated + "ProductAppService_Delete_CompatibilityInput"),
        ];

        var attributes = string.Join(
            "\n",
            roots
                .Where(root => root.Key != except)
                .Select(root => "                [global::System.Text.Json.Serialization.JsonSerializable(typeof("
                    + root.Type + "))]"));

        return """
            namespace Shop.Application.Products.Contracts
            {
                public class ProductDto { }
                public class CreateProductDto { }
                public class UpdateProductDto { }
                public class ProductListRequestDto { }
            }

            namespace CrestCreates.Generated
            {
                internal sealed class ProductAppService_Update_CompatibilityInput
                {
                    public global::System.Guid Id { get; init; }
                    public global::Shop.Application.Products.Contracts.UpdateProductDto Input { get; init; } = default!;
                }

                internal sealed class ProductAppService_Delete_CompatibilityInput
                {
                    public global::System.Guid Id { get; init; }
                    public string? IfMatch { get; init; }
                }

            __ROOTS__
                internal abstract partial class FullJsonContext
                    : global::System.Text.Json.Serialization.JsonSerializerContext
                {
                    protected FullJsonContext(global::System.Text.Json.JsonSerializerOptions options) : base(options)
                    {
                    }
                }
            }
            """.Replace("__ROOTS__", attributes);
    }

    private static (string Source, string ExpectedReason) InputEnvelopeMutation(string mutation) => mutation switch
    {
        "empty-shells" => ("""
            namespace ReviewUnrelated
            {
                public class UpdateProductInput { }
                public class DeleteProductInput { }
            }
            """, "must declare the Update capability input envelope"),
        "wrong-owner" => ("""
            namespace Shop.Application.Products.Contracts
            {
                public class UpdateProductDto { }
            }

            namespace ReviewUnrelated
            {
                internal sealed class OtherAppService_Update_CompatibilityInput
                {
                    public global::System.Guid Id { get; init; }
                    public global::Shop.Application.Products.Contracts.UpdateProductDto Input { get; init; } = default!;
                }
            }
            """, "must declare the Update capability input envelope"),
        "missing-member" => ("""
            namespace Shop.Application.Products.Contracts
            {
                public class UpdateProductDto { }
            }

            namespace CrestCreates.Generated
            {
                internal sealed class ProductAppService_Update_CompatibilityInput
                {
                    public global::System.Guid Id { get; init; }
                }

                internal sealed class ProductAppService_Delete_CompatibilityInput
                {
                    public global::System.Guid Id { get; init; }
                    public global::System.String? IfMatch { get; init; }
                }
            }
            """, "must expose the input member 'Input'"),
        "wrong-member-type" => ("""
            namespace Shop.Application.Products.Contracts
            {
                public class UpdateProductDto { }
            }

            namespace CrestCreates.Generated
            {
                internal sealed class ProductAppService_Update_CompatibilityInput
                {
                    public global::System.String Id { get; init; } = global::System.String.Empty;
                    public object Input { get; init; } = default!;
                }

                internal sealed class ProductAppService_Delete_CompatibilityInput
                {
                    public global::System.Guid Id { get; init; }
                    public global::System.String? IfMatch { get; init; }
                }
            }
            """, "must be statically typed 'System.Guid'"),
        "not-producer-owned" => (FormalEnvelopes + RootedContext, "must declare the Update capability input envelope"),
        _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
    };

    private const string FormalEnvelopes = """
        namespace Shop.Application.Products.Contracts
        {
            public class ProductDto { }
            public class UpdateProductDto { }
        }

        namespace CrestCreates.Generated
        {
            internal sealed class ProductAppService_Update_CompatibilityInput
            {
                public global::System.Guid Id { get; init; }
                public global::Shop.Application.Products.Contracts.UpdateProductDto Input { get; init; } = default!;
            }

            internal sealed class ProductAppService_Delete_CompatibilityInput
            {
                public global::System.Guid Id { get; init; }
                public global::System.String? IfMatch { get; init; }
            }
        }
        """;

    private const string RootedContext = """
        namespace CrestCreates.Generated
        {
            [System.Text.Json.Serialization.JsonSerializable(typeof(global::CrestCreates.Generated.ProductAppService_Update_CompatibilityInput))]
            [System.Text.Json.Serialization.JsonSerializable(typeof(global::CrestCreates.Generated.ProductAppService_Delete_CompatibilityInput))]
            internal partial class CrudProbeJsonContext : System.Text.Json.Serialization.JsonSerializerContext
            {
                internal CrudProbeJsonContext(global::System.Text.Json.JsonSerializerOptions options) : base(options)
                {
                }
            }
        }
        """;

    private static (string Source, string ExpectedReason) ResponseRootMutation(string mutation) => mutation switch
    {
        "fake-contracts" => ("""
            namespace ReviewUnrelated
            {
                public class JsonTypeInfo<T> { }
                public class DynamicApiResponse<T> { }
                public class DynamicApiResponse { }
                public class PagedResultDto<T> { }
                public class ProductDto { }

                public class UnusedFields
                {
                    public JsonTypeInfo<DynamicApiResponse<ProductDto>> DtoInfo;
                    public JsonTypeInfo<DynamicApiResponse<PagedResultDto<ProductDto>>> PageInfo;
                    public JsonTypeInfo<DynamicApiResponse> VoidInfo;
                }
            }
            """, "must declare a generated context derived from the referenced"),
        "fake-base-context" => ("""
            namespace Shop.Application.Products.Contracts
            {
                public class ProductDto { }
            }

            namespace ReviewUnrelated
            {
                public abstract class JsonSerializerContext { }
            }

            namespace CrestCreates.Generated
            {
                internal sealed class CrudProbeJsonContext : global::ReviewUnrelated.JsonSerializerContext
                {
                }
            }
            """, "must declare a generated context derived from the referenced"),
        "empty-official-context" => (FormalReadDto + """
            namespace CrestCreates.Generated
            {
                internal partial class CrudProbeJsonContext : System.Text.Json.Serialization.JsonSerializerContext
                {
                    internal CrudProbeJsonContext(global::System.Text.Json.JsonSerializerOptions options) : base(options)
                    {
                    }
                }
            }
            """, "must be registered as a [JsonSerializable] root"),
        "object-envelope-root" => (FormalReadDto + """
            namespace CrestCreates.Generated
            {
                [System.Text.Json.Serialization.JsonSerializable(typeof(global::System.Object))]
                [System.Text.Json.Serialization.JsonSerializable(typeof(global::CrestCreates.DynamicApi.DynamicApiResponse<object>))]
                internal partial class CrudProbeJsonContext : System.Text.Json.Serialization.JsonSerializerContext
                {
                    internal CrudProbeJsonContext(global::System.Text.Json.JsonSerializerOptions options) : base(options)
                    {
                    }
                }
            }
            """, "DynamicApiResponse<object> is not a formal CRUD result envelope"),
        _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
    };

    private const string FormalReadDto = """
        namespace Shop.Application.Products.Contracts
        {
            public class ProductDto { }
        }
        """;

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

        var (_, runResult, _) = RunRealDriver(privateSetterSource);

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

        var (_, runResult, _) = RunRealDriver(protectedIdSource);

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

        var (_, runResult, _) = RunRealDriver(excludedSource);

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

        var (_, runResult, _) = RunRealDriver(platformFieldsSource);

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

        var (_, runResult, _) = RunRealDriver(noCtorSource);

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

        var (_, runResult, _) = RunRealDriver(protectedCtorSource);

        var diagnostics = runResult.Diagnostics
            .Where(d => d.Id == CrudServiceDiagnosticCodes.StaticClosureUnsupportedValue)
            .ToList();

        diagnostics.Should().NotBeEmpty("an entity with only a protected parameterless constructor must trigger CCCRUD004");
        diagnostics.Should().ContainSingle("one diagnostic per entity");
        diagnostics[0].GetMessage().Should().Contain("public parameterless constructor",
            "the diagnostic must specify that a public constructor is required");
    }

    private static readonly string[] ForbiddenLegacyRegistrationSymbols =
    [
        "[ModuleInitializer]",
        "DescriptorProviderRegistry.Register",
        "CapabilityEndpointBindingRegistry.Register",
        "CapabilityEndpointJsonContractRegistry.RegisterBodyType",
        "CapabilityHandlerResolverProvider.Register",
        "CapabilityEndpointResultContractRegistration.Register",
        "AppServiceCompatibilityProjectionManifestRegistry.Register",
        "DynamicApiGeneratedRegistryStore",
        "IDynamicApiGeneratedProvider",
    ];

    [Fact]
    public void Materialized_bundle_must_activate_through_the_explicit_host_contribution()
    {
        // #128 S3 step B: the projection is delivered as an ExplicitHost contribution plus the
        // per-assembly AddGeneratedCrud extension, and formal CRUD never registers through any of the
        // legacy process-global initializers (§7.2/§9).
        var producer = CrudPrecompileProducer.Run(DeclarationSource, ClosureReferences());

        producer.Diagnostics.Should().BeEmpty(
            "the declaration must build a clean model: "
            + string.Join(" | ", producer.Diagnostics.Select(issue => issue.ToDisplayString())));

        var bundle = string.Join("\n", producer.BundleTrees.Select(tree => tree.ToString()));
        bundle.Should().Contain("AddGeneratedCrud",
            "the assembly must expose the explicit per-assembly activation extension");
        bundle.Should().Contain("CapabilityEndpointContribution",
            "the projection must be delivered as one contribution per service");
        bundle.Should().Contain("AddCrestCapabilityEndpointContribution",
            "activation must go through the DI entry point, not a global registry");
        bundle.Should().Contain("CapabilityEndpointBindingContract",
            "every action needs a binding contract");
        bundle.Should().Contain("CapabilityEndpointHandlerBinding",
            "every action needs an invoker binding");
        bundle.Should().Contain("CapabilityEndpointResultBinding",
            "every action needs a typed result mapper binding");
        bundle.Should().Contain("CapabilityEndpointRequiredIdentity",
            "every action must declare its required registry identity");

        // #128 S3 (G2): contributed handlers must reach the composed dispatcher without hand-written
        // glue — a real host otherwise answers HANDLER_NOT_FOUND and never calls the AppService.
        bundle.Should().Contain("ICapabilityHandlerModule",
            "the activation must register the handler module contract");
        bundle.Should().Contain("GeneratedCrudHandlerModule_",
            "the generated assembly needs its own handler module type");
        foreach (var capabilityId in new[]
                 {
                     "Shop.Application.ProductAppService.Create", "Shop.Application.ProductAppService.Get",
                     "Shop.Application.ProductAppService.Search", "Shop.Application.ProductAppService.Update",
                     "Shop.Application.ProductAppService.Delete",
                 })
        {
            bundle.Should().Contain("resolver.Register(\"" + capabilityId + "\"",
                $"the handler module must register the invoker of '{capabilityId}'");
        }
        bundle.Should().Contain("CapabilityEndpointJsonRootType",
            "every required JSON root must be declared for the map-time verification");
        bundle.Should().Contain("RequiredJsonRootTypes",
            "the contribution must carry the JSON roots the host's final options have to answer");
        bundle.Should().Contain("CrestCreatesCrudJsonSerializerContext",
            "the contribution must reference the assembly's one source-generated CRUD JSON context");
        bundle.Should().Contain("IConfigureOptions",
            "the activation must register the generated JsonOptions configurator");
        bundle.Should().Contain("JsonTypeInfoResolver.Combine",
            "the configurator must append the generated context after the host's own resolvers");

        foreach (var forbidden in ForbiddenLegacyRegistrationSymbols)
        {
            bundle.Should().NotContain(forbidden,
                $"formal CRUD must never register through the legacy global path ('{forbidden}')");
        }
    }

    [Fact]
    public void Materialized_bundle_must_declare_the_json_root_contract_and_host_wiring()
    {
        // #128 S3 step C (§7.1/§7.2): the contribution carries the assembly's generated JSON context
        // plus the exact §7.1 roots the host's final Http.Json.JsonOptions has to answer; the assembly
        // activation registers an IConfigureOptions<JsonOptions> configurator that appends the context
        // after any resolver the host configured itself. Nothing may install a reflection resolver.
        var producer = CrudPrecompileProducer.Run(DeclarationSource, ClosureReferences());

        producer.Diagnostics.Should().BeEmpty(
            "the declaration must build a clean model: "
            + string.Join(" | ", producer.Diagnostics.Select(issue => issue.ToDisplayString())));

        var bundle = string.Join("\n", producer.BundleTrees.Select(tree => tree.ToString()));

        // The one-driver harness closure deliberately carries only the references the contract checks
        // need (real MSBuild supplies the rest), so errors before the JSON contract block are closure
        // gaps, not producer defects. From the JSON contract block to the end of the bundle — which is
        // exactly the emitted JSON contract plus the assembly activation artifact — nothing may fail
        // to compile: a malformed generated statement must be caught here, not only by the real build.
        var jsonBlockStartLine = bundle[..bundle.IndexOf(
            "JsonContext = global::", StringComparison.Ordinal)].Count(character => character == '\n');
        var jsonContractErrors = producer.Compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Location.SourceTree is { } tree
                && tree.FilePath.EndsWith("CrestCreates.Crud.g.cs", StringComparison.Ordinal)
                && diagnostic.Location.GetLineSpan().StartLinePosition.Line >= jsonBlockStartLine)
            .Select(diagnostic => diagnostic.ToString())
            .ToArray();
        jsonContractErrors.Should().BeEmpty(
            "the generated JSON contract and host wiring must compile in the one-driver harness: "
            + string.Join(" | ", jsonContractErrors));

        bundle.Should().Contain(
            "JsonContext = global::CrestCreates.Generated.CrestCreatesCrudJsonSerializerContext.Default",
            "the contribution must deliver the assembly's source-generated CRUD context");
        bundle.Should().Contain(
            "RequiredJsonRootTypes = new global::CrestCreates.DynamicApi.CapabilityEndpointJsonRootType[]");

        // The §7.1 roots, each derived from the model: request bodies, the two capability input
        // envelopes, the read DTO, the bounded page payload, the typed and void result envelopes,
        // plus the error DTO the failure path in use (ProblemDetails) needs in the closure.
        string[] requiredRoots =
        [
            "typeof(global::Shop.Application.Products.Contracts.ProductDto),",
            "typeof(global::Shop.Application.Products.Contracts.CreateProductDto),",
            "typeof(global::Shop.Application.Products.Contracts.UpdateProductDto),",
            "typeof(global::Shop.Application.Products.Contracts.ProductListRequestDto),",
            "typeof(global::CrestCreates.Generated.ProductAppService_Update_CompatibilityInput),",
            "typeof(global::CrestCreates.Generated.ProductAppService_Delete_CompatibilityInput),",
            "typeof(global::CrestCreates.Application.Contracts.DTOs.Common.PagedResultDto<global::Shop.Application.Products.Contracts.ProductDto>),",
            "typeof(global::CrestCreates.DynamicApi.DynamicApiResponse<global::Shop.Application.Products.Contracts.ProductDto>),",
            "typeof(global::CrestCreates.DynamicApi.DynamicApiResponse<global::CrestCreates.Application.Contracts.DTOs.Common.PagedResultDto<global::Shop.Application.Products.Contracts.ProductDto>>),",
            "typeof(global::CrestCreates.DynamicApi.DynamicApiResponse),",
            "typeof(global::Microsoft.AspNetCore.Mvc.ProblemDetails),",
        ];
        foreach (var root in requiredRoots)
        {
            bundle.Should().Contain(root, "the §7.1 root matrix must be declared from the model");
        }

        var declaredRootCount = System.Text.RegularExpressions.Regex.Matches(
            bundle, @"new global::CrestCreates\.DynamicApi\.CapabilityEndpointJsonRootType\(").Count;
        declaredRootCount.Should().Be(requiredRoots.Length,
            "the default CRUD surface declares exactly the eleven §7.1 roots, deduplicated by type");

        bundle.Should().Contain("internal sealed class GeneratedCrudJsonOptionsConfigurator",
            "the assembly activation must emit the host wiring type");
        bundle.Should().Contain(
            "global::Microsoft.Extensions.Options.IConfigureOptions<global::Microsoft.AspNetCore.Http.Json.JsonOptions>",
            "the wiring must plug into the host's own Http.Json.JsonOptions");
        bundle.Should().Contain("JsonTypeInfoResolver.Combine(existing, generatedContext)",
            "an existing host resolver must stay first and keep winning for the types it knows");
        bundle.Should().Contain("ServiceCollectionDescriptorExtensions.TryAddEnumerable",
            "repeated AddGeneratedCrud calls must stay idempotent");
        bundle.Should().NotContain("DefaultJsonTypeInfoResolver",
            "the generated wiring must never install or fall back to a reflection resolver");
    }
}
