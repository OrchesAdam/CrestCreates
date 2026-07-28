namespace CrestCreates.Sample.Procurement.Domain.Exceptions;

public class InvalidProcurementRequestException : Exception
{
    public InvalidProcurementRequestException(string message) : base(message) { }
}
