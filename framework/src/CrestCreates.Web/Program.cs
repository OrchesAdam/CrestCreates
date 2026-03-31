using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using CrestCreates.Modularity;

namespace CrestCreates.Web
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().InitializeModules().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .RegisterModules() // 注册模块
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}
