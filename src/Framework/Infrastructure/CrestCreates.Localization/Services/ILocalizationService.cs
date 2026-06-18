namespace CrestCreates.Localization.Services;

public interface ILocalizationService
{
    string CurrentCulture { get; }

    // Sync methods (use async internally)
    string GetString(string key);
    string GetString(string key, params object[] arguments);
    string GetString(string key, string cultureName);
    string GetString(string key, string cultureName, params object[] arguments);

    // Async methods
    Task<string?> GetStringAsync(string key);
    Task<string?> GetStringAsync(string key, params object[] arguments);
    Task<string?> GetStringAsync(string key, string cultureName);
    Task<string?> GetStringAsync(string key, string cultureName, params object[] arguments);

    // Culture management
    IDisposable ChangeCulture(string cultureName);
    Task<IDisposable> ChangeCultureAsync(string cultureName);
}
