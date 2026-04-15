using CrestCreates.Localization.Abstractions;
using Microsoft.Extensions.Localization;
using System.Collections.Concurrent;
using System.Globalization;

namespace CrestCreates.Localization.Services;

public class LocalizationService : ILocalizationService
{
    private readonly IStringLocalizer _localizer;
    private readonly string _defaultCulture;
    private readonly ConcurrentDictionary<string, ILocalizationResourceContributor> _contributors = new();
    private readonly ConcurrentBag<LocalizationResource> _resources = new();

    // Thread-safe culture context using AsyncLocal
    private static readonly AsyncLocal<CultureContext?> _cultureContext = new();

    public LocalizationService(
        IStringLocalizerFactory factory,
        string defaultCulture = "en")
    {
        _localizer = factory.Create(typeof(LocalizationService));
        _defaultCulture = defaultCulture;
    }

    public string CurrentCulture => _cultureContext.Value?.Culture ?? _defaultCulture;

    public void RegisterResource(LocalizationResource resource)
    {
        _resources.Add(resource);
        _contributors[resource.Name] = resource.Contributor;
    }

    public async Task<string?> GetStringAsync(string key)
    {
        return await GetStringAsync(key, CurrentCulture);
    }

    public async Task<string?> GetStringAsync(string key, params object[] arguments)
    {
        var result = await GetStringAsync(key);
        return result != null ? FormatString(result, arguments) : null;
    }

    public async Task<string?> GetStringAsync(string key, string cultureName)
    {
        // Try each resource contributor in priority order
        foreach (var resource in _resources.OrderBy(r => r.Priority))
        {
            var value = await resource.Contributor.GetStringAsync(cultureName, key);
            if (value != null)
                return value;

            // Try fallback culture
            var fallbackCulture = GetFallbackCulture(cultureName);
            if (fallbackCulture != cultureName)
            {
                value = await resource.Contributor.GetStringAsync(fallbackCulture, key);
                if (value != null)
                    return value;
            }
        }

        // Fallback to IStringLocalizer
        return _localizer[key];
    }

    public async Task<string?> GetStringAsync(string key, string cultureName, params object[] arguments)
    {
        var result = await GetStringAsync(key, cultureName);
        return result != null ? FormatString(result, arguments) : null;
    }

    public string GetString(string key)
    {
        return GetStringAsync(key).GetAwaiter().GetResult() ?? key;
    }

    public string GetString(string key, params object[] arguments)
    {
        return GetStringAsync(key, arguments).GetAwaiter().GetResult() ?? key;
    }

    public string GetString(string key, string cultureName)
    {
        return GetStringAsync(key, cultureName).GetAwaiter().GetResult() ?? key;
    }

    public string GetString(string key, string cultureName, params object[] arguments)
    {
        return GetStringAsync(key, cultureName, arguments).GetAwaiter().GetResult() ?? key;
    }

    public IDisposable ChangeCulture(string cultureName)
    {
        return new CultureScope(cultureName);
    }

    public Task<IDisposable> ChangeCultureAsync(string cultureName)
    {
        return Task.FromResult(ChangeCulture(cultureName));
    }

    private static string GetFallbackCulture(string cultureName)
    {
        var culture = new CultureInfo(cultureName);
        if (culture.Parent != null && !culture.IsNeutralCulture)
            return culture.Parent.Name;
        return cultureName;
    }

    private static string FormatString(string format, object?[] arguments)
    {
        return arguments.Length switch
        {
            0 => format,
            1 => string.Format(format, arguments[0]),
            _ => string.Format(format, arguments)
        };
    }

    private class CultureScope : IDisposable
    {
        private readonly string? _parentCulture;

        public CultureScope(string cultureName)
        {
            _parentCulture = _cultureContext.Value?.Culture;
            _cultureContext.Value = new CultureContext(cultureName);
        }

        public void Dispose()
        {
            if (_parentCulture != null)
            {
                _cultureContext.Value = new CultureContext(_parentCulture);
            }
            else
            {
                _cultureContext.Value = null;
            }
        }
    }

    private class CultureContext
    {
        public string Culture { get; }

        public CultureContext(string culture)
        {
            Culture = culture;
        }
    }
}
