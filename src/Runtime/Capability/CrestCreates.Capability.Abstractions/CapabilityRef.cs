namespace CrestCreates.Capability.Abstractions;

/// <summary>
/// Structured reference to a capability. Avoids implicit string syntax.
/// </summary>
public readonly record struct CapabilityRef(string Id, int? Version = null)
{
    /// <summary>
    /// Parses string format:
    ///   "customer.create"     → ("customer.create", null)
    ///   "customer.create:3"   → ("customer.create", 3)
    /// </summary>
    public static CapabilityRef Parse(string input)
    {
        var separatorIndex = input.LastIndexOf(':');
        if (separatorIndex > 0 && int.TryParse(input.AsSpan(separatorIndex + 1), out var version))
        {
            return new CapabilityRef(input[..separatorIndex], version);
        }
        return new CapabilityRef(input);
    }

    public override string ToString()
        => Version.HasValue ? $"{Id}:{Version}" : Id;
}
