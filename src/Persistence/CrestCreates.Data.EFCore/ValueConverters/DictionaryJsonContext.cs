using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestCreates.Data.EFCore.ValueConverters;

[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(Guid))]
public sealed partial class DictionaryJsonContext : JsonSerializerContext;
