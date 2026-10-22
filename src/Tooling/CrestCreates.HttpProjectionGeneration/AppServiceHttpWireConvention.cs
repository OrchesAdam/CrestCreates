#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using CrestCreates.Tooling.CrudGeneration;

namespace CrestCreates.Tooling.HttpProjection;

/// <summary>
/// Wire naming shared by the hand-written projection front-end. Route/verb derivation mirrors the
/// compatibility convention that the legacy DynamicApiAnalyzer used, so a migrated declaration keeps
/// the same wire; the legacy analyzer is retired in S4 and must not keep a second copy.
/// </summary>
public static class AppServiceHttpWireConvention
{
    public const string DefaultCapabilityPrefix = "compat.appservice.";

    public static string TrimAsyncSuffix(string methodName)
        => methodName.EndsWith("Async", StringComparison.Ordinal)
            ? methodName.Substring(0, methodName.Length - "Async".Length)
            : methodName;

    /// <summary>Service name used for capability ids and envelopes: interface name without its "I".</summary>
    public static string ServiceName(string contractName)
        => contractName.Length > 1 && contractName[0] == 'I' && char.IsUpper(contractName[1])
            ? contractName.Substring(1)
            : contractName;

    /// <summary>
    /// Default capability id for the formal front-end: owner assembly + full contract identity + action.
    /// The simple service name alone would collide across namespaces or assemblies (review finding
    /// S21-R1-02); a declaration that needs to keep an existing id sets CapabilityIdPrefix explicitly.
    /// </summary>
    public static string DefaultCapabilityId(string ownerAssemblyName, string contractFullMetadataName, string actionName)
        => DefaultCapabilityPrefix + CanonicalToken(ownerAssemblyName) + "."
           + CanonicalToken(contractFullMetadataName) + "." + ToKebabCase(TrimAsyncSuffix(actionName));

    /// <summary>
    /// Envelope name for the hand-written front-end. The owner identity is part of the type name so two
    /// same-short-name contracts cannot share one envelope type (S21-R1-02).
    /// </summary>
    public static string EnvelopeFullName(string ownerAssemblyName, string contractFullMetadataName, string actionName)
        => CrudEnvelopeConvention.EnvelopeNamespace + "." + CanonicalToken(ownerAssemblyName) + "__"
           + CanonicalToken(contractFullMetadataName) + "__" + CanonicalToken(TrimAsyncSuffix(actionName))
           + CrudEnvelopeConvention.Suffix;

    /// <summary>
    /// Canonical, case-preserving, injective identifier token (S21-R2-HTTP129-01):
    /// letters and digits are kept as-is, an underscore becomes '_005F', and every other character
    /// becomes '_' + its UTF-16 code unit in hex. The encoding is reversible, so namespace separators
    /// and literal underscores can never collapse two identities, and the result is a legal C#
    /// identifier fragment (a leading underscore is added when the first character is a digit).
    /// </summary>
    public static string CanonicalToken(string value)
    {
        if (value.Length == 0)
        {
            return "_EMPTY";
        }

        var builder = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsLetterOrDigit(character))
            {
                if (index == 0 && char.IsDigit(character))
                {
                    // Encode the leading digit itself. A conditional '_' prefix is not injective:
                    // "_Host" already encodes to "_005FHost", which is exactly what the prefix produced
                    // for "005FHost" (S21-R3 review).
                    builder.Append('_').Append(((int)character).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
                    continue;
                }

                builder.Append(character);
            }
            else if (character == '_')
            {
                builder.Append("_005F");
            }
            else
            {
                builder.Append('_').Append(((int)character).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    /// <summary>Verb derived when the action declaration does not name one.</summary>
    public static string DefaultHttpMethod(string methodName)
    {
        var name = TrimAsyncSuffix(methodName);
        return name switch
        {
            "Create" => "POST",
            "Update" => "PUT",
            "Delete" => "DELETE",
            _ => "GET",
        };
    }

    /// <summary>Route relative to the projection prefix, derived when the declaration does not name one.</summary>
    public static string DefaultRoute(string methodName, IReadOnlyList<string> parameterNames)
    {
        var name = TrimAsyncSuffix(methodName);
        return name switch
        {
            "Create" => string.Empty,
            "GetById" => "{id}",
            "Update" => "{id}",
            "Delete" => "{id}",
            "GetAll" => "all",
            "Count" => "count",
            "Query" => "query",
            _ when name.StartsWith("GetBy", StringComparison.Ordinal) && parameterNames.Count == 1
                => "by-" + ToKebabCase(name.Substring("GetBy".Length)) + "/{" + parameterNames[0] + "}",
            _ when name.StartsWith("Get", StringComparison.Ordinal) && parameterNames.Count == 0
                => ToKebabCase(name.Substring("Get".Length)),
            _ when name.StartsWith("Get", StringComparison.Ordinal) && parameterNames.Count == 1
                => ToKebabCase(name.Substring("Get".Length)),
            _ when name.StartsWith("Get", StringComparison.Ordinal) && parameterNames.Count > 1
                => ToKebabCase(name.Substring("Get".Length)) + "/{" + parameterNames[0] + "}",
            _ => ToKebabCase(name),
        };
    }

    /// <summary>
    /// Final emitted HTTP route. Owner conflicts must compare this value, not the action-relative
    /// route: two different prefixes can share a relative route, and two different prefix/route
    /// splits can produce the same final route (S22B-R2-02).
    /// </summary>
    public static string FullRoute(string routePrefix, string routePattern)
    {
        var prefix = routePrefix.Trim('/');
        var route = routePattern.Trim('/');
        if (prefix.Length == 0)
        {
            return "/" + route;
        }

        return route.Length == 0 ? "/" + prefix : "/" + prefix + "/" + route;
    }

    public static string ToKebabCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsUpper(character))
            {
                if (index > 0)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(character));
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    public static string EndpointId(string capabilityId) => CrudWireConvention.EndpointId(capabilityId);
}
