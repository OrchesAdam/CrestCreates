using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.GeneratedCrud.AotFixture.Domain;

namespace CrestCreates.GeneratedCrud.AotFixture.Feature.Widgets;

/// <summary>
/// #128 S4 fixture: the only authored CRUD input of this assembly. The DTOs, the service
/// interface/implementation, mappings, the typed query adapter, the HTTP projection and the JSON
/// root registration are materialized by the CRUD + JSON toolchain; this project must never carry a
/// hand-written equivalent.
/// </summary>
[GenerateCrudService(typeof(Widget), RoutePrefix = "/api/widgets")]
public partial class WidgetAppService
{
}
