using Microsoft.Extensions.Localization;
using System.Globalization;

namespace CrestCreates.Localization.Services;

public class LocalizationService : ILocalizationService
{
    private readonly IStringLocalizer _localizer;

    public LocalizationService(IStringLocalizerFactory factory)
    {
        _localizer = factory.Create(typeof(LocalizationService));
        CurrentCulture = CultureInfo.CurrentUICulture.Name;
    }

    public string GetString(string key)
    {
        return _localizer[key];
    }

    public string GetString(string key, params object[] arguments)
    {
        return _localizer[key, arguments];
    }

    public string GetString(string key, string cultureName)
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(cultureName);
            return _localizer[key];
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    public string GetString(string key, string cultureName, params object[] arguments)
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(cultureName);
            return _localizer[key, arguments];
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    public void ChangeCulture(string cultureName)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(cultureName);
        CurrentCulture = cultureName;
    }

    public string CurrentCulture { get; private set; }
}