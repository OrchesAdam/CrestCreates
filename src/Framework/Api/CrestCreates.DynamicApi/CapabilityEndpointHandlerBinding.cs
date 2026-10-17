using System.ComponentModel;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.DynamicApi;

/// <summary>
/// Handler invoker contributed by one assembly, keyed by capability id.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record CapabilityEndpointHandlerBinding(
    string CapabilityId,
    ICapabilityHandlerInvoker Invoker);
