using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using SaaSHelpdesk.Domain.Modules;

namespace SaaSHelpdesk.Application.Contracts.Modules;

[CrestModule(typeof(DomainModule), Order = -150)]
public class ApplicationContractsModule : ModuleBase
{
}
