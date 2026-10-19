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
/// closed JSON roots its generated contribution requires. The bundle is ordinary Compile input, so the
/// roots are rebuilt from real symbols and cross-checked against the precompile outputs manifest; a
/// manifest that no longer agrees with the current declaration fails the build instead of producing a
/// half-closed context. The same shared planner the emitter uses is the only root authority.
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
        if (builds.Models.Count == 0 || builds.Diagnostics.Any(diagnostic =>
                diagnostic.Severity == AppServiceHttpDiagnosticSeverity.Error))
        {
            return roots;
        }

        var duplicateContracts = builds.Models
            .GroupBy(model => model.ContractFullName, StringComparer.Ordinal)
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

        if (!ReconcileWithPrecompileManifest(builds.Models, outputsManifestJson, assemblyName))
        {
            return roots;
        }

        var assemblyOptions = AppServiceHttpAssemblyOptions.ForAssembly(assemblyName);
        var optionsByModel = builds.Models.ToDictionary(
            model => model.ContractFullName,
            model => AppServiceHttpEmissionOptions.ForContract(model.ContractFullName) with
            {
                JsonContextFullName = assemblyOptions.JsonContextFullName,
            },
            StringComparer.Ordinal);

        foreach (var model in builds.Models)
        {
            foreach (var planned in AppServiceHttpProjectionEmitter.PlanJsonRoots(model, optionsByModel[model.ContractFullName]))
            {
                var display = planned.ClrTypeDisplay ?? planned.TypeDisplay;
                var type = ResolveTypeDisplay(compilation, display, out var failure);
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

    private sealed class BuildResult
    {
        public List<AppServiceHttpProjectionModel> Models { get; } = [];
        public List<AppServiceHttpProjectionDiagnostic> Diagnostics { get; } = [];
    }

    private BuildResult BuildModels(CSharpCompilation compilation)
    {
        var result = new BuildResult();
        const string projectionAttributeName =
            "CrestCreates.Domain.Shared.Attributes.CapabilityCompatibilityProjectionAttribute";

        foreach (var type in compilation.GetSymbolsWithName(_ => true, SymbolFilter.Type).OfType<INamedTypeSymbol>())
        {
            var projection = type.GetAttributes().FirstOrDefault(attribute =>
                attribute.AttributeClass?.ToDisplayString() == projectionAttributeName);
            if (projection is null || !HasContractType(projection))
            {
                continue;
            }

            var build = AppServiceHttpProjectionModelBuilder.Build(compilation, type);
            result.Diagnostics.AddRange(build.Diagnostics);
            if (build.Model is not null)
            {
                result.Models.Add(build.Model);
            }
        }

        result.Models.Sort((left, right) => string.CompareOrdinal(left.ContractFullName, right.ContractFullName));
        foreach (var diagnostic in result.Diagnostics)
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

        return result;
    }

    private static bool HasContractType(AttributeData projection)
        => projection.NamedArguments.Any(argument =>
            argument.Key == "ContractType" && argument.Value.Kind == TypedConstantKind.Type && argument.Value.Value is not null);

    /// <summary>
    /// The JSON roots must agree with the precompile's own artifacts: an owner, hash, declared type or
    /// required-root list that the producer did not emit means the context would close a declaration
    /// that is not the one actually compiled.
    /// </summary>
    private bool ReconcileWithPrecompileManifest(
        IReadOnlyList<AppServiceHttpProjectionModel> models,
        string? outputsManifestJson,
        string assemblyName)
    {
        if (string.IsNullOrWhiteSpace(outputsManifestJson))
        {
            ManifestError(
                $"'{models[0].ContractFullName}' declares an explicit-host HTTP projection, but no precompile "
                + "outputs manifest was provided; the projection toolchain is not assembled.");
            return false;
        }

        var expected = BuildExpectedArtifacts(assemblyName, models);
        var expectedByHint = expected.ToDictionary(artifact => artifact.HintName, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

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

    private sealed record ExpectedArtifact(
        string HintName,
        string? OwnerServiceFullName,
        string ContentHash,
        IReadOnlyList<string> DeclaredTypeFullNames,
        IReadOnlyList<string>? RequiredRoots);

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
            ContentHash(activation.Source),
            HttpProjectionShared.DeclaredTypeFullNames(activation.Source)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray(),
            null));
        return artifacts;
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

    private static readonly (string Keyword, SpecialType Type)[] KeywordTypes =
    [
        ("bool", SpecialType.System_Boolean),
        ("byte", SpecialType.System_Byte),
        ("sbyte", SpecialType.System_SByte),
        ("char", SpecialType.System_Char),
        ("decimal", SpecialType.System_Decimal),
        ("double", SpecialType.System_Double),
        ("float", SpecialType.System_Single),
        ("int", SpecialType.System_Int32),
        ("uint", SpecialType.System_UInt32),
        ("long", SpecialType.System_Int64),
        ("ulong", SpecialType.System_UInt64),
        ("short", SpecialType.System_Int16),
        ("ushort", SpecialType.System_UInt16),
        ("object", SpecialType.System_Object),
        ("string", SpecialType.System_String),
    ];

    /// <summary>
    /// Resolves a model type display (the shared <c>FullyQualifiedFormat</c> plus nullable annotations)
    /// to the one symbol in source or references. Zero or multiple owners are reported explicitly; a
    /// name the JSON side cannot resolve is never silently skipped.
    /// </summary>
    private static ITypeSymbol? ResolveTypeDisplay(
        CSharpCompilation compilation,
        string display,
        out string failure)
    {
        failure = string.Empty;
        var text = display.Trim();

        var nullableReference = false;
        if (text.EndsWith("?", StringComparison.Ordinal))
        {
            nullableReference = true;
            text = text.Substring(0, text.Length - 1);
        }

        if (text.EndsWith("[]", StringComparison.Ordinal))
        {
            var element = ResolveTypeDisplay(compilation, text.Substring(0, text.Length - 2), out failure);
            return element is null ? null : compilation.CreateArrayTypeSymbol(element);
        }

        var open = FindTopLevelOpenAngle(text);
        if (open < 0)
        {
            var resolved = ResolveNamed(compilation, text, out failure);
            if (resolved is not null && nullableReference && resolved.IsValueType)
            {
                var nullable = compilation.GetSpecialType(SpecialType.System_Nullable_T);
                return nullable.Construct(resolved);
            }

            return resolved;
        }

        if (!text.EndsWith(">", StringComparison.Ordinal))
        {
            failure = "the type display is not a closed generic type";
            return null;
        }

        var definitionDisplay = text.Substring(0, open);
        var argumentText = text.Substring(open + 1, text.Length - open - 2);
        var argumentDisplays = SplitTopLevelArguments(argumentText);
        var arguments = new List<ITypeSymbol>();
        foreach (var argumentDisplay in argumentDisplays)
        {
            var argument = ResolveTypeDisplay(compilation, argumentDisplay, out failure);
            if (argument is null)
            {
                return null;
            }

            arguments.Add(argument);
        }

        var definition = ResolveNamed(compilation, definitionDisplay, arguments.Count, out failure);
        if (definition is null)
        {
            return null;
        }

        if (definition.Arity != arguments.Count)
        {
            failure = $"generic arity {arguments.Count} does not match '{definitionDisplay}'";
            return null;
        }

        return definition.Construct(arguments.ToArray());
    }

    private static int FindTopLevelOpenAngle(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '<')
            {
                return index;
            }
        }

        return -1;
    }

    private static IReadOnlyList<string> SplitTopLevelArguments(string text)
    {
        var arguments = new List<string>();
        var depth = 0;
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            switch (text[index])
            {
                case '<':
                case '[':
                    depth++;
                    break;
                case '>':
                case ']':
                    depth--;
                    break;
                case ',' when depth == 0:
                    arguments.Add(text.Substring(start, index - start).Trim());
                    start = index + 1;
                    break;
            }
        }

        arguments.Add(text.Substring(start).Trim());
        return arguments;
    }

    private static INamedTypeSymbol? ResolveNamed(
        CSharpCompilation compilation,
        string display,
        out string failure)
    {
        var arity = 0;
        var name = display;
        var backtick = name.LastIndexOf('`');
        if (backtick >= 0)
        {
            name = name.Substring(0, backtick);
            int.TryParse(display.Substring(backtick + 1), out arity);
        }

        return ResolveNamed(compilation, name, arity, out failure);
    }

    private static INamedTypeSymbol? ResolveNamed(
        CSharpCompilation compilation,
        string display,
        int arity,
        out string failure)
    {
        failure = string.Empty;
        var name = display.StartsWith("global::", StringComparison.Ordinal)
            ? display.Substring("global::".Length)
            : display;

        foreach (var (keyword, specialType) in KeywordTypes)
        {
            if (name == keyword)
            {
                return compilation.GetSpecialType(specialType);
            }
        }

        var candidates = ResolveCandidates(compilation, name, arity);
        if (candidates.Count == 0)
        {
            // Framework reference assemblies route many types through type forwarders, which a plain
            // namespace walk cannot see. GetTypeByMetadataName follows forwarders; it is only used once
            // the explicit owner walk found nothing, so duplicate owners are still diagnosed below.
            var forwarded = ResolveThroughMetadataName(compilation, name, arity);
            if (forwarded is not null)
            {
                return forwarded;
            }

            failure = "no type with that metadata name exists in the compilation or its references";
            return null;
        }

        if (candidates.Count > 1)
        {
            failure = $"'{name}' is declared by {candidates.Count} inputs: "
                + string.Join(", ", candidates.Select(candidate => candidate.ContainingAssembly?.Name))
                + "; one JSON root type must have exactly one owner";
            return null;
        }

        return candidates[0];
    }

    private static INamedTypeSymbol? ResolveThroughMetadataName(
        CSharpCompilation compilation,
        string name,
        int arity)
    {
        var suffix = arity > 0 ? "`" + arity : string.Empty;
        var segments = name.Split('.');
        for (var nested = 0; nested < segments.Length; nested++)
        {
            var cut = segments.Length - nested;
            var metadataName = string.Join(".", segments.Take(cut))
                + (nested > 0 ? "+" + string.Join("+", segments.Skip(cut)) : string.Empty)
                + suffix;
            var resolved = compilation.GetTypeByMetadataName(metadataName);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        return null;
    }

    private static IReadOnlyList<INamedTypeSymbol> ResolveCandidates(
        CSharpCompilation compilation,
        string fullName,
        int arity)
    {
        var candidates = new List<INamedTypeSymbol>();
        var segments = fullName.Split('.');

        // Walk every split between namespace and nested type names: a display of a nested type uses
        // '.' while the metadata name uses '+'.
        for (var namespaceSegmentCount = segments.Length - 1; namespaceSegmentCount >= 0; namespaceSegmentCount--)
        {
            var namespaceName = string.Join(".", segments.Take(namespaceSegmentCount));
            var typeSegments = segments.Skip(namespaceSegmentCount).ToArray();
            if (typeSegments.Length == 0)
            {
                continue;
            }

            var metadataName = typeSegments[0] + (arity > 0 ? "`" + arity : string.Empty);
            foreach (var type in FindTypes(compilation, namespaceName, metadataName, arity))
            {
                var resolved = type;
                var valid = true;
                for (var index = 1; index < typeSegments.Length; index++)
                {
                    var nested = resolved.GetTypeMembers(typeSegments[index]).FirstOrDefault();
                    if (nested is null)
                    {
                        valid = false;
                        break;
                    }

                    resolved = nested;
                }

                if (valid && !candidates.Any(existing => SymbolEqualityComparer.Default.Equals(existing, resolved)))
                {
                    candidates.Add(resolved);
                }
            }
        }

        return candidates;
    }

    private static IEnumerable<INamedTypeSymbol> FindTypes(
        CSharpCompilation compilation,
        string namespaceName,
        string metadataName,
        int arity)
    {
        foreach (var namespaceSymbol in EnumerateNamespaces(compilation.Assembly.GlobalNamespace))
        {
            if (namespaceSymbol.ToDisplayString() != namespaceName)
            {
                continue;
            }

            foreach (var type in namespaceSymbol.GetTypeMembers(metadataName))
            {
                if (type.Arity == arity)
                {
                    yield return type;
                }
            }
        }

        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
            {
                continue;
            }

            foreach (var namespaceSymbol in EnumerateNamespaces(assembly.GlobalNamespace))
            {
                if (namespaceSymbol.ToDisplayString() != namespaceName)
                {
                    continue;
                }

                foreach (var type in namespaceSymbol.GetTypeMembers(metadataName))
                {
                    if (type.Arity == arity)
                    {
                        yield return type;
                    }
                }
            }
        }
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
}
