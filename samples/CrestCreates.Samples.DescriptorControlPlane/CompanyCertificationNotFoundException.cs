namespace CrestCreates.Samples.DescriptorControlPlane;

public sealed class CompanyCertificationNotFoundException : KeyNotFoundException
{
    public CompanyCertificationNotFoundException(Guid certificationId)
        : base($"Company certification '{certificationId}' was not found.")
    {
        CertificationId = certificationId;
    }

    public Guid CertificationId { get; }
}
