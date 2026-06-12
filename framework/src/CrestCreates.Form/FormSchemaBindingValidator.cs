using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Form;

public sealed class FormSchemaBindingValidator
{
    public ValidationReport Validate(
        IReadOnlyList<FormDescriptor> forms,
        ISchemaRegistry schemaRegistry)
    {
        var issues = new List<ValidationIssue>();

        foreach (var form in forms)
        {
            ValidateForm(form, schemaRegistry, issues);
        }

        return new ValidationReport(issues);
    }

    private static void ValidateForm(
        FormDescriptor form,
        ISchemaRegistry schemaRegistry,
        List<ValidationIssue> issues)
    {
        string ctx = $"Form '{form.Name}' (Id={form.Id}, v{form.Version})";

        // Use GetByVersion to validate against the requested version, NOT latest.
        var schema = schemaRegistry.GetByVersion(form.Schema.Id, form.Schema.Version);

        if (schema == null)
        {
            // Check if ANY version exists for this Id
            var latest = schemaRegistry.GetById(form.Schema.Id);
            if (latest != null)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error,
                    $"{ctx}: Schema '{form.Schema.Id}' v{form.Schema.Version} not found. " +
                    $"Latest version is v{latest.Version}."));
            }
            else
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error,
                    $"{ctx}: Schema '{form.Schema.Id}' not found in registry."));
            }
            return;
        }

        var schemaFieldNames = new HashSet<string>(
            schema.Fields.Select(f => f.Name), StringComparer.Ordinal);

        foreach (var field in form.Fields)
        {
            if (!schemaFieldNames.Contains(field.SchemaFieldName))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error,
                    $"{ctx}: Field '{field.SchemaFieldName}' not found in " +
                    $"Schema '{schema.Name}' v{schema.Version} Fields."));
            }
        }

        // Warn on Schema required fields not covered by Form
        foreach (var schemaField in schema.Fields.Where(f => f.IsRequired))
        {
            if (!form.Fields.Any(ff =>
                string.Equals(ff.SchemaFieldName, schemaField.Name, StringComparison.Ordinal)))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Warning,
                    $"{ctx}: Schema required field '{schemaField.Name}' is not present " +
                    $"in Form fields."));
            }
        }
    }
}
