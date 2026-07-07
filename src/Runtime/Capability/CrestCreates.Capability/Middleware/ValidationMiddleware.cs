using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;
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

    public ValidationMiddleware(
        ISchemaValidator? validator,
        ICapabilityRegistry capabilityRegistry,
        ISchemaRegistry? schemaRegistry = null)
    {
        _validator = validator;
        _capabilityRegistry = capabilityRegistry;
        _schemaRegistry = schemaRegistry;
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

        var schemaDescriptor = _schemaRegistry.GetById(inputSchema.Value.Id);
        if (schemaDescriptor == null)
            return next(context);

        var result = _validator.Validate(schemaDescriptor, context.Input);
        if (!result.IsValid)
        {
            var errorMessages = string.Join("; ", result.Errors.Select(e => e.Message));
            return Task.FromResult(CapabilityExecutionResult.Failure(
                "CAPABILITY_VALIDATION_FAILED",
                $"Input validation failed: {errorMessages}",
                TimeSpan.Zero));
        }

        return next(context);
    }
}
