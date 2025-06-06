using Volo.Abp.Modularity;

[DependsOn(
    typeof(CrestCreatesDomainModule),
    typeof(CrestCreatesApplicationModule),
    typeof(CrestCreatesInfrastructureModule)
)]
public class ModuleB : AbpModule
{
    public override void PreInitialize()
    {
        // Initialization logic before the module is initialized
    }

    public override void Initialize()
    {
        IocManager.RegisterAssemblyByConvention(typeof(ModuleB).GetAssembly());
    }

    public override void PostInitialize()
    {
        // Logic after the module is initialized
    }
}