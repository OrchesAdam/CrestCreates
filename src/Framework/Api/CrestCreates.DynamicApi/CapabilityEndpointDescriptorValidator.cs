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

        return new ValidationReport(issues);
    }

    private static void ValidateIdentity(
        CapabilityEndpointDescriptor descriptor,
        List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(descriptor.Id))
            AddError(issues, "Capability endpoint Id is required.");
        if (string.IsNullOrWhiteSpace(descriptor.Name))
            AddError(issues, $"Capability endpoint '{descriptor.Id}' Name is required.");
        if (descriptor.Version <= 0)
            AddError(issues, $"Capability endpoint '{descriptor.Id}' Version must be greater than zero.");
        if (descriptor.HttpMethod == CapabilityEndpointHttpMethod.None)
            AddError(issues, $"Capability endpoint '{descriptor.Id}' HttpMethod must not be None.");
        if (string.IsNullOrWhiteSpace(descriptor.Capability.Id) || descriptor.Capability.Version <= 0)
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
        if (descriptor.OutputMapping.SuccessStatusCode is < 200 or > 299)
            AddError(issues, $"Capability endpoint '{descriptor.Id}' success status code must be between 200 and 299.");
    }

    private void ValidateCapabilityAuthority(
        CapabilityEndpointDescriptor descriptor,
        List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(descriptor.Capability.Id) || descriptor.Capability.Version <= 0)
            return;

        var capability = _capabilityRegistry.GetByVersion(descriptor.Capability.Id, descriptor.Capability.Version);
        if (capability is null)
        {
            AddError(issues, $"Capability endpoint '{descriptor.Id}' references missing Capability '{descriptor.Capability.Id}' v{descriptor.Capability.Version}.");
            return;
        }

        if (descriptor.AuthorizationMode != CapabilityEndpointAuthorizationMode.AllowAnonymous || capability is null)
            return;

        if (capability.Permissions.Count > 0)
        {
            AddError(issues, $"Capability endpoint '{descriptor.Id}' AllowAnonymous would weaken Capability '{capability.Id}' permissions.");
        }

        if (capability.RiskLevel >= CapabilityRiskLevel.High)
        {
            AddError(issues, $"Capability endpoint '{descriptor.Id}' AllowAnonymous would weaken high-risk Capability '{capability.Id}'.");
        }
    }

    private static void ValidateProjection(
        CapabilityEndpointDescriptor descriptor,
        List<ValidationIssue> issues)
    {
        if (descriptor.Projection.OperationId is not null
            && string.IsNullOrWhiteSpace(descriptor.Projection.OperationId))
        {
            AddError(issues, $"Capability endpoint '{descriptor.Id}' Projection.OperationId must be stable and non-empty when specified.");
        }
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
