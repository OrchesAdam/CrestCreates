using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace CrestCreates.TestBase
{
    public abstract class ApiTestBase<TStartup> : IntegrationTestBase where TStartup : class
    {
        protected HttpClient Client { get; }
        protected WebApplicationFactory<TStartup> Factory { get; }

        protected ApiTestBase() : base()
        {
            Factory = new WebApplicationFactory<TStartup>()
                .WithWebHostBuilder(builder =>
                {
                    builder.ConfigureServices(services =>
                    {
                        ConfigureTestServices(services);
                    });
                });

            Client = Factory.CreateClient();
        }

        protected virtual void ConfigureTestServices(IServiceCollection services)
        {
            // 子类可以重写此方法来配置测试服务
        }

        protected async Task<HttpResponseMessage> GetAsync(string url)
        {
            return await Client.GetAsync(url);
        }

        protected async Task<HttpResponseMessage> PostAsync<T>(string url, T content)
        {
            var jsonContent = JsonConvert.SerializeObject(content);
            var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            return await Client.PostAsync(url, httpContent);
        }

        protected async Task<HttpResponseMessage> PutAsync<T>(string url, T content)
        {
            var jsonContent = JsonConvert.SerializeObject(content);
            var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            return await Client.PutAsync(url, httpContent);
        }

        protected async Task<HttpResponseMessage> DeleteAsync(string url)
        {
            return await Client.DeleteAsync(url);
        }

        protected async Task<T> ReadAsJsonAsync<T>(HttpResponseMessage response)
        {
            var content = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<T>(content);
        }

        protected void SetAuthorizationHeader(string token)
        {
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        protected void ClearAuthorizationHeader()
        {
            Client.DefaultRequestHeaders.Authorization = null;
        }

        public override void Dispose()
        {
            Client.Dispose();
            Factory.Dispose();
            base.Dispose();
        }
    }
}