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
    public abstract class DomainTestBase : TestBase
    {
        protected DomainTestBase() : base()
        {
        }

        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            // 配置领域服务测试所需的服务
        }
    }
}