using System.Linq;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.DescriptorCapability;

namespace CrestCreates.DynamicApi;

public sealed class CapabilityEndpointDescriptorValidator
    : IRegistryValidator<CapabilityEndpointDescriptor>
{
    private readonly ICapabilityRegistry _capabilityRegistry;

    public CapabilityEndpointDescriptorValidator(ICapabilityRegistry capabilityRegistry)
    {
        _capabilityRegistry = capabilityRegistry;
    }

    public int Order => 100;

    public ValidationReport Validate(IReadOnlyList<CapabilityEndpointDescriptor> descriptors)
    {
        var issues = new List<ValidationIssue>();

        foreach (var descriptor in descriptors)
        {
            ValidateIdentity(descriptor, issues);
            ValidateRoute(descriptor, issues);
            ValidateBindings(descriptor, issues);
            ValidateOutput(descriptor, issues);
            ValidateCapabilityAuthority(descriptor, issues);
            ValidateProjection(descriptor, issues);
        }

        ValidateUniqueMethodRoute(descriptors, issues);

        return new ValidationReport(issues);
    }

    private static void ValidateIdentity(
        CapabilityEndpointDescriptor descriptor,
        List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(descriptor.Id))
            AddError(issues, "Capability endpoint Id is required.");
        if (!string.IsNullOrWhiteSpace(descriptor.Id) && descriptor.Id.Any(char.IsWhiteSpace))
            AddError(issues, $"Capability endpoint '{descriptor.Id}' Id must not contain whitespace characters.");
        if (string.IsNullOrWhiteSpace(descriptor.Name))
            AddError(issues, $"Capability endpoint '{descriptor.Id}' Name is required.");
        if (descriptor.Version <= 0)
            AddError(issues, $"Capability endpoint '{descriptor.Id}' Version must be greater than zero.");
        if (descriptor.HttpMethod == CapabilityEndpointHttpMethod.None)
            AddError(issues, $"Capability endpoint '{descriptor.Id}' HttpMethod must not be None.");
        if (string.IsNullOrWhiteSpace(descriptor.Capability.Id) || (descriptor.Capability.Version <= 0 && descriptor.Capability.SelectionMode == VersionSelectionMode.Exact))
            AddError(issues, $"Capability endpoint '{descriptor.Id}' Capability reference must specify Id and Version.");
    }

    private static void ValidateRoute(
        CapabilityEndpointDescriptor descriptor,
        List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(descriptor.RoutePattern))
        {
            AddError(issues, $"Capability endpoint '{descriptor.Id}' RoutePattern is required.");
            return;
        }

        if (!descriptor.RoutePattern.StartsWith("/", StringComparison.Ordinal))
            AddError(issues, $"Capability endpoint '{descriptor.Id}' RoutePattern must start with '/'.");
    }

    private static void ValidateBindings(
        CapabilityEndpointDescriptor descriptor,
        List<ValidationIssue> issues)
    {
        if (descriptor.InputBindings is null)
        {
            AddError(issues, $"Capability endpoint '{descriptor.Id}' InputBindings must not be null.");
            return;
        }

        foreach (var binding in descriptor.InputBindings)
        {
            if (string.IsNullOrWhiteSpace(binding.Name))
                AddError(issues, $"Capability endpoint '{descriptor.Id}' input binding Name must not be empty.");
        }

        var bodyCount = descriptor.InputBindings.Count(b => b.Source == CapabilityEndpointParameterSource.Body);
        if (bodyCount > 1)
            AddError(issues, $"Capability endpoint '{descriptor.Id}' may have at most one body input binding.");

        var routeTokens = ExtractRouteTokens(descriptor.RoutePattern)
            .ToHashSet(StringComparer.Ordinal);

        var routeBindings = descriptor.InputBindings
            .Where(b => b.Source == CapabilityEndpointParameterSource.Route)
            .Select(b => b.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var token in routeTokens.Except(routeBindings))
            AddError(issues, $"Capability endpoint '{descriptor.Id}' route token '{token}' has no route input binding.");

        foreach (var binding in routeBindings.Except(routeTokens))
            AddError(issues, $"Capability endpoint '{descriptor.Id}' route input binding '{binding}' has no route token.");
    }

    private static void ValidateOutput(
        CapabilityEndpointDescriptor descriptor,
        List<ValidationIssue> issues)
    {
        if (descriptor.OutputMapping is null)
        {
            AddError(issues, $"Capability endpoint '{descriptor.Id}' OutputMapping must not be null.");
            return;
        }

        if (descriptor.OutputMapping.SuccessStatusCode is < 200 or > 299)
            AddError(issues, $"Capability endpoint '{descriptor.Id}' success status code must be between 200 and 299.");
    }

    private void ValidateCapabilityAuthority(
        CapabilityEndpointDescriptor descriptor,
        List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(descriptor.Capability.Id))
            return;

        CapabilityDescriptor? capability;

        if (descriptor.Capability.Version <= 0)
        {
            // Resolve latest active capability for authority validation.
            // Version=0 with SelectionMode=Latest/Compatible means the resolver
            // picks the latest active at map time — validate against that.
            capability = ResolveLatestActiveCapability(descriptor.Capability.Id);
            if (capability is null)
                return; // Will fail-closed at map time
        }
        else
        {
            capability = _capabilityRegistry.GetByVersion(descriptor.Capability.Id, descriptor.Capability.Version);
        }

        if (capability is null)
        {
            AddError(issues, $"Capability endpoint '{descriptor.Id}' references missing Capability '{descriptor.Capability.Id}' v{descriptor.Capability.Version}.");
            return;
        }

        switch (descriptor.AuthorizationMode)
        {
            case CapabilityEndpointAuthorizationMode.AllowAnonymous:
                // AllowAnonymous weakens any capability that has permissions or is high-risk
                if (capability.Permissions.Count > 0)
                    AddError(issues, $"Capability endpoint '{descriptor.Id}' AllowAnonymous would weaken Capability '{capability.Id}' permissions.");
                if (capability.RiskLevel >= CapabilityRiskLevel.High)
                    AddError(issues, $"Capability endpoint '{descriptor.Id}' AllowAnonymous would weaken high-risk Capability '{capability.Id}'.");
                break;

            case CapabilityEndpointAuthorizationMode.InheritCapability:
                // InheritCapability defers to the capability's authority model — if the capability
                // has no permissions and is high-risk, the endpoint is effectively unguarded
                if (capability.Permissions.Count == 0 && capability.RiskLevel >= CapabilityRiskLevel.High)
                    AddError(issues, $"Capability endpoint '{descriptor.Id}' InheritCapability on high-risk Capability '{capability.Id}' with no permissions leaves the endpoint unguarded.");
                break;

            case CapabilityEndpointAuthorizationMode.RequireAuthenticated:
                // RequireAuthenticated is the most restrictive mode — no authority validation needed
                break;

            default:
                AddError(issues, $"Capability endpoint '{descriptor.Id}' has unrecognized AuthorizationMode '{descriptor.AuthorizationMode}'.");
                break;
        }
    }

    private static void ValidateProjection(
        CapabilityEndpointDescriptor descriptor,
        List<ValidationIssue> issues)
    {
        if (descriptor.Projection is null)
        {
            AddError(issues, $"Capability endpoint '{descriptor.Id}' Projection must not be null.");
            return;
        }

        if (descriptor.Projection.OperationId is not null
            && string.IsNullOrWhiteSpace(descriptor.Projection.OperationId))
        {
            AddError(issues, $"Capability endpoint '{descriptor.Id}' Projection.OperationId must be stable and non-empty when specified.");
        }
    }

    private static void ValidateUniqueMethodRoute(
        IReadOnlyList<CapabilityEndpointDescriptor> descriptors,
        List<ValidationIssue> issues)
    {
        var validEndpoints = descriptors
            .Where(d => d.HttpMethod != CapabilityEndpointHttpMethod.None
                        && !string.IsNullOrWhiteSpace(d.RoutePattern))
            .GroupBy(d => (HttpMethod: d.HttpMethod, RoutePattern: NormalizeRoutePattern(d.RoutePattern!)))
            .Where(g => g.Count() > 1);

        foreach (var group in validEndpoints)
        {
            var ids = string.Join(", ", group.Select(d => d.Id));
            AddError(issues,
                $"Duplicate capability endpoint route: {group.Key.HttpMethod} {group.Key.RoutePattern}. Endpoint IDs: {ids}");
        }
    }

    private static string NormalizeRoutePattern(string routePattern)
    {
        // Normalize route parameters: strip constraints, catch-all markers, and optional markers
        // so that /api/books/{id:int} and /api/books/{id} are treated as the same route.
        var result = new System.Text.StringBuilder(routePattern.Length);
        var i = 0;
        while (i < routePattern.Length)
        {
            if (routePattern[i] == '{')
            {
                result.Append('{');
                i++;
                // Skip optional marker
                if (i < routePattern.Length && routePattern[i] == '*')
                {
                    i++;
                    if (i < routePattern.Length && routePattern[i] == '*')
                        i++;
                }
                // Read parameter name until ':', '}', or '?'
                var nameStart = i;
                while (i < routePattern.Length && routePattern[i] != ':' && routePattern[i] != '}' && routePattern[i] != '?')
                    i++;
                result.Append(routePattern[nameStart..i]);
                // Skip constraint and optional marker until '}'
                while (i < routePattern.Length && routePattern[i] != '}')
                    i++;
                if (i < routePattern.Length)
                {
                    result.Append('}');
                    i++;
                }
            }
            else
            {
                result.Append(routePattern[i]);
                i++;
            }
        }

        return result.ToString().TrimEnd('/');
    }

    private CapabilityDescriptor? ResolveLatestActiveCapability(string capabilityId)
    {
        // Try GetById first (may return latest active by Id)
        var byId = _capabilityRegistry.GetById(capabilityId);
        if (byId is not null && byId.State == DescriptorState.Active)
            return byId;

        // Scan all and find latest active
        var active = _capabilityRegistry.GetAll()
            .Where(d => d.Id == capabilityId && d.State == DescriptorState.Active)
            .MaxBy(d => d.Version);

        return active;
    }

    private static void AddError(List<ValidationIssue> issues, string message)
        => issues.Add(new ValidationIssue(SeverityLevel.Error, message));

    private static IEnumerable<string> ExtractRouteTokens(string routePattern)
    {
        var index = 0;
        while (index < routePattern.Length)
        {
            var start = routePattern.IndexOf('{', index);
            if (start < 0)
                yield break;

            var end = routePattern.IndexOf('}', start + 1);
            if (end < 0)
                yield break;

            var token = routePattern[(start + 1)..end];
            var constraintIndex = token.IndexOf(':', StringComparison.Ordinal);
            if (constraintIndex >= 0)
                token = token[..constraintIndex];

            if (token.StartsWith("**", StringComparison.Ordinal))
                token = token[2..];
            if (token.EndsWith("?", StringComparison.Ordinal))
                token = token[..^1];

            if (!string.IsNullOrWhiteSpace(token))
                yield return token;

            index = end + 1;
        }
    }
}
