using System;

namespace CrestCreates.Metadata.Abstractions.CanonicalHashing;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CanonicalHashUnionProfileAttribute : Attribute
{
    public required Type TargetType { get; init; }
    public required string Discriminator { get; init; }
}
