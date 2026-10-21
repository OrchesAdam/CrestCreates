using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace CrestCreates.DynamicApi;

/// <summary>
/// Canonical route identity for collision prechecks. ASP.NET Core matches route templates
/// case-insensitively and parameter names are not part of the match, so raw-text comparison would
/// miss '/api/x' vs '/API/x' and '/x/{id}' vs '/x/{ticketId}'. Parameter constraints do take part in
/// matching precedence, so they stay in the canonical key.
/// </summary>
internal static class RoutePatternCollisionGuard
{
    internal static HashSet<(string Method, string Route)> CollectMappedRoutes(IEndpointRouteBuilder endpoints)
    {
        var routes = new HashSet<(string Method, string Route)>();
        foreach (var endpoint in endpoints.DataSources
                     .SelectMany(source => source.Endpoints)
                     .OfType<RouteEndpoint>())
        {
            var raw = endpoint.RoutePattern.RawText;
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var canonical = Canonicalize(raw);
            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
            if (methods is null || methods.Count == 0)
            {
                // An endpoint without method metadata matches every method.
                routes.Add(("*", canonical));
                continue;
            }

            foreach (var method in methods)
            {
                routes.Add((method.ToUpperInvariant(), canonical));
            }
        }

        return routes;
    }

    internal static bool IsMapped(
        HashSet<(string Method, string Route)> mappedRoutes,
        string method,
        string route)
    {
        var canonical = Canonicalize(route);
        return mappedRoutes.Contains((method.ToUpperInvariant(), canonical))
               || mappedRoutes.Contains(("*", canonical));
    }

    internal static string Canonicalize(string route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return "/";
        }

        RoutePattern pattern;
        try
        {
            pattern = RoutePatternFactory.Parse(route);
        }
        catch (RoutePatternException)
        {
            // Invalid legacy text cannot be normalized structurally; mapping would fail later anyway.
            return "/" + route.Trim().Trim('/').ToLowerInvariant();
        }

        var builder = new StringBuilder();
        foreach (var segment in pattern.PathSegments)
        {
            builder.Append('/');
            foreach (var part in segment.Parts)
            {
                switch (part)
                {
                    case RoutePatternLiteralPart literal:
                        builder.Append("l:").Append(literal.Content.ToLowerInvariant());
                        break;
                    case RoutePatternSeparatorPart separator:
                        builder.Append("s:").Append(separator.Content.ToLowerInvariant());
                        break;
                    case RoutePatternParameterPart parameter:
                        builder.Append(parameter.IsCatchAll ? "c:" : "p:");
                        foreach (var policy in parameter.ParameterPolicies
                                     .Select(reference => reference.Content)
                                     .Where(content => !string.IsNullOrEmpty(content))
                                     .OrderBy(content => content, StringComparer.Ordinal))
                        {
                            builder.Append(':').Append(policy);
                        }

                        break;
                    default:
                        builder.Append(part.ToString()?.ToLowerInvariant());
                        break;
                }
            }
        }

        return builder.Length == 0 ? "/" : builder.ToString();
    }
}
