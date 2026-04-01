namespace CrestCreates.Localization.Services;

public interface ILocalizationService
{
    string GetString(string key);
    string GetString(string key, params object[] arguments);
    string GetString(string key, string cultureName);
    string GetString(string key, string cultureName, params object[] arguments);
    void ChangeCulture(string cultureName);
    string CurrentCulture { get; }
}