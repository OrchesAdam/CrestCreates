using System;
using System.Collections.Generic;
using System.Linq;
using CrestCreates.JsonContracts.BuildTasks.Diagnostics;
using CrestCreates.JsonContracts.BuildTasks.Model;
using CrestCreates.Tooling.CrudGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CrestCreates.JsonContracts.BuildTasks.Semantic;

/// <summary>
/// #128 S2: discovers the materialized CRUD surface and turns it into the closed JSON roots of §7.1.
/// The CRUD bundle arrives as ordinary Compile input, so the roots are built from real symbols here
/// and the official STJ generator produces the metadata in the same CoreCompile — no second generator
/// reads another generator's output. Every required member of the root matrix is resolved through the
/// shared <see cref="CrudGenerationModel"/>, so a name that only the JSON side invented cannot appear.
/// </summary>
public sealed class CrudJsonSurfaceDiscovery
{
    private readonly List<JsonContractDiagnostic> _diagnostics = [];

    public IReadOnlyList<JsonContractDiagnostic> Diagnostics => _diagnostics;

    public List<JsonContractRootModel> Discover(CSharpCompilation compilation, string assemblyName)
    {
        var roots = new List<JsonContractRootModel>();
        var build = CrudGenerationModelBuilder.Build(compilation, assemblyName);

        foreach (var issue in build.Diagnostics)
        {
            _diagnostics.Add(new JsonContractDiagnostic
            {
                Id = issue.Id,
                Severity = JsonContractDiagnosticSeverity.Error,
                Message = issue.Message,
                FilePath = issue.Location?.Path ?? string.Empty,
                Line = issue.Location?.Line ?? 0,
                Column = issue.Location?.Column ?? 0,
            });
        }

        // The result-envelope contract types are only required when this compilation actually
        // declares CRUD: a contracts-only or Abstractions project must not fail for types it never uses.
        if (build.Models.Count == 0)
        {
            return roots;
        }

        var envelopeOpen = compilation.GetTypeByMetadataName(
            "CrestCreates.DynamicApi.DynamicApiResponse`1");
        var voidEnvelope = compilation.GetTypeByMetadataName("CrestCreates.DynamicApi.DynamicApiResponse");
        var pagedOpen = compilation.GetTypeByMetadataName(
            "CrestCreates.Application.Contracts.DTOs.Common.PagedResultDto`1");

        if (envelopeOpen is null || voidEnvelope is null || pagedOpen is null)
        {
            _diagnostics.Add(new JsonContractDiagnostic
            {
                Id = CrudDiagnosticIds.RequiredReferenceMissing,
                Severity = JsonContractDiagnosticSeverity.Error,
                Message = $"'{build.Models[0].ServiceFullName}' declares CRUD, so the result envelopes need "
                    + "the referenced DynamicApiResponse, DynamicApiResponse<T> and PagedResultDto<T> types.",
            });
            return roots;
        }

        foreach (var model in build.Models)
        {
            // §7.1: public DTOs and internal capability-input envelopes are validated separately;
            // same-name types from another assembly never reach here because resolution is by FQN.
            AddDeclaredRoot(roots, compilation, model, model.ReadDtoFullName, "public", required: true);
            AddDeclaredRoot(roots, compilation, model, model.CreateDtoFullName, "public", required: true);
            AddDeclaredRoot(roots, compilation, model, model.UpdateDtoFullName, "public", required: true);
            AddDeclaredRoot(roots, compilation, model, model.ListRequestFullName, "public", required: true);
            AddDeclaredRoot(roots, compilation, model, model.EnvelopeName("Update"), "internal", required: true);
            AddDeclaredRoot(roots, compilation, model, model.EnvelopeName("Delete"), "internal", required: true);

            var readDto = compilation.GetTypeByMetadataName(model.ReadDtoFullName);
            if (readDto is null)
            {
                continue;
            }

            var paged = pagedOpen.Construct(readDto);
            AddRoot(roots, paged, model);
            AddRoot(roots, envelopeOpen.Construct(readDto), model);
            AddRoot(roots, envelopeOpen.Construct(paged), model);
            AddRoot(roots, voidEnvelope, model);
        }

        return roots;
    }

    private void AddDeclaredRoot(
        List<JsonContractRootModel> roots,
        CSharpCompilation compilation,
        CrudGenerationModel model,
        string fullMetadataName,
        string expectedAccessibility,
        bool required)
    {
        var type = compilation.GetTypeByMetadataName(fullMetadataName);
        if (type is null)
        {
            if (required)
            {
                _diagnostics.Add(new JsonContractDiagnostic
                {
                    Id = CrudDiagnosticIds.ToolchainNotAssembled,
                    Severity = JsonContractDiagnosticSeverity.Error,
                    Message = $"'{fullMetadataName}' is declared by a formal CRUD service but is not "
                        + "present in the compilation input; the CRUD precompile output is missing or stale.",
                    FilePath = model.DeclarationLocation.Path,
                    Line = model.DeclarationLocation.Line,
                    Column = model.DeclarationLocation.Column,
                });
            }

            return;
        }

        if (type is IErrorTypeSymbol)
        {
            _diagnostics.Add(new JsonContractDiagnostic
            {
                Id = CrudDiagnosticIds.StaticClosureUnsupported,
                Severity = JsonContractDiagnosticSeverity.Error,
                Message = $"'{fullMetadataName}' resolves to an error type.",
            });
            return;
        }

        var actual = type.DeclaredAccessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            _ => type.DeclaredAccessibility.ToString().ToLowerInvariant(),
        };
        if (!string.Equals(actual, expectedAccessibility, StringComparison.Ordinal))
        {
            _diagnostics.Add(new JsonContractDiagnostic
            {
                Id = CrudDiagnosticIds.StaticClosureUnsupported,
                Severity = JsonContractDiagnosticSeverity.Error,
                Message = $"'{fullMetadataName}' must be {expectedAccessibility} on the CRUD wire "
                    + $"contract but is {actual}.",
            });
            return;
        }

        AddRoot(roots, type, model);
    }

    private static void AddRoot(List<JsonContractRootModel> roots, ITypeSymbol type, CrudGenerationModel model)
    {
        if (type is null)
        {
            return;
        }

        if (roots.Any(root => SymbolEqualityComparer.Default.Equals(root.RootType, type)))
        {
            return;
        }

        roots.Add(new JsonContractRootModel
        {
            RootType = type,
            FullMetadataName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Provenance = new JsonContractRootProvenance
            {
                Origins =
                [
                    new JsonContractRootOrigin
                    {
                        SourceKind = JsonContractRootSourceKind.CrudSurface,
                        DeclaringSurface = model.ServiceFullName,
                        DeclarationName = model.OwnerAssemblyName,
                        RoleName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    },
                ],
            },
        });
    }
}
