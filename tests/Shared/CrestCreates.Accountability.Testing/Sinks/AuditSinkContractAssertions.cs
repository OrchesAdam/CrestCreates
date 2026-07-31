namespace CrestCreates.Accountability.Testing.Sinks;

public static class AuditSinkContractAssertions
{
    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new AuditSinkContractAssertionException(message);
    }

    public static void NotEqual<T>(T notExpected, T actual, string message)
    {
        if (EqualityComparer<T>.Default.Equals(notExpected, actual))
            throw new AuditSinkContractAssertionException(message);
    }

    public static void Null<T>(T? value, string message)
    {
        if (value is not null)
            throw new AuditSinkContractAssertionException(message);
    }

    public static void NotNull<T>(T? value, string message)
    {
        if (value is null)
            throw new AuditSinkContractAssertionException(message);
    }

    public static void False(bool value, string message)
    {
        if (value)
            throw new AuditSinkContractAssertionException(message);
    }

    public static void SequenceEqual<T>(IEnumerable<T> actual, IEnumerable<T> expected, string message)
    {
        if (!actual.SequenceEqual(expected))
            throw new AuditSinkContractAssertionException(message);
    }
}
