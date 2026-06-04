using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.DynamicApi.Modules;
using CrestCreates.Modularity;

namespace CrestCreates.OpenApi.Modules;

[CrestModule(typeof(DynamicApiModule))]
public sealed class OpenApiModule : ModuleBase;