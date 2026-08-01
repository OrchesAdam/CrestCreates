namespace CrestCreates.Metadata.Abstractions.Runtime;

public sealed class RuntimeDescriptorPinValidationException : Exception
{
    public RuntimeDescriptorPinValidationException(string message)
        : base(message)
    {
    }

    public RuntimeDescriptorPinValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
