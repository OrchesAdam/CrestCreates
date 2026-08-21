using System.Text.Json.Serialization;
using CrestCreates.HumanTask.Abstractions;

namespace CrestCreates.HumanTask;

[JsonSerializable(typeof(HumanTaskCompletedEvent))]
internal partial class HumanTaskJsonSerializerContext : JsonSerializerContext
{
}
