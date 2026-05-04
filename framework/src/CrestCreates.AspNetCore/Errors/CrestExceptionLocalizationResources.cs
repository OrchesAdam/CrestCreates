using System.Collections.Generic;

namespace CrestCreates.AspNetCore.Errors;

public class CrestExceptionLocalizationResources
{
    public CrestExceptionLocalizationResources(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> cultures)
    {
        Cultures = cultures;
    }

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Cultures { get; }
}
