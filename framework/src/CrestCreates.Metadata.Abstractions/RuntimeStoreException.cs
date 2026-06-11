namespace CrestCreates.Metadata.Abstractions;

public class RuntimeStoreException : Exception
{
    public RuntimeStoreException(string message) : base(message) { }
    public RuntimeStoreException(string message, Exception innerException) : base(message, innerException) { }
}
