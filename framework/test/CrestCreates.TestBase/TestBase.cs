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
    public abstract class TestBase
    {
        protected IFixture Fixture { get; }
        protected IServiceProvider ServiceProvider { get; }

        protected TestBase()
        {
            Fixture = new Fixture().Customize(new AutoMoqCustomization());
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

        protected T Create<T>()
        {
            return Fixture.Create<T>();
        }

        protected IEnumerable<T> CreateMany<T>(int count = 3)
        {
            return Fixture.CreateMany<T>(count);
        }

        public virtual void Dispose()
        {
            // 子类可以重写此方法来释放资源
        }
    }
}