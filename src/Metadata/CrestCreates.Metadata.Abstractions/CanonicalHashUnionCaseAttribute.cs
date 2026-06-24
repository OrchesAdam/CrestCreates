using System;

namespace CrestCreates.Metadata.Abstractions;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CanonicalHashUnionCaseAttribute : Attribute
{
    public CanonicalHashUnionCaseAttribute(Type caseType, string discriminatorValue)
    {
        CaseType = caseType;
        DiscriminatorValue = discriminatorValue;
    }

    public Type CaseType { get; }
    public string DiscriminatorValue { get; }
    public required Type ValueProfile { get; init; }
}
