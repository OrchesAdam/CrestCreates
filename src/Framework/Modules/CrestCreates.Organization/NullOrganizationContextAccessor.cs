using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization;

public sealed class NullOrganizationContextAccessor : IOrganizationContextAccessor
{
    public OrganizationContext? Current => null;
}
