using System;
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
            // 构建主机并注册模块
            var hostBuilder = Host.CreateDefaultBuilder();
            
            // 调用生成的RegisterModules方法
            hostBuilder = AutoModuleRegistration.RegisterModules(hostBuilder);
            var host = hostBuilder.Build();

            // 验证模块是否已注册
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
            // 构建主机并注册模块
            var hostBuilder = Host.CreateDefaultBuilder();
            hostBuilder = AutoModuleRegistration.RegisterModules(hostBuilder);
            var host = hostBuilder.Build();

            // 初始化模块
            host = AutoModuleRegistration.InitializeModules(host);

            // 验证模块是否已初始化
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
            // 验证RegisteredModules属性是否存在
            var registeredModules = AutoModuleRegistration.RegisteredModules;
            Assert.NotNull(registeredModules);
            Assert.NotEmpty(registeredModules);

            // 验证所有测试模块都已注册
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
    }
}

