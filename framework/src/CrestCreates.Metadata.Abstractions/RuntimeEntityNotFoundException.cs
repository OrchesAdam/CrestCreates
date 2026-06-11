namespace CrestCreates.Metadata.Abstractions;

public class RuntimeEntityNotFoundException : RuntimeStoreException
{
    public RuntimeEntityNotFoundException(string message) : base(message) { }
    public RuntimeEntityNotFoundException(string message, Exception innerException) : base(message, innerException) { }
}
