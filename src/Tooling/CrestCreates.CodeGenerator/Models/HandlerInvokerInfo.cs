namespace CrestCreates.CodeGenerator.Models;

internal sealed class HandlerInvokerInfo
{
    public string HandlerTypeName { get; set; } = string.Empty;
    public string HandlerNamespace { get; set; } = string.Empty;
    public string CapabilityName { get; set; } = string.Empty;
    public string InputTypeName { get; set; } = string.Empty;
    public string OutputTypeName { get; set; } = string.Empty;
}
