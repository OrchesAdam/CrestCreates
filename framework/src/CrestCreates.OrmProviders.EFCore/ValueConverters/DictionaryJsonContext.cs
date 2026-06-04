using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestCreates.OrmProviders.EFCore.ValueConverters;

[JsonSerializable(typeof(Dictionary<string, object>))]
public sealed partial class DictionaryJsonContext : JsonSerializerContext;
