namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Canonical string constants for descriptor kind values in canonical hash metadata.
/// Used in <see cref="CanonicalHash.DescriptorKind"/> and envelope metadata.
/// Never use enum.ToString() for hash input — always use these canonical string helpers.
/// </summary>
public static class DescriptorKindNames
{
    public const string Schema = "Schema";
    public const string Capability = "Capability";
    public const string Event = "Event";
    public const string Workflow = "Workflow";
    public const string Form = "Form";
    public const string HumanTask = "HumanTask";

    /// <summary>
    /// Converts a <see cref="DescriptorKind"/> to its canonical string representation.
    /// </summary>
    public static string ToCanonicalString(DescriptorKind kind) => kind switch
    {
        DescriptorKind.Schema => Schema,
        DescriptorKind.Capability => Capability,
        DescriptorKind.Event => Event,
        DescriptorKind.Workflow => Workflow,
        DescriptorKind.Form => Form,
        DescriptorKind.HumanTask => HumanTask,
        DescriptorKind.Unknown => throw new InvalidOperationException($"Canonical string not defined for {nameof(DescriptorKind)}.{nameof(DescriptorKind.Unknown)}."),
        _ => throw new System.ComponentModel.InvalidEnumArgumentException(nameof(kind), (int)kind, typeof(DescriptorKind))
    };
}
