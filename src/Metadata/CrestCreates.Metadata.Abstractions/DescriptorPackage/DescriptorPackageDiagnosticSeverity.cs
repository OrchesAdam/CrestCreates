using System.Text.Json.Serialization;

namespace CrestCreates.Metadata.Abstractions.DescriptorPackage;

[JsonConverter(typeof(JsonStringEnumConverter<DescriptorPackageDiagnosticSeverity>))]
public enum DescriptorPackageDiagnosticSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2
}
