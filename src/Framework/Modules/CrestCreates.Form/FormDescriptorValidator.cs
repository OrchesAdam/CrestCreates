using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Registry;

namespace CrestCreates.Form;

public sealed class FormDescriptorValidator : IRegistryValidator<FormDescriptor>
{
    public int Order => 10;

    public ValidationReport Validate(IReadOnlyList<FormDescriptor> descriptors)
    {
        var issues = new List<ValidationIssue>();

        foreach (var descriptor in descriptors)
        {
            ValidateDescriptor(descriptor, issues);
        }

        return new ValidationReport(issues);
    }

    private static void ValidateDescriptor(FormDescriptor d, List<ValidationIssue> issues)
    {
        string ctx = $"Form '{d.Name}' (Id={d.Id}, v{d.Version})";

        // Rule 1: Id non-whitespace
        if (string.IsNullOrWhiteSpace(d.Id))
            issues.Add(new ValidationIssue(SeverityLevel.Error,
                $"{ctx}: Id must not be null or whitespace."));

        // Rule 2: Name non-whitespace
        if (string.IsNullOrWhiteSpace(d.Name))
            issues.Add(new ValidationIssue(SeverityLevel.Error,
                $"{ctx}: Name must not be null or whitespace."));

        // Rule 3: Version > 0
        if (d.Version <= 0)
            issues.Add(new ValidationIssue(SeverityLevel.Error,
                $"{ctx}: Version must be positive (was {d.Version})."));

        // Rule 4: Schema ref valid
        if (string.IsNullOrWhiteSpace(d.Schema.Id))
            issues.Add(new ValidationIssue(SeverityLevel.Error,
                $"{ctx}: Schema.Id must not be null or whitespace."));
        if (d.Schema.Version <= 0)
            issues.Add(new ValidationIssue(SeverityLevel.Error,
                $"{ctx}: Schema.Version must be positive (was {d.Schema.Version})."));

        // Rule 5: Fields not null
        if (d.Fields == null)
        {
            issues.Add(new ValidationIssue(SeverityLevel.Error,
                $"{ctx}: Fields must not be null. Use Array.Empty<FormFieldDescriptor>()."));
            return;
        }

        var seenFieldNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var field in d.Fields)
        {
            string fctx = $"{ctx}.Field '{field.SchemaFieldName}'";

            // Rule 6: SchemaFieldName non-whitespace
            if (string.IsNullOrWhiteSpace(field.SchemaFieldName))
                issues.Add(new ValidationIssue(SeverityLevel.Error,
                    $"{ctx}: Field has null or whitespace SchemaFieldName."));

            // Rule 7: Duplicate SchemaFieldName
            if (!string.IsNullOrWhiteSpace(field.SchemaFieldName) &&
                !seenFieldNames.Add(field.SchemaFieldName))
                issues.Add(new ValidationIssue(SeverityLevel.Error,
                    $"{ctx}: Duplicate SchemaFieldName '{field.SchemaFieldName}'."));

            // Rule 8: ControlType not whitespace-only (null is OK, whitespace is not)
            if (field.ControlType != null && string.IsNullOrWhiteSpace(field.ControlType))
                issues.Add(new ValidationIssue(SeverityLevel.Error,
                    $"{fctx}: ControlType is whitespace-only. Set null or non-empty value."));
        }

        // Rule 9: Duplicate Order is allowed — no validation.
    }
}
