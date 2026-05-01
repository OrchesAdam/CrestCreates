using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using CrestCreates.Modularity;
using CrestCreates.CodeGenerator.Tests.Modules;

namespace CrestCreates.CodeGenerator.Tests
{
    public class ModuleGeneratorTests
    {
        [Fact]
        public void Should_Register_Modules_Using_Source_Generator()
        {
            var hostBuilder = Host.CreateDefaultBuilder();
            hostBuilder = ModuleAutoInitializer.RegisterModules(hostBuilder);
            var host = hostBuilder.Build();

            var coreModule = host.Services.GetService<CoreModule>();
            var databaseModule = host.Services.GetService<DatabaseModule>();
            var applicationModule = host.Services.GetService<ApplicationModule>();

            Assert.NotNull(coreModule);
            Assert.NotNull(databaseModule);
            Assert.NotNull(applicationModule);
        }

        [Fact]
        public void Should_Initialize_Modules_Using_Source_Generator()
        {
            var hostBuilder = Host.CreateDefaultBuilder();
            hostBuilder = ModuleAutoInitializer.RegisterModules(hostBuilder);
            hostBuilder.ConfigureServices((context, services) =>
            {
                // 注册 IServiceCollection 以支持 InitializeModules 中生成的代码
                services.AddSingleton<IServiceCollection>(services);
            });
            var host = hostBuilder.Build();
            host = ModuleAutoInitializer.InitializeModules(host);

            var coreModule = host.Services.GetService<CoreModule>();
            var databaseModule = host.Services.GetService<DatabaseModule>();
            var applicationModule = host.Services.GetService<ApplicationModule>();

            Assert.NotNull(coreModule);
            Assert.NotNull(databaseModule);
            Assert.NotNull(applicationModule);
        }

        [Fact]
        public void Should_Have_RegisteredModules_Property()
        {
            var registeredModules = ModuleAutoInitializer.RegisteredModules;
            Assert.NotNull(registeredModules);
            Assert.NotEmpty(registeredModules);

            var expectedModules = new[]
            {
                "CrestCreates.CodeGenerator.Tests.Modules.CoreModule",
                "CrestCreates.CodeGenerator.Tests.Modules.DatabaseModule",
                "CrestCreates.CodeGenerator.Tests.Modules.ApplicationModule"
            };

            foreach (var expectedModule in expectedModules)
            {
                Assert.Contains(expectedModule, registeredModules);
            }
        }

        [Fact]
        public void Should_Sort_Modules_By_Dependency()
        {
            // 触发 ModuleAutoInitializer 静态构造函数以确保模块已注册
            var _ = ModuleAutoInitializer.RegisteredModules;

            var descriptors = ModuleDescriptorRegistry.GetDescriptors();
            Assert.NotNull(descriptors);
            Assert.Equal(3, descriptors.Count);

            var coreIndex = FindIndex(descriptors, "CoreModule");
            var databaseIndex = FindIndex(descriptors, "DatabaseModule");
            var applicationIndex = FindIndex(descriptors, "ApplicationModule");

            Assert.True(coreIndex < databaseIndex, "CoreModule should be before DatabaseModule");
            Assert.True(databaseIndex < applicationIndex, "DatabaseModule should be before ApplicationModule");
        }

        private static int FindIndex(IReadOnlyList<ModuleDescriptor> descriptors, string moduleName)
        {
            for (int i = 0; i < descriptors.Count; i++)
            {
                if (descriptors[i].ModuleType.Name == moduleName)
                    return i;
            }
            return -1;
        }
    }
}