namespace CrestCreates.Metadata;

public sealed class DescriptorRefValidatorValidationReport
{
    public bool IsValid => Errors.Count == 0;
    public List<string> Errors { get; init; } = new();
}
