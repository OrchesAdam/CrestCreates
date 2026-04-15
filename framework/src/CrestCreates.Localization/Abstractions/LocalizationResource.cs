namespace CrestCreates.Localization.Abstractions;

public class LocalizationResource
{
    public required string Name { get; init; }
    public required ILocalizationResourceContributor Contributor { get; init; }
    public int Priority { get; init; }
}
