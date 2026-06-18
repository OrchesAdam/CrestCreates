using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoFixture;
using AutoFixture.AutoMoq;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace CrestCreates.TestBase
{
    public abstract class IntegrationTestBase : TestBase
    {
        protected IntegrationTestBase() : base()
        {
        }

        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            // 配置集成测试所需的服务
        }
    }
}