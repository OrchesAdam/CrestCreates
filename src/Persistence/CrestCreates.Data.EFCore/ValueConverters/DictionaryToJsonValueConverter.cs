using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CrestCreates.Data.EFCore.ValueConverters;

public sealed class DictionaryToJsonValueConverter : ValueConverter<Dictionary<string, object>, string>
{
    public DictionaryToJsonValueConverter()
        : base(
            v => JsonSerializer.Serialize(v, DictionaryJsonContext.Default.DictionaryStringObject),
            v => JsonSerializer.Deserialize(v, DictionaryJsonContext.Default.DictionaryStringObject)
                 ?? new Dictionary<string, object>())
    { }
}
