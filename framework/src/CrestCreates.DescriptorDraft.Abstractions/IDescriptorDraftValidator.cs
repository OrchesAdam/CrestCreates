namespace CrestCreates.DescriptorDraft.Abstractions;

public interface IDescriptorDraftValidator
{
    DescriptorDraftValidationResult Validate(DescriptorDraft draft);
}
