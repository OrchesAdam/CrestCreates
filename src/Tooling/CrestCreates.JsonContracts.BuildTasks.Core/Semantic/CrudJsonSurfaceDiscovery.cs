using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

    public List<JsonContractRootModel> Discover(
        CSharpCompilation compilation,
        string assemblyName,
        string? crudOutputsManifestJson)
    {
        var roots = new List<JsonContractRootModel>();
        var build = CrudGenerationModelBuilder.Build(compilation, assemblyName);
        ReconcileWithPrecompileManifest(build.Models, crudOutputsManifestJson, assemblyName);

        // The result-envelope contract types are required only when this compilation actually
        // declares CRUD, so a contracts-only project never fails for types it cannot use.
        if (build.Models.Count == 0)
        {
            return roots;
        }

        var envelopeOpen = ResolveFrameworkType(compilation, "CrestCreates.DynamicApi.DynamicApiResponse`1");
        var voidEnvelope = ResolveFrameworkType(compilation, "CrestCreates.DynamicApi.DynamicApiResponse");
        var pagedOpen = ResolveFrameworkType(compilation,
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
            AddDeclaredRoot(roots, compilation, model, CrudEnvelopeConvention.EnvelopeFullName(model.ServiceClassName, "Update"), "internal", required: true);
            AddDeclaredRoot(roots, compilation, model, CrudEnvelopeConvention.EnvelopeFullName(model.ServiceClassName, "Delete"), "internal", required: true);

            var readDto = ResolveType(compilation, model.ReadDtoFullName);
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
        var candidates = ResolveCandidates(compilation, fullMetadataName);
        var type = candidates.Count switch
        {
            0 => null,
            1 => candidates[0],
            _ => candidates.First(candidate => candidate.ContainingAssembly != compilation.Assembly),
        };
        if (candidates.Count > 1)
        {
            _diagnostics.Add(new JsonContractDiagnostic
            {
                Id = CrudDiagnosticIds.OwnerIdentityConflict,
                Severity = JsonContractDiagnosticSeverity.Error,
                Message = $"'{fullMetadataName}' is declared by {candidates.Count} inputs: "
                    + string.Join(", ", candidates.Select(candidate => candidate.ContainingAssembly?.Name))
                    + ". One CRUD contract type must have exactly one owner.",
            });
            return;
        }

        if (type is null)
        {
            if (required)
            {
                _diagnostics.Add(new JsonContractDiagnostic
                {
                    Id = CrudDiagnosticIds.ToolchainNotAssembled,
                    Severity = JsonContractDiagnosticSeverity.Error,
                    Message = $"'{fullMetadataName}' is declared by a formal CRUD service but is not "
                        + "present in the compilation input; the CRUD precompile output is missing or stale. "
                        + $"Source trees seen: {compilation.SyntaxTrees.Count()}, "
                        + $"root namespaces: {string.Join(", ", compilation.Assembly.GlobalNamespace.GetNamespaceMembers().Select(ns => ns.Name))}.",
                    FilePath = model.DeclarationLocation.Path,
                    Line = model.DeclarationLocation.Line,
                    Column = model.DeclarationLocation.Column,
                });
            }

            return;
        }

        if (!SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly))
        {
            ManifestError($"'{fullMetadataName}' must be declared by its current CRUD owner, not a referenced assembly.");
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

    /// <summary>
    /// §9 CCCRUD008: the JSON roots must agree with the CRUD precompile's own outputs manifest. A
    /// build that "succeeds" because the CRUD surface silently disappeared is the exact false green
    /// this stage must not be able to produce.
    /// </summary>
    private void ReconcileWithPrecompileManifest(
        IReadOnlyList<CrudGenerationModel> models,
        string? crudOutputsManifestJson,
        string assemblyName)
    {
        if (string.IsNullOrWhiteSpace(crudOutputsManifestJson))
        {
            if (models.Count > 0) ManifestError("A formal CRUD surface requires its precompile outputs manifest.");
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(crudOutputsManifestJson);
            var root = document.RootElement;
            if (root.GetProperty("SchemaVersion").GetString() != "crud-artifacts-v1"
                || root.GetProperty("ModelSchemaVersion").GetString() != CrudGenerationModel.ModelSchemaVersion)
            {
                ManifestError("Unsupported CRUD outputs manifest schema.");
                return;
            }
            var expected = models.SelectMany(model => CrudArtifactEmitter.Emit(model).Select(artifact =>
                (Model: model, Artifact: artifact))).ToDictionary(
                item => (item.Model.ServiceFullName, item.Artifact.HintName));
            var seen = new HashSet<(string Owner, string Hint)>();
            foreach (var entry in root.GetProperty("Entries").EnumerateArray())
            {
                var key = (entry.GetProperty("OwnerServiceFullName").GetString()!,
                    entry.GetProperty("ArtifactHintName").GetString()!);
                if (!seen.Add(key) || !expected.TryGetValue(key, out var item)
                    || entry.GetProperty("OwnerAssemblyName").GetString() != assemblyName
                    || entry.GetProperty("ModelSchemaVersion").GetString() != CrudGenerationModel.ModelSchemaVersion)
                {
                    ManifestError("CRUD artifact owner, schema or identity does not match the current declaration.");
                    continue;
                }
                var names = entry.GetProperty("DeclaredTypeFullNames").EnumerateArray()
                    .Select(value => value.GetString()!).OrderBy(value => value, StringComparer.Ordinal);
                var expectedNames = CrudArtifactEmitter.DeclaredTypeFullNames(item.Artifact.Source)
                    .OrderBy(value => value, StringComparer.Ordinal);
                var hash = "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.Artifact.Source))).ToLowerInvariant();
                if (!names.SequenceEqual(expectedNames, StringComparer.Ordinal)
                    || entry.GetProperty("ContentHash").GetString() != hash)
                    ManifestError("CRUD artifact declared types or content hash do not match the current producer model.");
            }
            if (seen.Count != expected.Count || expected.Keys.Any(key => !seen.Contains(key)))
                ManifestError("The CRUD outputs manifest does not cover the complete current artifact set.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            ManifestError("Invalid CRUD outputs manifest: " + ex.GetType().Name);
        }
    }

    private void ManifestError(string message) => _diagnostics.Add(new JsonContractDiagnostic
    {
        Id = CrudDiagnosticIds.LegacyContractMismatch,
        Severity = JsonContractDiagnosticSeverity.Error,
        Message = message,
    });

    private INamedTypeSymbol? ResolveFrameworkType(CSharpCompilation compilation, string fullMetadataName)
    {
        var candidates = ResolveCandidates(compilation, fullMetadataName);
        if (candidates.Count != 1
            || SymbolEqualityComparer.Default.Equals(candidates[0].ContainingAssembly, compilation.Assembly))
        {
            _diagnostics.Add(new JsonContractDiagnostic
            {
                Id = CrudDiagnosticIds.OwnerIdentityConflict,
                Severity = JsonContractDiagnosticSeverity.Error,
                Message = $"Framework contract '{fullMetadataName}' must resolve uniquely from a referenced assembly.",
            });
            return null;
        }
        return candidates[0];
    }

    private static INamedTypeSymbol? ResolveType(CSharpCompilation compilation, string fullMetadataName)
    {
        var candidates = ResolveCandidates(compilation, fullMetadataName);
        return candidates.Count == 0 ? null : candidates[0];
    }

    /// <summary>
    /// Resolves a CRUD contract type by walking the source assembly and then each referenced assembly.
    /// <c>Compilation.GetTypeByMetadataName</c> collapses ambiguity and absence into one null, which
    /// would let a duplicate owner or a stale bundle look like "materialization missing".
    /// </summary>
    private static IReadOnlyList<INamedTypeSymbol> ResolveCandidates(
        CSharpCompilation compilation,
        string fullMetadataName)
    {
        var lastDot = fullMetadataName.LastIndexOf('.');
        var namespaceName = fullMetadataName[..Math.Max(lastDot, 0)];
        var metadataName = fullMetadataName[(lastDot + 1)..];
        var arity = 0;
        var backtick = metadataName.IndexOf('`');
        if (backtick >= 0)
        {
            arity = int.Parse(metadataName[(backtick + 1)..]);
            metadataName = metadataName[..backtick];
        }

        List<INamedTypeSymbol> candidates = [];
        foreach (var @namespace in EnumerateNamespaces(compilation.Assembly.GlobalNamespace))
        {
            if (@namespace.ToDisplayString() != namespaceName)
            {
                continue;
            }

            candidates.AddRange(@namespace.GetTypeMembers(metadataName).Where(type => type.Arity == arity));
        }

        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
            {
                continue;
            }

            foreach (var @namespace in EnumerateNamespaces(assembly.GlobalNamespace))
            {
                if (@namespace.ToDisplayString() != namespaceName)
                {
                    continue;
                }

                candidates.AddRange(@namespace.GetTypeMembers(metadataName).Where(type => type.Arity == arity));
                break;
            }
        }

        return candidates;
    }

    private static IEnumerable<INamespaceSymbol> EnumerateNamespaces(INamespaceSymbol root)
    {
        yield return root;
        foreach (var child in root.GetNamespaceMembers())
        {
            foreach (var nested in EnumerateNamespaces(child))
            {
                yield return nested;
            }
        }
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
