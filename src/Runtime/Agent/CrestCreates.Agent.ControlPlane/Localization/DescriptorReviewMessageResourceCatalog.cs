using System.Collections.Frozen;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Agent.ControlPlane;

/// <summary>
/// Loads the two first-party descriptor-governance message resources by their
/// explicit manifest names. Culture fallback is intentionally string based so
/// the catalog also works in invariant-globalization NativeAOT hosts.
/// </summary>
internal sealed class DescriptorReviewMessageResourceCatalog
{
    private const string EnglishCulture = "en";
    private const string ChineseCulture = "zh-CN";
    private const string EnglishResourceName =
        "CrestCreates.Agent.ControlPlane.Localization.Resources.DescriptorReviewMessages.en.json";
    private const string ChineseResourceName =
        "CrestCreates.Agent.ControlPlane.Localization.Resources.DescriptorReviewMessages.zh-CN.json";
    private static readonly IReadOnlyDictionary<string, string> EmptyTemplates =
        new Dictionary<string, string>(StringComparer.Ordinal)
            .ToFrozenDictionary(StringComparer.Ordinal);

    private readonly ILogger<DefaultDescriptorReviewMessageTemplateCatalog> _logger;
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _resources;

    public DescriptorReviewMessageResourceCatalog(
        ILogger<DefaultDescriptorReviewMessageTemplateCatalog> logger)
    {
        _logger = logger;

        var resources = new Dictionary<string, IReadOnlyDictionary<string, string>>(
            StringComparer.OrdinalIgnoreCase);
        Load(resources, EnglishCulture, EnglishResourceName);
        Load(resources, ChineseCulture, ChineseResourceName);
        _resources = resources.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGetTemplate(string cultureName, string templateId, out string template)
    {
        var candidate = cultureName;
        while (!string.IsNullOrWhiteSpace(candidate))
        {
            if (_resources.TryGetValue(candidate, out var templates)
                && templates.TryGetValue(templateId, out template!))
            {
                return true;
            }

            var separator = candidate.LastIndexOf('-');
            if (separator <= 0)
                break;
            candidate = candidate[..separator];
        }

        template = string.Empty;
        return false;
    }

    internal IReadOnlyDictionary<string, string> GetExactTemplatesForTesting(string cultureName)
        => _resources.TryGetValue(cultureName, out var templates)
            ? templates
            : EmptyTemplates;

    private void Load(
        IDictionary<string, IReadOnlyDictionary<string, string>> resources,
        string cultureName,
        string resourceName)
    {
        try
        {
            using var stream = typeof(DescriptorReviewMessageResourceCatalog)
                .Assembly
                .GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                _logger.LogWarning(
                    "Descriptor governance localization resource {ResourceName} is unavailable.",
                    resourceName);
                return;
            }

            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                _logger.LogWarning(
                    "Descriptor governance localization resource {ResourceName} is not a JSON object.",
                    resourceName);
                return;
            }

            var templates = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    _logger.LogWarning(
                        "Descriptor governance localization resource {ResourceName} contains a non-string value.",
                        resourceName);
                    return;
                }

                templates[property.Name] = property.Value.GetString()!;
            }

            resources[cultureName] = templates.ToFrozenDictionary(StringComparer.Ordinal);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Descriptor governance localization resource {ResourceName} could not be loaded.",
                resourceName);
        }
    }
}
