using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace CrestCreates.Domain.Tests
{
    public abstract class DomainTestBase
    {
        protected IServiceProvider ServiceProvider { get; }
        
        protected DomainTestBase()
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            ServiceProvider = services.BuildServiceProvider();
        }
        
        protected virtual void ConfigureServices(IServiceCollection services)
        {
            // 子类可以重写此方法来配置服务
        }
        
        protected T GetRequiredService<T>()
        {
            return ServiceProvider.GetRequiredService<T>();
        }
        
        protected Mock<T> RegisterMock<T>() where T : class
        {
            var mock = new Mock<T>();
            var services = new ServiceCollection();
            ConfigureServices(services);
            services.AddSingleton(mock.Object);
            return mock;
        }
    }
}
