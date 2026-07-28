using System.Collections.Concurrent;

namespace CrestCreates.Mcp;

public static class McpToolJsonContractRegistry
{
    private static readonly ConcurrentDictionary<Type, byte> InputTypes = new();
    private static readonly ConcurrentDictionary<Type, byte> OutputTypes = new();

    public static void RegisterInputType(Type type) => InputTypes.TryAdd(type, 0);

    public static void RegisterOutputType(Type type) => OutputTypes.TryAdd(type, 0);

    public static IReadOnlyCollection<Type> GetInputTypes() => InputTypes.Keys.ToArray();

    public static IReadOnlyCollection<Type> GetOutputTypes() => OutputTypes.Keys.ToArray();
}
