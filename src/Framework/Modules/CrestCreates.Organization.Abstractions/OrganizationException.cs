namespace CrestCreates.Organization.Abstractions;

public class OrganizationException : Exception
{
    public OrganizationException(string message) : base(message) { }
    public OrganizationException(string message, Exception innerException) : base(message, innerException) { }
}
