using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Capability.Middleware;

/// <summary>
/// Validates capability input against the InputSchema declared on the CapabilityDescriptor.
/// Resolves the SchemaDescriptor from ISchemaRegistry, then delegates to ISchemaValidator.
/// If the validator or schema registry is not registered, passes through unchanged.
/// Returns CAPABILITY_VALIDATION_FAILED when validation errors are found.
/// </summary>
public sealed class ValidationMiddleware : ICapabilityPipelineMiddleware
{
    private readonly ISchemaValidator? _validator;
    private readonly ICapabilityRegistry _capabilityRegistry;
    private readonly ISchemaRegistry? _schemaRegistry;
    private readonly ICapabilityInputValidationPolicy _inputValidationPolicy;

    public ValidationMiddleware(
        ISchemaValidator? validator,
        ICapabilityRegistry capabilityRegistry,
        ISchemaRegistry? schemaRegistry = null,
        ICapabilityInputValidationPolicy? inputValidationPolicy = null)
    {
        _validator = validator;
        _capabilityRegistry = capabilityRegistry;
        _schemaRegistry = schemaRegistry;
        _inputValidationPolicy = inputValidationPolicy
            ?? AllowUnknownCapabilityInputPropertiesPolicy.Instance;
    }

    public Task<CapabilityExecutionResult> InvokeAsync(
        CapabilityExecutionContext context,
        CapabilityPipelineDelegate next)
    {
        if (_validator == null || _schemaRegistry == null)
            return next(context);

        var capDescriptor = _capabilityRegistry.GetByVersion(context.CapabilityId, context.CapabilityVersion);
        if (capDescriptor == null)
            return next(context);

        var inputSchema = capDescriptor.InputSchema;
        if (inputSchema == null)
            return next(context);

        if (inputSchema.Value.SelectionMode != VersionSelectionMode.Exact
            || inputSchema.Value.Version <= 0)
        {
            return Task.FromResult(CapabilityExecutionResult.Failure(
                "CAPABILITY_SCHEMA_REFERENCE_INVALID",
                "Capability input schema reference must use an exact positive version.",
                TimeSpan.Zero));
        }

        var schemaDescriptor = _schemaRegistry.GetByVersion(
            inputSchema.Value.Id,
            inputSchema.Value.Version);
        if (schemaDescriptor == null)
        {
            return Task.FromResult(CapabilityExecutionResult.Failure(
                "CAPABILITY_SCHEMA_NOT_FOUND",
                "Capability input schema could not be resolved.",
                TimeSpan.Zero));
        }

        var rejectUnknownProperties = _inputValidationPolicy.RejectUnknownProperties(
            capDescriptor,
            schemaDescriptor);
        var result = context.InputJson.HasValue
            ? _validator.Validate(
                schemaDescriptor,
                context.InputJson.Value,
                rejectUnknownProperties)
            : _validator.Validate(
                schemaDescriptor,
                context.Input,
                rejectUnknownProperties);
        if (!result.IsValid)
        {
            var errorMessages = string.Join("; ", result.Errors.Select(e => e.Message));
            var issues = result.Errors
                .Select(error => new CapabilityExecutionIssue(
                    error.ErrorCode.ToString(),
                    error.Message,
                    error.FieldName))
                .ToArray();
            return Task.FromResult(CapabilityExecutionResult.Failure(
                CapabilityExecutionErrorCodes.ValidationFailed,
                $"Input validation failed: {errorMessages}",
                TimeSpan.Zero,
                issues));
        }

        return next(context);
    }
}
