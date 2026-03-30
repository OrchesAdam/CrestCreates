using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using CrestCreates.TestBase;

namespace CrestCreates.Domain.Tests
{
    public abstract class DomainTestBase : CrestCreates.TestBase.DomainTestBase
    {
        protected DomainTestBase() : base()
        {
        }
        
        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            // 子类可以重写此方法来配置服务
        }
    }
}
