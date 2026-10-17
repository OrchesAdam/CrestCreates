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

    public static string DefaultCapabilityId(string contractName, string actionName)
        => DefaultCapabilityPrefix + ToKebabCase(ServiceName(contractName)) + "." + ToKebabCase(TrimAsyncSuffix(actionName));

    public static string EnvelopeFullName(string contractName, string actionName)
        => CrudEnvelopeConvention.EnvelopeFullName(ServiceName(contractName), TrimAsyncSuffix(actionName));

    public static string EndpointId(string capabilityId) => CrudWireConvention.EndpointId(capabilityId);

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
    public static string DefaultRoute(string methodName, IReadOnlyList<string> scalarParameterNames)
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
            _ when name.StartsWith("GetBy", StringComparison.Ordinal) && scalarParameterNames.Count == 1
                => "by-" + ToKebabCase(name.Substring("GetBy".Length)) + "/{" + scalarParameterNames[0] + "}",
            _ when name.StartsWith("Get", StringComparison.Ordinal) && scalarParameterNames.Count == 0
                => ToKebabCase(name.Substring("Get".Length)),
            _ when name.StartsWith("Get", StringComparison.Ordinal) && scalarParameterNames.Count == 1
                => ToKebabCase(name.Substring("Get".Length)),
            _ when name.StartsWith("Get", StringComparison.Ordinal) && scalarParameterNames.Count > 1
                => ToKebabCase(name.Substring("Get".Length)) + "/{" + scalarParameterNames[0] + "}",
            _ => ToKebabCase(name),
        };
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
}
