using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CrestCreates.JsonContracts.BuildTasks.Diagnostics;
using CrestCreates.JsonContracts.BuildTasks.HttpProjection;
using CrestCreates.JsonContracts.BuildTasks.Model;
using CrestCreates.Tooling.HttpProjection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CrestCreates.JsonContracts.BuildTasks.Semantic;

/// <summary>
/// #129 S2.2-B: discovers the materialized hand-written HTTP projection surface and turns it into the
/// closed JSON roots its generated contribution requires. Roots are bound from the real symbols the
/// projection model was built from (never reconstructed from display text), and the actual Compile
/// inputs are checked against the producer's artifacts and the precompile outputs manifest. A surface
/// that does not match what is compiled fails the build instead of producing a half-closed context.
/// </summary>
public sealed class AppServiceHttpJsonSurfaceDiscovery
{
    private readonly List<JsonContractDiagnostic> _diagnostics = [];

    public IReadOnlyList<JsonContractDiagnostic> Diagnostics => _diagnostics;

    public List<JsonContractRootModel> Discover(
        CSharpCompilation compilation,
        string assemblyName,
        string? outputsManifestJson)
    {
        var roots = new List<JsonContractRootModel>();
        var builds = BuildModels(compilation);
        if (builds.Count == 0 || builds.Any(build => build.Model is null)
            || _diagnostics.Any(diagnostic => diagnostic.Severity == JsonContractDiagnosticSeverity.Error))
        {
            return roots;
        }

        var duplicateContracts = builds
            .GroupBy(build => build.Model!.ContractFullName, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (duplicateContracts.Count > 0)
        {
            foreach (var contract in duplicateContracts)
            {
                _diagnostics.Add(new JsonContractDiagnostic
                {
                    Id = AppServiceHttpDiagnosticCodes.OwnerRouteActionConflict,
                    Severity = JsonContractDiagnosticSeverity.Error,
                    Message = $"contract '{contract}' has more than one explicit-host projection anchor in "
                        + $"assembly '{assemblyName}'; one contract must have exactly one HTTP owner.",
                });
            }

            return roots;
        }

        if (!ReconcileWithPrecompileManifest(compilation, builds, outputsManifestJson, assemblyName))
        {
            return roots;
        }

        var responseOpen = ResolveReferenceType(
            compilation, "CrestCreates.DynamicApi.DynamicApiResponse`1", out var responseFailure);
        var responseVoid = ResolveReferenceType(
            compilation, "CrestCreates.DynamicApi.DynamicApiResponse", out var voidFailure);
        if (responseOpen is null || responseVoid is null)
        {
            _diagnostics.Add(new JsonContractDiagnostic
            {
                Id = AppServiceHttpDiagnosticCodes.JsonClosureIncomplete,
                Severity = JsonContractDiagnosticSeverity.Error,
                Message = "The HTTP projection result envelopes cannot be resolved from the referenced "
                    + $"assemblies: {responseFailure} {voidFailure}".Trim(),
            });
            return roots;
        }

        foreach (var build in builds)
        {
            var model = build.Model!;
            var options = AppServiceHttpEmissionOptions.ForContract(model.ContractFullName) with
            {
                JsonContextFullName = AppServiceHttpAssemblyOptions.ForAssembly(assemblyName).JsonContextFullName,
            };
            var actionsByName = model.Actions.ToDictionary(action => action.Name, StringComparer.Ordinal);
            var symbols = build.Symbols
                .GroupBy(symbol => Key(symbol.Action, symbol.Component, symbol.Display), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Type, StringComparer.Ordinal);

            foreach (var planned in AppServiceHttpProjectionEmitter.PlanJsonRoots(model, options))
            {
                var display = planned.ClrTypeDisplay ?? planned.TypeDisplay;
                ITypeSymbol? type;
                string failure;
                switch (planned.Component)
                {
                    case "VoidEnvelope":
                        type = responseVoid;
                        failure = string.Empty;
                        break;
                    case "ResultEnvelope":
                        var payload = ResolveSymbol(symbols, planned.Action, "Payload", display: null, out failure);
                        if (payload is null)
                        {
                            type = null;
                            break;
                        }

                        type = responseOpen.Construct(payload);
                        break;
                    case "Payload":
                        type = ResolveSymbol(symbols, planned.Action, "Payload", display, out failure);
                        break;
                    case "Body":
                        type = ResolveSymbol(symbols, planned.Action, "Body", display, out failure);
                        break;
                    case "CapabilityInput":
                        type = ResolveCapabilityInput(
                            compilation, model, actionsByName, symbols, planned, display, out failure);
                        break;
                    default:
                        type = null;
                        failure = $"unknown root component '{planned.Component}'";
                        break;
                }

                if (type is null)
                {
                    _diagnostics.Add(new JsonContractDiagnostic
                    {
                        Id = AppServiceHttpDiagnosticCodes.JsonClosureIncomplete,
                        Severity = JsonContractDiagnosticSeverity.Error,
                        Message = $"HTTP projection '{model.ContractFullName}' requires JSON root '{display}' "
                            + $"for action '{planned.Action}' ({planned.Component}), but {failure}",
                    });
                    continue;
                }

                if (roots.Any(root => SymbolEqualityComparer.Default.Equals(root.RootType, type)))
                {
                    continue;
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
                                SourceKind = JsonContractRootSourceKind.AppServiceHttpSurface,
                                DeclaringSurface = model.ContractFullName,
                                DeclarationName = model.HttpOwnerAssemblyName,
                                RoleName = planned.Component + ":" + planned.Action,
                            },
                        ],
                    },
                });
            }
        }

        return roots;
    }

    private sealed record ModelBuild(
        AppServiceHttpProjectionModel? Model,
        IReadOnlyList<AppServiceHttpRootSymbol> Symbols);

    private List<ModelBuild> BuildModels(CSharpCompilation compilation)
    {
        var builds = new List<ModelBuild>();
        const string projectionAttributeName =
            "CrestCreates.Domain.Shared.Attributes.CapabilityCompatibilityProjectionAttribute";

        foreach (var type in compilation.GetSymbolsWithName(_ => true, SymbolFilter.Type).OfType<INamedTypeSymbol>())
        {
            var projection = type.GetAttributes().FirstOrDefault(attribute =>
                attribute.AttributeClass?.ToDisplayString() == projectionAttributeName);
            if (projection is null || !AppServiceHttpProjectionModelBuilder.OwnsFormalHttpSurface(projection))
            {
                continue;
            }

            var build = AppServiceHttpProjectionModelBuilder.Build(compilation, type);
            builds.Add(new ModelBuild(build.Model, build.RootSymbols));
            foreach (var diagnostic in build.Diagnostics)
            {
                _diagnostics.Add(new JsonContractDiagnostic
                {
                    Id = diagnostic.Id,
                    Severity = diagnostic.Severity == AppServiceHttpDiagnosticSeverity.Error
                        ? JsonContractDiagnosticSeverity.Error
                        : JsonContractDiagnosticSeverity.Warning,
                    Message = diagnostic.Message,
                });
            }
        }

        builds.Sort((left, right) => string.CompareOrdinal(
            left.Model?.ContractFullName ?? string.Empty,
            right.Model?.ContractFullName ?? string.Empty));
        return builds;
    }

    private static string Key(string action, string component, string display)
        => action + "\u001f" + component + "\u001f" + display;

    private static ITypeSymbol? ResolveSymbol(
        IReadOnlyDictionary<string, ITypeSymbol> symbols,
        string action,
        string component,
        string? display,
        out string failure)
    {
        failure = string.Empty;
        if (display is not null)
        {
            if (symbols.TryGetValue(Key(action, component, display), out var exact))
            {
                return exact;
            }

            var byComponent = DistinctTypes(symbols
                .Where(pair => pair.Key.StartsWith(action + "\u001f" + component + "\u001f", StringComparison.Ordinal))
                .Select(pair => pair.Value));
            if (byComponent.Count == 1)
            {
                return byComponent[0];
            }

            failure = $"no {component} symbol with display '{display}' belongs to the compiled contract";
            return null;
        }

        var candidates = DistinctTypes(symbols
            .Where(pair => pair.Key.StartsWith(action + "\u001f" + component + "\u001f", StringComparison.Ordinal))
            .Select(pair => pair.Value));
        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        failure = candidates.Count == 0
            ? $"no {component} symbol belongs to action '{action}'"
            : $"action '{action}' has {candidates.Count} distinct {component} symbols; the root is ambiguous";
        return null;
    }

    private static IReadOnlyList<ITypeSymbol> DistinctTypes(IEnumerable<ITypeSymbol> types)
    {
        var result = new List<ITypeSymbol>();
        foreach (var type in types)
        {
            if (!result.Any(existing => SymbolEqualityComparer.Default.Equals(existing, type)))
            {
                result.Add(type);
            }
        }

        return result;
    }

    private ITypeSymbol? ResolveCapabilityInput(
        CSharpCompilation compilation,
        AppServiceHttpProjectionModel model,
        IReadOnlyDictionary<string, AppServiceHttpActionModel> actionsByName,
        IReadOnlyDictionary<string, ITypeSymbol> symbols,
        HttpWireJsonRoot planned,
        string display,
        out string failure)
    {
        failure = string.Empty;
        if (actionsByName.TryGetValue(planned.Action, out var action)
            && action.InputEnvelopeFullName is not null
            && display == "global::" + action.InputEnvelopeFullName)
        {
            var envelope = compilation.GetTypeByMetadataName(action.InputEnvelopeFullName);
            if (envelope is null)
            {
                failure = $"the composed input envelope '{action.InputEnvelopeFullName}' is not declared "
                    + "by the compiled materialized surface";
                return null;
            }

            if (!SymbolEqualityComparer.Default.Equals(envelope.ContainingAssembly, compilation.Assembly))
            {
                failure = $"the composed input envelope '{action.InputEnvelopeFullName}' is not owned by "
                    + $"assembly '{compilation.AssemblyName}'";
                return null;
            }

            return envelope;
        }

        return ResolveSymbol(symbols, planned.Action, "CapabilityInput", display, out failure);
    }

    private static INamedTypeSymbol? ResolveReferenceType(
        CSharpCompilation compilation,
        string metadataName,
        out string failure)
    {
        failure = string.Empty;
        var type = compilation.GetTypeByMetadataName(metadataName);
        if (type is null)
        {
            failure = $"framework type '{metadataName}' does not resolve uniquely from the references";
            return null;
        }

        if (SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly))
        {
            failure = $"framework type '{metadataName}' must come from a referenced assembly, not the owner";
            return null;
        }

        return type;
    }

    private sealed record ExpectedArtifact(
        string HintName,
        string? OwnerServiceFullName,
        string Source,
        string ContentHash,
        IReadOnlyList<string> DeclaredTypeFullNames,
        IReadOnlyList<string>? RequiredRoots);

    /// <summary>
    /// The JSON roots must agree with the producer's artifacts and the compiled inputs: an owner,
    /// hash, declared type, required root or actual artifact source the producer did not emit means
    /// the context would close a declaration that is not the one actually compiled.
    /// </summary>
    private bool ReconcileWithPrecompileManifest(
        CSharpCompilation compilation,
        IReadOnlyList<ModelBuild> builds,
        string? outputsManifestJson,
        string assemblyName)
    {
        if (string.IsNullOrWhiteSpace(outputsManifestJson))
        {
            ManifestError(
                $"'{builds[0].Model!.ContractFullName}' declares an explicit-host HTTP projection, but no "
                + "precompile outputs manifest was provided; the projection toolchain is not assembled.");
            return false;
        }

        var models = builds.Select(build => build.Model!).ToList();
        var expected = BuildExpectedArtifacts(assemblyName, models);
        var expectedByHint = expected.ToDictionary(artifact => artifact.HintName, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // S22B-R1-02/R2-01: verify the actual compiled code, not dead text. The expected artifact must
        // appear as a contiguous active token sequence in a syntax tree; tokens inside comments,
        // disabled preprocessor branches or string literals can never satisfy this.
        var activeTokens = compilation.SyntaxTrees
            .Select(tree => tree.GetRoot().DescendantTokens()
                .Where(token => !token.IsKind(SyntaxKind.EndOfFileToken))
                .Select(token => token.Text)
                .ToList())
            .ToList();
        foreach (var artifact in expected)
        {
            var artifactTokens = CSharpSyntaxTree.ParseText(
                    artifact.Source,
                    CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest))
                .GetRoot()
                .DescendantTokens()
                .Where(token => !token.IsKind(SyntaxKind.EndOfFileToken))
                .Select(token => token.Text)
                .ToList();
            if (!activeTokens.Any(tokens => ContainsContiguous(tokens, artifactTokens)))
            {
                ManifestError(
                    $"The active compiled inputs do not contain AppService HTTP artifact '{artifact.HintName}' "
                    + "with the producer's content; the materialized surface was modified, disabled or "
                    + "retained only as inactive text.");
            }
        }

        try
        {
            using var document = JsonDocument.Parse(outputsManifestJson);
            var root = document.RootElement;
            if (GetString(root, "SchemaVersion") != AppServiceHttpPrecompileEngine.ArtifactSchemaVersion
                || GetString(root, "ModelSchemaVersion") != AppServiceHttpProjectionModel.ModelSchemaVersion)
            {
                ManifestError("Unsupported AppService HTTP outputs manifest schema.");
                return false;
            }

            if (!root.TryGetProperty("Entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            {
                ManifestError("The AppService HTTP outputs manifest carries no entries.");
                return false;
            }

            foreach (var entry in entries.EnumerateArray())
            {
                var hint = GetString(entry, "ArtifactHintName");
                if (!seen.Add(hint) || !expectedByHint.TryGetValue(hint, out var artifact))
                {
                    ManifestError($"Unknown or duplicate AppService HTTP artifact '{hint}'.");
                    continue;
                }

                if (GetString(entry, "OwnerAssemblyName") != assemblyName
                    || GetString(entry, "ModelSchemaVersion") != AppServiceHttpProjectionModel.ModelSchemaVersion
                    || GetString(entry, "ContentHash") != artifact.ContentHash)
                {
                    ManifestError($"AppService HTTP artifact '{hint}' owner, schema or content hash does not "
                        + "match the current declaration.");
                    continue;
                }

                var declared = ReadStringArray(entry, "DeclaredTypeFullNames");
                if (!declared.SequenceEqual(artifact.DeclaredTypeFullNames.OrderBy(name => name, StringComparer.Ordinal), StringComparer.Ordinal))
                {
                    ManifestError($"AppService HTTP artifact '{hint}' declared types do not match the producer.");
                    continue;
                }

                if (artifact.OwnerServiceFullName is not null
                    && GetString(entry, "OwnerServiceFullName") != artifact.OwnerServiceFullName)
                {
                    ManifestError($"AppService HTTP artifact '{hint}' owner service does not match the contract.");
                    continue;
                }

                if (artifact.RequiredRoots is not null)
                {
                    var required = ReadStringArray(entry, "RequiredJsonRootTypeNames");
                    if (!required.SequenceEqual(artifact.RequiredRoots, StringComparer.Ordinal))
                    {
                        ManifestError($"AppService HTTP artifact '{hint}' required JSON roots do not match the "
                            + "shared root plan.");
                    }
                }
            }

            if (seen.Count != expectedByHint.Count || expectedByHint.Keys.Any(hint => !seen.Contains(hint)))
            {
                ManifestError("The AppService HTTP outputs manifest does not cover the complete current artifact set.");
                return false;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            ManifestError("Invalid AppService HTTP outputs manifest: " + ex.GetType().Name);
            return false;
        }

        return _diagnostics.All(diagnostic => diagnostic.Severity != JsonContractDiagnosticSeverity.Error);
    }

    private static List<ExpectedArtifact> BuildExpectedArtifacts(
        string assemblyName,
        IReadOnlyList<AppServiceHttpProjectionModel> models)
    {
        var assemblyOptions = AppServiceHttpAssemblyOptions.ForAssembly(assemblyName);
        var optionsByModel = models.ToDictionary(
            model => model.ContractFullName,
            model => AppServiceHttpEmissionOptions.ForContract(model.ContractFullName) with
            {
                JsonContextFullName = assemblyOptions.JsonContextFullName,
            },
            StringComparer.Ordinal);

        var artifacts = new List<ExpectedArtifact>();
        foreach (var model in models)
        {
            var options = optionsByModel[model.ContractFullName];
            var bundle = AppServiceHttpProjectionEmitter.Emit(model, options).Single();
            artifacts.Add(new ExpectedArtifact(
                bundle.FileName,
                model.ContractFullName,
                bundle.Source,
                ContentHash(bundle.Source),
                HttpProjectionShared.DeclaredTypeFullNames(bundle.Source)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray(),
                AppServiceHttpProjectionEmitter.PlanJsonRoots(model, options)
                    .Select(root => root.ClrTypeDisplay ?? root.TypeDisplay)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray()));
        }

        var activation = AppServiceHttpProjectionEmitter.EmitAssemblyActivation(
            assemblyName,
            models.Select(model => (model, optionsByModel[model.ContractFullName])).ToArray(),
            assemblyOptions);
        artifacts.Add(new ExpectedArtifact(
            activation.FileName,
            null,
            activation.Source,
            ContentHash(activation.Source),
            HttpProjectionShared.DeclaredTypeFullNames(activation.Source)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray(),
            null));
        return artifacts;
    }

    private static bool ContainsContiguous(IReadOnlyList<string> haystack, IReadOnlyList<string> needle)
    {
        if (needle.Count == 0)
        {
            return true;
        }

        for (var start = 0; start + needle.Count <= haystack.Count; start++)
        {
            var matches = true;
            for (var offset = 0; offset < needle.Count; offset++)
            {
                if (!string.Equals(haystack[start + offset], needle[offset], StringComparison.Ordinal))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    private static string ContentHash(string source)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();

    private void ManifestError(string message) => _diagnostics.Add(new JsonContractDiagnostic
    {
        Id = AppServiceHttpDiagnosticCodes.TransportManifestState,
        Severity = JsonContractDiagnosticSeverity.Error,
        Message = message,
    });

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? string.Empty)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray()
            : Array.Empty<string>();
}
