using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using CrestCreates.CodeGenerator.CrudServiceGenerator;
using CrestCreates.CodeGenerator.EntityGenerator;
using CrestCreates.CodeGenerator.ServiceGenerator;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Tooling.CrudGeneration;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.CrudServiceGenerator;

/// <summary>
/// #128 S2 item ① baselines for artifact ownership. §6.1 states that
/// <c>EntitySourceGenerator</c> and the service registration generator must explicitly skip the concrete
/// products the CRUD precompile manifest already owns and must not emit a second file with the same name,
/// while keeping every other product of a plain <c>[Entity]</c> or a hand-written AppService unchanged.
/// The harness authors a declaration that really collides (the same <c>[Entity] Product</c> plus a formal
/// <c>[GenerateCrudService(typeof(Product))]</c> service whose contracts namespace is exactly the one
/// <c>EntitySourceGenerator</c> uses for its own <c>Product</c> DTOs), materializes the manifest through
/// the shared producer rule (<see cref="CrudArtifactEmitter.DeclaredTypeFullNames"/>) and hands it to one
/// real <see cref="CSharpGeneratorDriver"/> as an <c>AdditionalText</c>. Every case is booked twice: with
/// the manifest the owned artifact is absent, <b>without the manifest it is present</b> — that half is
/// what makes the absence assertion discriminating instead of vacuous. The helper's stub attributes and
/// serial-driver path are deliberately not used.
/// </summary>
public class CrudArtifactOwnershipSkipTests
{
    private const string EntityCollisionSource = """
        using System;
        using CrestCreates.Domain.Shared.Attributes;

        namespace Shop.Catalog
        {
            [Entity]
            public class Product
            {
                public Guid Id { get; set; }
                public string Name { get; set; } = string.Empty;
                public decimal Price { get; set; }
            }

            // The DtoNamespace is exactly the namespace EntitySourceGenerator derives for the DTOs of an
            // entity that is not under ".Domain.Entities", so both producers target the same four types.
            [GenerateCrudService(typeof(Product), DtoNamespace = "Shop.Catalog.Dtos")]
            public partial class ProductAppService
            {
            }

            // An entity the CRUD manifest owns nothing of: every one of its artifacts must stay emitted.
            [Entity]
            public class Order
            {
                public Guid Id { get; set; }
                public string Reference { get; set; } = string.Empty;
            }
        }
        """;

    private const string ServiceCollisionSource = """
        using System;
        using CrestCreates.Domain.Shared.Attributes;

        namespace Shop.Catalog
        {
            public class Product
            {
                public Guid Id { get; set; }
                public string Name { get; set; } = string.Empty;
                public decimal Price { get; set; }
            }

            [CrestService]
            [GenerateCrudService(typeof(Product), DtoNamespace = "Shop.Catalog.Dtos")]
            public partial class ProductAppService
            {
            }

            [CrestService]
            public partial class ReportAppService
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
        "Rougamo",
    ];

    private static IReadOnlyList<MetadataReference> ClosureReferences()
    {
        var referencePaths = new List<string>
        {
            typeof(object).Assembly.Location,
            typeof(GenerateCrudServiceAttribute).Assembly.Location,
        };
        foreach (var referenceName in DeclaredReferenceNames)
        {
            try
            {
                referencePaths.Add(System.Reflection.Assembly.Load(referenceName).Location);
            }
            catch (Exception)
            {
                // Unavailable optional reference: the closure carries what is actually present.
            }
        }

        // The CRUD HTTP projection binds ASP.NET and DI types a real consumer receives from the
        // ASP.NET Core shared framework; the framework files are not copied next to the test
        // assembly, so the whole framework reference is closed over (see the helper).
        referencePaths.AddRange(CrudConsumerFrameworkClosure.AspNetCoreSharedFrameworkAssemblies());

        return referencePaths
            .Distinct()
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    // ---- EntitySourceGenerator -----------------------------------------------------------------

    [Fact]
    public void Manifest_owned_dto_and_mapping_artifacts_must_be_skipped_by_the_entity_generator()
    {
        var manifest = ManifestJson(EntityCollisionSource);

        var files = RunEntityGenerator(EntityCollisionSource, manifest);

        files.Should().NotContainKey("ProductDto.g.cs",
            "the CRUD manifest owns Shop.Catalog.Dtos.ProductDto, so the entity generator must not emit a second one");
        files.Should().NotContainKey("CreateProductDto.g.cs",
            "the CRUD manifest owns Shop.Catalog.Dtos.CreateProductDto");
        files.Should().NotContainKey("UpdateProductDto.g.cs",
            "the CRUD manifest owns Shop.Catalog.Dtos.UpdateProductDto");
        files.Should().NotContainKey("ProductObjectMappings.g.cs",
            "the CRUD manifest owns Shop.Catalog.Mappings.ProductObjectMappings");

        // §6.1 skips only the owned artifact: everything else about the same entity stays generated.
        files.Should().ContainKey("ProductRepositoryBase.g.cs",
            "the repository base is not owned by the CRUD manifest and must stay generated");
        files.Should().ContainKey("ProductPermissions.g.cs",
            "the entity permission class has a different name than the CRUD permissions class and must stay generated");
        files.Should().ContainKey("ProductExtensions.g.cs",
            "the entity extensions are not owned by the CRUD manifest and must stay generated");

        // An entity the manifest owns nothing of keeps all four of its own artifacts.
        files.Should().ContainKey("OrderDto.g.cs", "unrelated entities must not be affected by an ownership skip");
        files.Should().ContainKey("CreateOrderDto.g.cs", "unrelated entities must not be affected by an ownership skip");
        files.Should().ContainKey("UpdateOrderDto.g.cs", "unrelated entities must not be affected by an ownership skip");
        files.Should().ContainKey("OrderObjectMappings.g.cs", "unrelated entities must not be affected by an ownership skip");
    }

    [Fact]
    public void Without_the_manifest_the_entity_generator_still_emits_the_colliding_artifacts()
    {
        // The discriminating half: the same authored source, no ownership AdditionalText. If this case
        // ever stops emitting these four files, the previous absence assertion proves nothing.
        var files = RunEntityGenerator(EntityCollisionSource, manifestJson: null);

        files.Should().ContainKey("ProductDto.g.cs",
            "without an ownership manifest the entity generator owns the Product DTO as before");
        files.Should().ContainKey("CreateProductDto.g.cs");
        files.Should().ContainKey("UpdateProductDto.g.cs");
        files.Should().ContainKey("ProductObjectMappings.g.cs");

        files["ProductDto.g.cs"].Should().Contain("namespace Shop.Catalog.Dtos")
            .And.Contain("public partial class ProductDto",
                "the control must really be the artifact the manifest-owned case skips");

        // The artifacts the ownership case expects to survive must also survive here, otherwise the
        // "not skipped" assertions in the first case would only measure a generator that emitted nothing.
        files.Should().ContainKey("ProductRepositoryBase.g.cs");
        files.Should().ContainKey("ProductPermissions.g.cs");
        files.Should().ContainKey("ProductExtensions.g.cs");
        files.Should().ContainKey("OrderDto.g.cs");
        files.Should().ContainKey("OrderObjectMappings.g.cs");
    }

    [Fact]
    public void Ownership_skip_is_per_artifact_not_per_entity()
    {
        // Synthetic manifest: exactly one of the four owned fully-qualified names is kept. A generator
        // that skipped the whole entity once any of its products were owned would fail here.
        var manifest = ManifestJson(EntityCollisionSource, onlyOwned: ["Shop.Catalog.Dtos.ProductDto"]);

        var files = RunEntityGenerator(EntityCollisionSource, manifest);

        files.Should().NotContainKey("ProductDto.g.cs", "the one owned DTO must be skipped");
        files.Should().ContainKey("CreateProductDto.g.cs", "an artifact the manifest does not own must stay emitted");
        files.Should().ContainKey("UpdateProductDto.g.cs", "an artifact the manifest does not own must stay emitted");
        files.Should().ContainKey("ProductObjectMappings.g.cs", "an artifact the manifest does not own must stay emitted");
    }

    [Fact]
    public void Unparsable_or_foreign_additional_files_are_treated_as_no_ownership_and_never_throw()
    {
        var unparsable = RunEntityGenerator(EntityCollisionSource, manifestJson: "{ this is not json");
        unparsable.Should().ContainKey("ProductDto.g.cs",
            "an unparsable manifest must not delete framework output");

        var foreign = RunEntityGenerator(EntityCollisionSource,
            manifestJson: ManifestJson(EntityCollisionSource),
            manifestFileName: "CrestCreates.SomethingElse.json");
        foreign.Should().ContainKey("ProductDto.g.cs",
            "only the CRUD outputs manifest path may be read as ownership evidence");

        foreach (var files in new[] { unparsable, foreign })
        {
            files.Keys.Should().NotBeEmpty("the generator must still produce its normal output");
        }
    }

    [Fact]
    public void Entity_generator_must_not_fail_when_the_manifest_is_read()
    {
        var (result, _) = RunEntityDriver(EntityCollisionSource, ManifestJson(EntityCollisionSource));

        result.Diagnostics
            .Select(diagnostic => diagnostic.Id)
            .Should().NotContain("AD0001",
                "the ownership reader must never throw out of a generator; observed: "
                + string.Join(" | ", result.Diagnostics.Select(diagnostic => diagnostic.ToString())));
    }

    // ---- ServiceSourceGenerator ----------------------------------------------------------------

    [Fact]
    public void Manifest_owned_service_must_be_left_out_of_the_auto_service_registration()
    {
        var files = RunServiceGenerator(ServiceCollisionSource, ManifestJson(ServiceCollisionSource));

        var registration = files["AutoServiceRegistration.g.cs"];
        registration.Should().NotContain("Shop.Catalog.ProductAppService",
            "formal CRUD activation is an explicit per-assembly contribution, so an owned service must not also be auto-registered");
        registration.Should().Contain("Shop.Catalog.ReportAppService",
            "a service the manifest does not own must stay auto-registered");
    }

    [Fact]
    public void Without_the_manifest_the_owned_service_is_auto_registered()
    {
        // The discriminating half for the registration claim.
        var files = RunServiceGenerator(ServiceCollisionSource, manifestJson: null);

        var registration = files["AutoServiceRegistration.g.cs"];
        registration.Should().Contain("Shop.Catalog.ProductAppService",
            "without an ownership manifest the service keeps its existing auto-registration");
        registration.Should().Contain("Shop.Catalog.ReportAppService");
    }

    [Fact]
    public void Manifest_owned_service_interface_must_not_get_a_second_file()
    {
        // The interface skip is by fully-qualified name: the manifest owns this interface type, so the
        // generator must not emit its own copy of it.
        var manifest = ManifestJson(ServiceCollisionSource, extraOwned: ["Shop.Catalog.IProductAppService"]);

        var files = RunServiceGenerator(ServiceCollisionSource, manifest);

        files.Should().NotContainKey("IProductAppService.g.cs",
            "the manifest owns Shop.Catalog.IProductAppService");
        files.Should().ContainKey("IReportAppService.g.cs",
            "an interface the manifest does not own must stay generated");
    }

    [Fact]
    public void Without_the_manifest_the_service_interface_is_generated()
    {
        // The discriminating half, and it also proves the producer-derived manifest does not silently
        // suppress the interface: the CRUD surface declares its interface in the .Services namespace,
        // which is a different fully-qualified name than the one this generator emits.
        var files = RunServiceGenerator(ServiceCollisionSource, manifestJson: null);

        files.Should().ContainKey("IProductAppService.g.cs");
        files["IProductAppService.g.cs"].Should().Contain("public interface IProductAppService",
            "the control must really be the artifact the owned-interface case skips");

        RunServiceGenerator(ServiceCollisionSource, ManifestJson(ServiceCollisionSource))
            .Should().ContainKey("IProductAppService.g.cs",
                "owning the .Services interface must not skip the differently-named interface file");
    }

    // ---- ownership reader ----------------------------------------------------------------------

    [Fact]
    public void Reader_returns_plain_fully_qualified_names_and_a_stable_value_for_the_pipeline()
    {
        var manifest = ManifestJson(EntityCollisionSource);
        var first = OwnedSetOf(manifest);
        var second = OwnedSetOf(manifest);

        first.Should().Contain("Shop.Catalog.Dtos.ProductDto",
            "the manifest declares plain type names; the owner identity stays a sibling field (§5.1)");
        first.Should().NotContain(name => name.Contains('|'),
            "an owner-prefixed type name can never match a fully-qualified type");
        first.Should().Equal(second,
            "the same manifest must yield the same immutable, deterministically ordered value, so the "
            + "ownership step cannot churn the pipeline between compilations");
        first.KeyComparer.Should().Be(StringComparer.Ordinal,
            "ownership matching is ordinal, never culture-sensitive");

        // The absent-manifest path is the common one for every project without a CRUD surface: it must
        // stay one canonical instance so the pipeline never sees a value change there.
        ReferenceEquals(CrudArtifactOwnership.Read(ImmutableArray<AdditionalText>.Empty),
            CrudArtifactOwnership.Read(ImmutableArray<AdditionalText>.Empty)).Should().BeTrue();
        CrudArtifactOwnership.Read(ImmutableArray<AdditionalText>.Empty)
            .Should().BeSameAs(CrudArtifactOwnership.None,
                "an absent manifest must not allocate a fresh set on every compilation");
        CrudArtifactOwnership
            .Read(ImmutableArray.Create<AdditionalText>(new InMemoryAdditionalText(
                "/obj/Debug/net10.0/CrestCreates.Crud.outputs.json", "{ \"Entries\": [] }")))
            .Should().BeEmpty("a manifest without entries owns nothing");
    }

    [Theory]
    [InlineData("Archived.CrestCreates.Crud.outputs.json", false)]
    [InlineData("CrestCreates.Crud.outputs.json", true)]
    public void Foreign_filename_or_unsupported_schema_must_not_claim_ownership(string name, bool unknownSchema)
    {
        var json = ManifestJson(EntityCollisionSource);
        if (unknownSchema) json = json.Replace("crud-artifacts-v1", "future-schema");
        var owned = CrudArtifactOwnership.Read(ImmutableArray.Create<AdditionalText>(
            new InMemoryAdditionalText("/obj/" + name, json)));
        owned.Should().BeSameAs(CrudArtifactOwnership.None);
        RunEntityGenerator(EntityCollisionSource, json, name).Should().ContainKey("ProductDto.g.cs");
    }

    [Fact]
    public void Incremental_comparer_must_compare_nonempty_ownership_by_content()
    {
        var first = OwnedSetOf(ManifestJson(EntityCollisionSource));
        var second = OwnedSetOf(ManifestJson(EntityCollisionSource));
        CrudArtifactOwnership.ContentComparer.Equals(first, second).Should().BeTrue();
        CrudArtifactOwnership.ContentComparer.GetHashCode(first)
            .Should().Be(CrudArtifactOwnership.ContentComparer.GetHashCode(second));
        CrudArtifactOwnership.ContentComparer.Equals(first, CrudArtifactOwnership.None).Should().BeFalse();
    }

    private static ImmutableSortedSet<string> OwnedSetOf(string manifestJson) =>
        CrudArtifactOwnership.Read(ImmutableArray.Create<AdditionalText>(new InMemoryAdditionalText(
            "/obj/Debug/net10.0/" + CrudArtifactOwnership.ManifestFileName, manifestJson)));

    // ---- harness -------------------------------------------------------------------------------

    private static IReadOnlyDictionary<string, string> RunEntityGenerator(
        string source,
        string? manifestJson,
        string manifestFileName = "CrestCreates.Crud.outputs.json")
    {
        var (result, _) = RunEntityDriver(source, manifestJson, manifestFileName);
        return result.GeneratedTrees.ToDictionary(
            tree => Path.GetFileName(tree.FilePath), tree => tree.ToString(), StringComparer.Ordinal);
    }

    private static (GeneratorDriverRunResult RunResult, Compilation Updated) RunEntityDriver(
        string source,
        string? manifestJson,
        string manifestFileName = "CrestCreates.Crud.outputs.json")
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new EntitySourceGenerator());
        return Run(driver, source, manifestJson, manifestFileName);
    }

    private static IReadOnlyDictionary<string, string> RunServiceGenerator(string source, string? manifestJson)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ServiceSourceGenerator());
        var (result, _) = Run(driver, source, manifestJson, "CrestCreates.Crud.outputs.json");
        return result.GeneratedTrees.ToDictionary(
            tree => Path.GetFileName(tree.FilePath), tree => tree.ToString(), StringComparer.Ordinal);
    }

    private static (GeneratorDriverRunResult RunResult, Compilation Updated) Run(
        GeneratorDriver driver,
        string source,
        string? manifestJson,
        string manifestFileName)
    {
        var parseOptions = new CSharpParseOptions();
        var compilation = CSharpCompilation.Create(
            "Shop.Application",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            ClosureReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var texts = new List<AdditionalText>();
        if (manifestJson is not null)
        {
            texts.Add(new InMemoryAdditionalText(
                Path.Combine("/obj/Debug/net10.0", manifestFileName), manifestJson));
        }

        driver = driver.AddAdditionalTexts(texts.ToImmutableArray());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
        return (driver.GetRunResult(), updated);
    }

    /// <summary>
    /// Builds the outputs manifest the CRUD precompile stage would write for this authored source, using
    /// the shared model + emitter + the one declared-type-name rule, so the ownership evidence under test
    /// is the real producer identity rather than a name this test invented.
    /// </summary>
    private static string ManifestJson(
        string source,
        IReadOnlyList<string>? onlyOwned = null,
        IReadOnlyList<string>? extraOwned = null)
    {
        var producer = CrudPrecompileProducer.Run(source, ClosureReferences());
        producer.Diagnostics.Should().BeEmpty(
            "the authored collision scenario must produce a CRUD surface without CCCRUD diagnostics: "
            + string.Join(" | ", producer.Diagnostics.Select(issue => issue.ToDisplayString())));
        producer.Models.Should().NotBeEmpty("the formal declaration must build a model");

        var entries = new List<object>();
        foreach (var model in producer.Models)
        {
            foreach (var artifact in CrudArtifactEmitter.Emit(model))
            {
                var declared = CrudArtifactEmitter.DeclaredTypeFullNames(artifact.Source);
                if (onlyOwned is not null)
                {
                    declared = declared
                        .Where(name => onlyOwned.Contains(name, StringComparer.Ordinal))
                        .ToArray();
                }

                entries.Add(new
                {
                    OwnerAssemblyName = model.OwnerAssemblyName,
                    OwnerServiceFullName = model.ServiceFullName,
                    ArtifactHintName = artifact.HintName,
                    DeclaredTypeFullNames = declared,
                    SourceLocation = model.DeclarationLocation.ToString(),
                    ModelSchemaVersion = CrudGenerationModel.ModelSchemaVersion,
                    ContentHash = "sha256:" + Convert
                        .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(artifact.Source)))
                        .ToLowerInvariant(),
                });
            }
        }

        foreach (var owned in extraOwned ?? Array.Empty<string>())
        {
            entries.Add(new
            {
                OwnerAssemblyName = producer.Models[0].OwnerAssemblyName,
                OwnerServiceFullName = producer.Models[0].ServiceFullName,
                ArtifactHintName = owned + ".g.cs",
                DeclaredTypeFullNames = new[] { owned },
                SourceLocation = producer.Models[0].DeclarationLocation.ToString(),
                ModelSchemaVersion = CrudGenerationModel.ModelSchemaVersion,
                ContentHash = "sha256:" + Convert
                    .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(owned)))
                    .ToLowerInvariant(),
            });
        }

        return JsonSerializer.Serialize(new
        {
            SchemaVersion = "crud-artifacts-v1",
            ModelSchemaVersion = CrudGenerationModel.ModelSchemaVersion,
            Entries = entries,
        });
    }

    private sealed class InMemoryAdditionalText : AdditionalText
    {
        private readonly SourceText _sourceText;

        public InMemoryAdditionalText(string path, string content)
        {
            Path = path;
            _sourceText = SourceText.From(content, Encoding.UTF8);
        }

        public override string Path { get; }

        public override SourceText GetText(CancellationToken cancellationToken = default) => _sourceText;
    }
}
