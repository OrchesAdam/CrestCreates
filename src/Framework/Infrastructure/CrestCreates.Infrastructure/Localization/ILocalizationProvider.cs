using System.Globalization;

namespace CrestCreates.Infrastructure.Localization
{
    public interface ILocalizationProvider
    {
        string? GetString(string name);
        string? GetString(string name, params object[] args);
        string? GetString(string name, CultureInfo culture);
        string? GetString(string name, CultureInfo culture, params object[] args);
    }
}
