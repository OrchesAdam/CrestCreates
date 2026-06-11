namespace CrestCreates.Organization.Abstractions;

public class OrganizationHierarchyException : OrganizationException
{
    public OrganizationHierarchyException(string message) : base(message) { }
    public OrganizationHierarchyException(string message, Exception innerException) : base(message, innerException) { }
}
