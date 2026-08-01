using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace CrestCreates.Runtime.Persistence.Abstractions.State;

public sealed record RuntimeStateBag
{
    [JsonConstructor]
    public RuntimeStateBag(IReadOnlyDictionary<string, RuntimeStateValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var ordered = new SortedDictionary<string, RuntimeStateValue>(StringComparer.Ordinal);
        foreach (var pair in values)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key);
            ArgumentNullException.ThrowIfNull(pair.Value);
            if (!ordered.TryAdd(pair.Key, pair.Value))
            {
                throw new ArgumentException(
                    $"Duplicate Runtime State key '{pair.Key}'.",
                    nameof(values));
            }
        }

        Values = new ReadOnlyDictionary<string, RuntimeStateValue>(ordered);
    }

    public RuntimeStateBag(IEnumerable<KeyValuePair<string, RuntimeStateValue>> values)
        : this(values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal))
    {
    }

    public IReadOnlyDictionary<string, RuntimeStateValue> Values { get; }
}
