using CrestCreates.Agent.Memory.Abstractions;

namespace CrestCreates.Agent.Memory.Persistence.Testing.Assertions;

/// <summary>
/// Runner-free assertion helpers for the provider-neutral Agent Memory contract
/// kit. Never references xUnit/FluentAssertions; failures surface as
/// <see cref="AgentMemoryPersistenceContractAssertionException"/>.
/// </summary>
public static class AgentMemoryPersistenceContractAssertions
{
    public static void True(bool condition, string message)
    {
        if (!condition)
            throw new AgentMemoryPersistenceContractAssertionException(message);
    }

    public static void False(bool condition, string message)
        => True(!condition, message);

    public static void Null(object? value, string message)
        => True(value is null, message);

    public static void NotNull(object? value, string message)
        => True(value is not null, message);

    public static void Equal<T>(T expected, T actual, string message)
        where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new AgentMemoryPersistenceContractAssertionException(
                $"{message} Expected: {expected}; actual: {actual}.");
        }
    }

    public static void SequenceEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, string message)
        where T : notnull
    {
        if (expected.Count != actual.Count
            || expected.Zip(actual).Any(pair => !EqualityComparer<T>.Default.Equals(pair.First, pair.Second)))
        {
            throw new AgentMemoryPersistenceContractAssertionException(
                $"{message} Expected [{string.Join(", ", expected)}]; actual [{string.Join(", ", actual)}].");
        }
    }

    public static TException Throws<TException>(Action action, string message)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }
        catch (Exception other)
        {
            throw new AgentMemoryPersistenceContractAssertionException(
                $"{message} Expected {typeof(TException).Name} but received {other.GetType().Name}: {other.Message}");
        }
        throw new AgentMemoryPersistenceContractAssertionException(
            $"{message} Expected {typeof(TException).Name} but no exception was thrown.");
    }

    public static async Task<TException> ThrowsAsync<TException>(Func<Task> action, string message)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (TException exception)
        {
            return exception;
        }
        catch (Exception other)
        {
            throw new AgentMemoryPersistenceContractAssertionException(
                $"{message} Expected {typeof(TException).Name} but received {other.GetType().Name}: {other.Message}");
        }
        throw new AgentMemoryPersistenceContractAssertionException(
            $"{message} Expected {typeof(TException).Name} but no exception was thrown.");
    }

    public static void MemoryOperationFailure(
        AgentMemoryOperationFailureCode expectedCode,
        Exception actual,
        string message)
    {
        var operation = actual as AgentMemoryOperationException;
        True(operation is not null, $"{message} Expected AgentMemoryOperationException but received {actual.GetType().Name}.");
        Equal(expectedCode, operation!.Code, message);
    }

    public static void SameOrdering(IReadOnlyList<string> expectedIds, IReadOnlyList<string> actualIds, string message)
        => SequenceEqual(
            expectedIds.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            actualIds,
            message);
}
