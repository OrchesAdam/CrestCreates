namespace CrestCreates.Metadata.Abstractions;

public enum DescriptorBindingStatus
{
    /// <summary>All bindings valid; descriptor is runtime-executable.</summary>
    RuntimeReady,

    /// <summary>Warnings only (e.g., optional schema field missing from form).</summary>
    PartiallyBound,

    /// <summary>Missing handler or binding (e.g., capability without handler).</summary>
    Unbound,

    /// <summary>Feature declared but current runtime explicitly does not support it.</summary>
    Unsupported,

    /// <summary>Unresolved references (schema missing, target missing, etc.).</summary>
    Invalid
}
