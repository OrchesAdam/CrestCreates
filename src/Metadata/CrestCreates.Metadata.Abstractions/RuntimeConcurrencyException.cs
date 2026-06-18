namespace CrestCreates.Metadata.Abstractions;

public class RuntimeConcurrencyException : RuntimeStoreException
{
    public RuntimeConcurrencyException(string message) : base(message) { }
    public RuntimeConcurrencyException(string message, Exception innerException) : base(message, innerException) { }
}
