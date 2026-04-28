using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using CrestCreates.Security.Services;
using CrestCreates.Security.Configuration;

namespace CrestCreates.Security.Modules;

/// <summary>
/// 安全模块
/// </summary>
[CrestModule]
public class SecurityModule : ModuleBase
{
    private readonly SecurityOptions _options;
    
    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="configuration">配置对象</param>
    public SecurityModule(IConfiguration configuration)
    {
        // 从配置系统读取安全配置
        _options = new SecurityOptions();
        configuration.GetSection("Security").Bind(_options);
    }
    
    /// <summary>
    /// 配置服务
    /// </summary>
    /// <param name="services">服务集合</param>
    public override void OnConfigureServices(IServiceCollection services)
    {
        base.OnConfigureServices(services);
        
        // 注册安全配置
        services.AddSingleton(_options);
        
        // 配置CSRF保护
        services.AddAntiforgery(options =>
        {
            options.HeaderName = _options.Csrf.HeaderName;
            options.Cookie.Name = _options.Csrf.CookieName;
            options.Cookie.HttpOnly = _options.Csrf.CookieHttpOnly;
            options.Cookie.SecurePolicy = _options.Csrf.CookieSecurePolicy;
        });
        
        // 配置HSTS
        services.AddHsts(options =>
        {
            options.Preload = _options.Hsts.Preload;
            options.IncludeSubDomains = _options.Hsts.IncludeSubDomains;
            options.MaxAge = _options.Hsts.MaxAge;
        });
        
        // 注册安全服务
        services.AddSingleton<ISecurityService, SecurityService>();
    }
    
    /// <summary>
    /// 应用程序初始化
    /// </summary>
    /// <param name="host">主机对象</param>
    public override void OnApplicationInitialization(Microsoft.Extensions.Hosting.IHost host)
    {
        base.OnApplicationInitialization(host);
        
        var app = host.Services.GetRequiredService<IApplicationBuilder>();
        
        // 添加安全头中间件
        app.Use(async (context, next) =>
        {
            var headers = _options.Headers;
            
            if (headers.EnableXContentTypeOptions)
            {
                context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
            }
            
            if (headers.EnableXFrameOptions)
            {
                context.Response.Headers.Append("X-Frame-Options", headers.XFrameOptionsValue);
            }
            
            if (headers.EnableXXssProtection)
            {
                context.Response.Headers.Append("X-XSS-Protection", headers.XXssProtectionValue);
            }
            
            if (headers.EnableReferrerPolicy)
            {
                context.Response.Headers.Append("Referrer-Policy", headers.ReferrerPolicyValue);
            }
            
            if (headers.EnablePermissionsPolicy)
            {
                context.Response.Headers.Append("Permissions-Policy", headers.PermissionsPolicyValue);
            }
            
            await next();
        });
        
        // 使用HTTPS重定向
        if (_options.EnableHttpsRedirection)
        {
            app.UseHttpsRedirection();
            app.UseHsts();
        }
    }
}