namespace CrestCreates.Agent.Tools.Persistence.Testing.Assertions;

public static class AgentToolPreDispatchContractAssertions
{
    public static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new AgentToolPreDispatchContractAssertionException(message);
        }
    }

    public static void Equal<T>(T expected, T actual, string message)
        where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new AgentToolPreDispatchContractAssertionException(
                $"{message} Expected: {expected}; actual: {actual}.");
        }
    }
}
