using CrestCreates.CodeGenerator.Tests.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests
{
    public class ModuleGeneratorTests
    {
        [Fact]
        public void Should_Register_Module_Using_ServiceCollection_Extension()
        {
            var services = new ServiceCollection();

            services.AddCoreModule();

            using var provider = services.BuildServiceProvider();
            var coreModule = provider.GetService<CoreModule>();

            Assert.NotNull(coreModule);
        }

        [Fact]
        public void Should_Register_Module_Using_HostBuilder_Extension()
        {
            var hostBuilder = Host.CreateDefaultBuilder();

            hostBuilder.AddDatabaseModule();

            using var host = hostBuilder.Build();
            var databaseModule = host.Services.GetService<DatabaseModule>();

            Assert.NotNull(databaseModule);
        }

        [Fact]
        public void Should_Generate_Get_And_TryGet_Module_Extensions()
        {
            var services = new ServiceCollection();
            services.AddApplicationModule();

            using var provider = services.BuildServiceProvider();

            Assert.NotNull(provider.GetApplicationModule());
            Assert.NotNull(provider.TryGetApplicationModule());
        }

        [Fact]
        public void Should_Not_Generate_Global_Module_Auto_Initializer()
        {
            var generatedType = typeof(ModuleGeneratorTests).Assembly
                .GetType("CrestCreates.Modularity.ModuleAutoInitializer", throwOnError: false);

            Assert.Null(generatedType);
        }

        [Fact]
        public void Should_Not_Generate_Global_Module_Descriptor_Registry()
        {
            var generatedType = typeof(ModuleGeneratorTests).Assembly
                .GetType("CrestCreates.Modularity.ModuleDescriptorRegistry", throwOnError: false);

            Assert.Null(generatedType);
        }
    }
}
