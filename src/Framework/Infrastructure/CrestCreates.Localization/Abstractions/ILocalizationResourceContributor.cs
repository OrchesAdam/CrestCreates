namespace CrestCreates.Localization.Abstractions;

public interface ILocalizationResourceContributor
{
    string ResourceName { get; }

    Task<string?> GetStringAsync(string cultureName, string key);

    Task<IEnumerable<string>> GetAllKeysAsync(string cultureName);

    bool HasKey(string cultureName, string key);
}
