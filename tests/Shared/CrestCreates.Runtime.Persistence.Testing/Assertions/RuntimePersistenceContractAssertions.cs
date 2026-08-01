namespace CrestCreates.Runtime.Persistence.Testing.Assertions;

public static class RuntimePersistenceContractAssertions
{
    public static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new RuntimePersistenceContractAssertionException(message);
        }
    }

    public static void Equal<T>(T expected, T actual, string message)
        where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new RuntimePersistenceContractAssertionException(
                $"{message} Expected: {expected}; actual: {actual}.");
        }
    }
}
