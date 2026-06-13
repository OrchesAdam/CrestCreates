namespace CrestCreates.Metadata.Abstractions.DescriptorCompatibility;

// Unsupported = 0 is deliberate: MaxLevel uses natural Max() over classified
// findings (Compatible..Breaking). Unsupported is excluded from MaxLevel unless
// all findings are Unsupported. This matches the semantics lock: Unsupported
// means "insufficient rule knowledge", not "more severe than Breaking".
public enum DescriptorCompatibilityLevel
{
    Unsupported = 0,
    Compatible = 1,
    Risky = 2,
    SecuritySensitive = 3,
    Breaking = 4
}
