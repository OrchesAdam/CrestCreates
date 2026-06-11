namespace CrestCreates.Organization.Abstractions;

public interface IOrganizationContextAccessor
{
    OrganizationContext? Current { get; }
}
