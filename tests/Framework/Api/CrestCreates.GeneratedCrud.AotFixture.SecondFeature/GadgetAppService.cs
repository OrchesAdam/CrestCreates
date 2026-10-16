using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.GeneratedCrud.AotFixture.Domain;

namespace CrestCreates.GeneratedCrud.AotFixture.SecondFeature.Gadgets;

/// <summary>
/// #128 R1 dual-feature fixture: the second generated Feature assembly. Its only authored input is
/// this declaration; the DTOs, service, projection, endpoints and JSON roots are materialized by the
/// same toolchain as the main Feature, so the dual-order acceptance activates two independently
/// generated assemblies.
/// </summary>
[GenerateCrudService(typeof(Gadget), RoutePrefix = "/api/gadgets")]
public partial class GadgetAppService
{
}
