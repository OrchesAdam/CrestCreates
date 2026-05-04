using CrestCreates.Localization.Abstractions;

namespace CrestCreates.AspNetCore.Localization;

public class DictionaryResourceContributor : ILocalizationResourceContributor
{
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _cultures;

    public DictionaryResourceContributor(
        string resourceName,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> cultures)
    {
        ResourceName = resourceName;
        _cultures = cultures;
    }

    public string ResourceName { get; }

    public Task<string?> GetStringAsync(string cultureName, string key)
    {
        if (_cultures.TryGetValue(cultureName, out var entries) && entries.TryGetValue(key, out var value))
            return Task.FromResult<string?>(value);

        return Task.FromResult<string?>(null);
    }

    public Task<IEnumerable<string>> GetAllKeysAsync(string cultureName)
    {
        if (_cultures.TryGetValue(cultureName, out var entries))
            return Task.FromResult<IEnumerable<string>>(entries.Keys);

        return Task.FromResult<IEnumerable<string>>(Array.Empty<string>());
    }

    public bool HasKey(string cultureName, string key)
    {
        return _cultures.TryGetValue(cultureName, out var entries) && entries.ContainsKey(key);
    }
}
