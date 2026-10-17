#nullable enable
using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.CodeAnalysis;

namespace CrestCreates.CodeGenerator.CrudServiceGenerator;

/// <summary>
/// #128 S2 item ①: the one ownership reader. §6.1 states that the CRUD precompile manifest owns the
/// concrete artifacts it materialized and that <c>EntitySourceGenerator</c> /
/// <c>ServiceRegistrationGenerator</c> must therefore skip exactly those products instead of emitting a
/// second file with the same name. The build stage hands the CRUD outputs manifest to the compiler as an
/// <c>AdditionalFile</c>, so every generator asks this single reader for the fully-qualified type names
/// the manifest already owns — no per-generator naming rule, no assembly loading, no runtime reflection.
/// The result is an immutable, deterministically ordered <see cref="ImmutableSortedSet{T}"/> keyed by
/// <see cref="StringComparer.Ordinal"/>, and every "nothing is owned" outcome returns the one shared
/// instance so the incremental pipeline never observes a spurious change. An absent or unparsable
/// manifest yields that empty set, which keeps a generator on its pre-#128 behavior, and no exception
/// can escape into the driver.
/// </summary>
public static class CrudArtifactOwnership
{
    /// <summary>Name of the outputs manifest the CRUD targets contribute as an AdditionalFile.</summary>
    public const string ManifestFileName = "CrestCreates.Crud.outputs.json";

    /// <summary>Manifest document shape written by <c>GenerateCrestCrudArtifacts</c>.</summary>
    private const string EntriesProperty = "Entries";

    private const string DeclaredTypesProperty = "DeclaredTypeFullNames";

    /// <summary>The single stable "nothing is owned" value, so an absent manifest never allocates a new set.</summary>
    public static ImmutableSortedSet<string> None { get; } =
        ImmutableSortedSet.Create<string>(StringComparer.Ordinal);

    /// <summary>
    /// Reads the owned type names (plain <c>Namespace.Type</c> fully-qualified names; the owner identity
    /// is a sibling field of each entry, never part of the type name) out of the CRUD outputs manifest.
    /// The result is immutable and deterministically ordered, and every "nothing is owned" path — no
    /// additional files, no CRUD manifest, an empty entry list, an unreadable manifest — returns the one
    /// shared <see cref="None"/> instance so Roslyn's incremental pipeline never sees a value change.
    /// </summary>
    public static ImmutableSortedSet<string> Read(ImmutableArray<AdditionalText> additionalTexts)
    {
        if (additionalTexts.IsDefaultOrEmpty)
        {
            return None;
        }

        ImmutableSortedSet<string>.Builder? owned = null;
        foreach (var additionalText in additionalTexts)
        {
            if (additionalText is null
                || string.IsNullOrEmpty(additionalText.Path)
                || !string.Equals(Path.GetFileName(additionalText.Path), ManifestFileName, StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryReadDeclaredTypes(additionalText, out var declaredTypes))
            {
                return None;
            }

            owned ??= None.ToBuilder();
            foreach (var declaredType in declaredTypes)
            {
                owned.Add(declaredType);
            }
        }

        return owned is null || owned.Count == 0 ? None : owned.ToImmutable();
    }

    /// <summary>Ownership content, rather than a newly allocated set, controls incremental invalidation.</summary>
    public static IEqualityComparer<ImmutableSortedSet<string>> ContentComparer { get; } = new SetComparer();

    private sealed class SetComparer : IEqualityComparer<ImmutableSortedSet<string>>
    {
        public bool Equals(ImmutableSortedSet<string>? x, ImmutableSortedSet<string>? y) =>
            ReferenceEquals(x, y) || (x is not null && y is not null && x.SetEquals(y));
        public int GetHashCode(ImmutableSortedSet<string> value)
        {
            var hash = 17;
            foreach (var name in value) hash = unchecked(hash * 31 + StringComparer.Ordinal.GetHashCode(name));
            return hash;
        }
    }

    private static bool TryReadDeclaredTypes(AdditionalText additionalText, out ImmutableArray<string> types)
    {
        types = ImmutableArray<string>.Empty;
        try
        {
            var text = additionalText.GetText(default);
            if (text is null)
            {
                return false;
            }

            using var document = JsonDocument.Parse(text.ToString());
            if (!document.RootElement.TryGetProperty("SchemaVersion", out var schema)
                || schema.GetString() != "crud-artifacts-v1"
                || !document.RootElement.TryGetProperty("ModelSchemaVersion", out var model)
                || model.GetString() != CrestCreates.Tooling.CrudGeneration.CrudGenerationModel.ModelSchemaVersion
                || !document.RootElement.TryGetProperty(EntriesProperty, out var entries)
                || entries.ValueKind != JsonValueKind.Array)
            {
                // No entries section is "nothing owned" (a project without CRUD declarations), never a
                // reason to guess an ownership set.
                return false;
            }

            var builder = ImmutableArray.CreateBuilder<string>();
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object
                    || !entry.TryGetProperty(DeclaredTypesProperty, out var declared)
                    || declared.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var type in declared.EnumerateArray())
                {
                    if (type.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var fullName = type.GetString();
                    if (!string.IsNullOrEmpty(fullName))
                    {
                        builder.Add(fullName!);
                    }
                }
            }

            types = builder.ToImmutable();
            return true;
        }
        catch (Exception)
        {
            // A generator must never throw: an unreadable manifest is treated as "not owned" and the
            // existing build-time CCCRUD diagnostics remain the place where a broken manifest is reported.
            return false;
        }
    }
}
