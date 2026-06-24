using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorBinding;
using CrestCreates.Metadata.DescriptorBinding;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Form;

public sealed class FormBindingStatusContributor : IDescriptorBindingStatusContributor
{
    private readonly IFormRegistry _formRegistry;
    private readonly ISchemaRegistry _schemaRegistry;

    public FormBindingStatusContributor(IFormRegistry formRegistry, ISchemaRegistry schemaRegistry)
    {
        _formRegistry = formRegistry;
        _schemaRegistry = schemaRegistry;
    }

    public DescriptorKind SupportedKind => DescriptorKind.Form;
    public int Order => 20;

    public IReadOnlyList<IDescriptor> GetDescriptors()
    {
        return _formRegistry.GetAll().Cast<IDescriptor>().ToList();
    }

    public DescriptorBindingReport Evaluate(IDescriptor descriptor)
    {
        var form = (FormDescriptor)descriptor;
        var issues = new List<DescriptorBindingIssue>();
        var fullId = $"{form.Namespace}.{form.Id}";

        // Check schema version exists (Schema is non-nullable on FormDescriptor)
        var schema = _schemaRegistry.GetByVersion(form.Schema.Id, form.Schema.Version);
        if (schema == null)
        {
            issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "REF_MISSING_SCHEMA_VERSION",
                $"Schema '{form.Schema.Id}' v{form.Schema.Version} not found.",
                fullId, DescriptorKind.Form, "Schema"));
        }
        else
        {
            // Check all form fields exist in schema
            var schemaFieldNames = new HashSet<string>(schema.Fields.Select(f => f.Name));
            foreach (var field in form.Fields)
            {
                if (!schemaFieldNames.Contains(field.SchemaFieldName))
                {
                    issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "REF_MISSING_SCHEMA_FIELD",
                        $"Form field '{field.SchemaFieldName}' not found in schema '{form.Schema.Id}' v{form.Schema.Version}.",
                        fullId, DescriptorKind.Form, $"Fields.{field.SchemaFieldName}"));
                }
            }

            // Check required schema fields present in form (warning only)
            var formFieldNames = new HashSet<string>(form.Fields.Select(f => f.SchemaFieldName));
            foreach (var schemaField in schema.Fields.Where(f => f.IsRequired))
            {
                if (!formFieldNames.Contains(schemaField.Name))
                {
                    issues.Add(new DescriptorBindingIssue(ValidationSeverity.Warning, "BIND_MISSING_REQUIRED_FIELD",
                        $"Required schema field '{schemaField.Name}' is missing from form.",
                        fullId, DescriptorKind.Form, $"Fields.{schemaField.Name}"));
                }
            }
        }

        var status = BindingStatusSynthesizer.SynthesizeStatus(issues);
        return new DescriptorBindingReport
        {
            DescriptorId = fullId,
            DescriptorKind = DescriptorKind.Form,
            Status = status,
            Issues = issues
        };
    }
}
