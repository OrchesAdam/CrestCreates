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
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
        return (updated, driver.GetRunResult());
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
}
