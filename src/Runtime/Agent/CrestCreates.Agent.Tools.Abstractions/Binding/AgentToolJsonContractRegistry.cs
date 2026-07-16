using System.Collections.Concurrent;

namespace CrestCreates.Agent.Tools;

public static class AgentToolJsonContractRegistry
{
    private static readonly ConcurrentDictionary<Type, byte> InputTypes = new();
    private static readonly ConcurrentDictionary<Type, byte> OutputTypes = new();

    public static void RegisterInputType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        InputTypes.TryAdd(type, 0);
    }

    public static void RegisterOutputType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        OutputTypes.TryAdd(type, 0);
    }

    public static IReadOnlyCollection<Type> GetInputTypes() => InputTypes.Keys.ToArray();

    public static IReadOnlyCollection<Type> GetOutputTypes() => OutputTypes.Keys.ToArray();
}
