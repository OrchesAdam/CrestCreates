namespace CrestCreates.Domain.Shared.Features;

[System.Flags]
public enum FeatureScope
{
    Global = 1,
    Tenant = 2
}
