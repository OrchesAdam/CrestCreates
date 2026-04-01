using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CrestCreates.Infrastructure.UnitOfWork;

namespace CrestCreates.OrmConfiguration.Test
{
    public class Startup
    {
        public IConfiguration Configuration { get; }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            // 方法1: 通过代码配置 ORM
            services.ConfigureOrm(options =>
            {
                options.DefaultProvider = OrmProvider.EfCore;
                options.ConnectionString = "InMemoryConnection";
            });

            // 方法2: 从配置文件加载 ORM 配置
            // services.ConfigureOrm(Configuration);

            // 注册工作单元服务
            services.AddUnitOfWork();

            // 其他服务注册...
            services.AddControllers();
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}