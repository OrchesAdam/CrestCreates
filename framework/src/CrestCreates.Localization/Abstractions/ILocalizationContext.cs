namespace CrestCreates.Localization.Abstractions;

public interface ILocalizationContext
{
    string CurrentCulture { get; }
    ILocalizationContext? Parent { get; }

    IDisposable Scope(string cultureName);
}
