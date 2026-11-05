using System.Collections.ObjectModel;
using System.Text.Json;

namespace CrestCreates.Agent.Authoring.Parsing;

/// <summary>
/// Stage A of <see cref="JsonDescriptorAuthoringOutputParser"/>: the raw-envelope facts a projected
/// DTO cannot keep. Member presence, value kinds, repeated names and casing aliases are decided here,
/// before any value is read, because deserialization merges absent with <c>null</c> and, with the
/// default options, lets the last duplicate value win.
/// </summary>
internal static class DescriptorAuthoringEnvelopeFacts
{
    internal const string RootPath = "envelope";

    private const string RootLevel = "root";

    private static readonly ReadOnlyDictionary<string, IReadOnlyList<string>> DeclaredMembers =
        new(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [RootLevel] = ["contractVersion", "promptInputHash", "outcome", "plan", "items", "unsupported", "needsClarification"],
            ["plan"] = ["planId", "intentText", "assumptions", "plannedDescriptorRefs"],
            ["plannedDescriptorRefs"] = ["namespace", "id", "version"],
            ["items"] =
            [
                "descriptorKind", "descriptorId", "operation", "baseVersion", "rationale",
                "payload", "evidenceRefs", "memoryRefs", "assumptions"
            ],
            ["unsupported"] = ["reasons"],
            ["reasons"] = ["code", "message", "evidenceRefs"],
            ["needsClarification"] = ["questions"],
            ["questions"] = ["id", "reasonCode", "question", "missingInputs", "candidateValues"]
        });

    /// <summary>Members whose object or array-element value carries another protocol envelope level.</summary>
    private static readonly ReadOnlyDictionary<string, IReadOnlyList<string>> EnvelopeChildMembers =
        new(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [RootLevel] = ["plan", "items", "unsupported", "needsClarification"],
            ["plan"] = ["plannedDescriptorRefs"],
            ["unsupported"] = ["reasons"],
            ["needsClarification"] = ["questions"]
        });

    /// <summary>
    /// Reads the raw envelope facts. Returns <c>false</c> with the diagnostic message when the root is
    /// not an object, a member name repeats at one level (recursing through <c>payload</c>), a member
    /// is outside the declared vocabulary for its level, or a member name cannot be decoded as text.
    /// </summary>
    internal static bool TryRead(
        JsonElement root,
        out EnvelopeMemberInventory inventory,
        out string? failureMessage)
    {
        var collected = new EnvelopeFactsAccumulator();

        if (root.ValueKind != JsonValueKind.Object)
        {
            inventory = new EnvelopeMemberInventory(collected.Objects, collected.ArrayLengths);
            failureMessage = "Failed to deserialize provider output as JSON.";
            return false;
        }

        if (!TryCollectObject(root, RootLevel, RootPath, collected, out failureMessage))
        {
            inventory = new EnvelopeMemberInventory(collected.Objects, collected.ArrayLengths);
            return false;
        }

        inventory = new EnvelopeMemberInventory(collected.Objects, collected.ArrayLengths);
        failureMessage = null;
        return true;
    }

    /// <summary>
    /// Decodes a raw member name, or returns <c>null</c> when it cannot be decoded.
    /// <see cref="JsonDocument"/> accepts an unpaired surrogate escape and defers the failure to the moment
    /// the text is decoded, where it throws <see cref="InvalidOperationException"/> rather than a
    /// <see cref="JsonException"/> — so no name read in this stage may be left unguarded.
    /// </summary>
    internal static string? DecodeMemberName(JsonProperty property)
    {
        try
        {
            return property.Name;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Decodes a raw string value under the same boundary as <see cref="DecodeMemberName"/>.</summary>
    internal static string? DecodeStringValue(JsonElement value)
    {
        try
        {
            return value.GetString();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    internal static string UndecodableMemberNameMessage(string path)
        => $"Envelope contains a member name at {path} that cannot be decoded as UTF-16 text.";

    private static bool TryCollectObject(
        JsonElement target,
        string level,
        string path,
        EnvelopeFactsAccumulator collected,
        out string? failureMessage)
    {
        if (!collected.Objects.TryGetValue(path, out var kinds))
        {
            kinds = new Dictionary<string, JsonValueKind>(StringComparer.Ordinal);
            collected.Objects[path] = kinds;
        }

        var declared = DeclaredMembers[level];
        var children = EnvelopeChildMembers.TryGetValue(level, out var childMembers)
            ? childMembers
            : Array.Empty<string>();

        foreach (var property in target.EnumerateObject())
        {
            var name = DecodeMemberName(property);
            if (name is null)
            {
                failureMessage = UndecodableMemberNameMessage(path);
                return false;
            }

            if (!declared.Contains(name, StringComparer.Ordinal))
            {
                failureMessage = $"Envelope contains unknown member '{name}' at {path}.";
                return false;
            }

            if (kinds.ContainsKey(name))
            {
                failureMessage = $"Envelope contains duplicate member '{name}' at {path}.";
                return false;
            }

            kinds[name] = property.Value.ValueKind;
            var childPath = $"{path}.{name}";

            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                if (!TryCollectArray(
                        property.Value,
                        children.Contains(name, StringComparer.Ordinal) ? name : null,
                        childPath,
                        collected,
                        out failureMessage))
                {
                    return false;
                }
            }
            else if (property.Value.ValueKind == JsonValueKind.Object)
            {
                if (children.Contains(name, StringComparer.Ordinal))
                {
                    if (!TryCollectObject(property.Value, name, childPath, collected, out failureMessage))
                    {
                        return false;
                    }
                }
                else if (!TryCollectOpenContent(property.Value, childPath, out failureMessage))
                {
                    return false;
                }
            }
        }

        failureMessage = null;
        return true;
    }

    private static bool TryCollectArray(
        JsonElement array,
        string? elementLevel,
        string path,
        EnvelopeFactsAccumulator collected,
        out string? failureMessage)
    {
        var index = 0;
        foreach (var element in array.EnumerateArray())
        {
            var elementPath = $"{path}[{index}]";
            if (elementLevel is not null)
            {
                if (element.ValueKind == JsonValueKind.Object
                    && !TryCollectObject(element, elementLevel, elementPath, collected, out failureMessage))
                {
                    return false;
                }
            }
            else if (element.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                     && !TryCollectOpenContent(element, elementPath, out failureMessage))
            {
                return false;
            }

            index++;
        }

        collected.ArrayLengths[path] = index;
        failureMessage = null;
        return true;
    }

    /// <summary>
    /// Descriptor content such as <c>payload</c>: member names are not checked against the envelope
    /// vocabulary, but a repeated name is still an ambiguous answer, so it is rejected here — before the
    /// version and hash checks — keeping both the order and the diagnostic class fixed.
    /// </summary>
    private static bool TryCollectOpenContent(JsonElement target, string path, out string? failureMessage)
    {
        if (target.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in target.EnumerateObject())
            {
                var name = DecodeMemberName(property);
                if (name is null)
                {
                    failureMessage = UndecodableMemberNameMessage(path);
                    return false;
                }

                if (!seen.Add(name))
                {
                    failureMessage = $"Envelope contains duplicate member '{name}' at {path}.";
                    return false;
                }

                if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                    && !TryCollectOpenContent(property.Value, $"{path}.{name}", out failureMessage))
                {
                    return false;
                }
            }

            failureMessage = null;
            return true;
        }

        var index = 0;
        foreach (var element in target.EnumerateArray())
        {
            if (element.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                && !TryCollectOpenContent(element, $"{path}[{index}]", out failureMessage))
            {
                return false;
            }

            index++;
        }

        failureMessage = null;
        return true;
    }
}

internal sealed class EnvelopeFactsAccumulator
{
    internal Dictionary<string, Dictionary<string, JsonValueKind>> Objects { get; } =
        new(StringComparer.Ordinal);

    internal Dictionary<string, int> ArrayLengths { get; } = new(StringComparer.Ordinal);

    internal EnvelopeFactsAccumulator()
    {
        Objects[DescriptorAuthoringEnvelopeFacts.RootPath] = new Dictionary<string, JsonValueKind>(StringComparer.Ordinal);
    }
}

/// <summary>Raw member names and value kinds per protocol envelope object path, as read by Stage A.</summary>
internal sealed class EnvelopeMemberInventory(
    Dictionary<string, Dictionary<string, JsonValueKind>> byPath,
    Dictionary<string, int> arrayLengths)
{
    internal bool Has(string path, string name)
        => byPath.TryGetValue(path, out var kinds) && kinds.ContainsKey(name);

    internal JsonValueKind Kind(string path, string name)
        => byPath.TryGetValue(path, out var kinds) && kinds.TryGetValue(name, out var kind)
            ? kind
            : JsonValueKind.Undefined;

    internal bool HasKind(string path, string name, JsonValueKind expected)
        => byPath.TryGetValue(path, out var kinds) && kinds.TryGetValue(name, out var kind) && kind == expected;

    /// <summary>Length of a recorded array, or <c>null</c> when Stage A did not see an array at that path.</summary>
    internal int? ArrayLength(string path)
        => arrayLengths.TryGetValue(path, out var length) ? length : null;
}
