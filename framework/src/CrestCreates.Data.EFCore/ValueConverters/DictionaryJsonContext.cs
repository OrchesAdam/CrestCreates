using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestCreates.Data.EFCore.ValueConverters;

[JsonSerializable(typeof(Dictionary<string, object>))]
public sealed partial class DictionaryJsonContext : JsonSerializerContext;
