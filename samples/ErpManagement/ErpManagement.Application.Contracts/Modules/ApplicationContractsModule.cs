using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using ErpManagement.Domain.Modules;

namespace ErpManagement.Application.Contracts.Modules;

[CrestModule(typeof(DomainModule), Order = -150)]
public class ApplicationContractsModule : ModuleBase
{
}