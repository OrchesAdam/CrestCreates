using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using LibraryManagement.Domain.Modules;

namespace LibraryManagement.Application.Contracts.Modules;

[CrestModule(typeof(DomainModule), Order = -150)]
public class ApplicationContractsModule : ModuleBase
{
}
