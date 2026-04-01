# SecurityModule 实现改进方案

## 当前实现分析

当前 `SecurityModule.cs` 的实现存在以下问题：

1. **配置硬编码**：所有安全配置都硬编码在模块中，不够灵活
2. **方法签名不正确**：没有使用 `ModuleBase` 提供的标准生命周期方法
3. **缺乏配置系统集成**：无法从外部配置文件读取配置
4. **缺少配置验证**：没有对配置进行验证和默认值处理
5. **结构不够清晰**：配置和实现混合在一起

## 改进方案

### 1. 创建安全配置类

首先创建一个配置类来管理安全相关的设置：

```csharp
using Microsoft.AspNetCore.Http;
using System;

namespace CrestCreates.Security.Configuration
{
    public class SecurityOptions
    {
        /// <summary>
        /// CSRF 配置
        /// </summary>
        public CsrfOptions Csrf { get; set; } = new CsrfOptions();
        
        /// <summary>
        /// HSTS 配置
        /// </summary>
        public HstsOptions Hsts { get; set; } = new HstsOptions();
        
        /// <summary>
        /// 安全头配置
        /// </summary>
        public SecurityHeadersOptions Headers { get; set; } = new SecurityHeadersOptions();
        
        /// <summary>
        /// 是否启用 HTTPS 重定向
        /// </summary>
        public bool EnableHttpsRedirection { get; set; } = true;
    }
    
    public class CsrfOptions
    {
        public string HeaderName { get; set; } = "X-CSRF-TOKEN";
        public string CookieName { get; set; } = "XSRF-TOKEN";
        public bool CookieHttpOnly { get; set; } = true;
        public CookieSecurePolicy CookieSecurePolicy { get; set; } = CookieSecurePolicy.Always;
    }
    
    public class HstsOptions
    {
        public bool Preload { get; set; } = true;
        public bool IncludeSubDomains { get; set; } = true;
        public TimeSpan MaxAge { get; set; } = TimeSpan.FromDays(365);
    }
    
    public class SecurityHeadersOptions
    {
        public bool EnableXContentTypeOptions { get; set; } = true;
        public bool EnableXFrameOptions { get; set; } = true;
        public string XFrameOptionsValue { get; set; } = "SAMEORIGIN";
        public bool EnableXXssProtection { get; set; } = true;
        public string XXssProtectionValue { get; set; } = "1; mode=block";
        public bool EnableReferrerPolicy { get; set; } = true;
        public string ReferrerPolicyValue { get; set; } = "strict-origin-when-cross-origin";
        public bool EnablePermissionsPolicy { get; set; } = true;
        public string PermissionsPolicyValue { get; set; } = "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";
    }
}
```

### 2. 改进 SecurityModule 实现

使用标准的 `ModuleBase` 生命周期方法，并集成配置系统：

```csharp
using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using CrestCreates.Security.Services;
using CrestCreates.Security.Configuration;

namespace CrestCreates.Security.Modules;

public class SecurityModule : ModuleBase
{
    private readonly SecurityOptions _options;
    
    public SecurityModule(IConfiguration configuration)
    {
        // 从配置系统读取安全配置
        _options = new SecurityOptions();
        configuration.GetSection("Security").Bind(_options);
    }
    
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
```

### 3. 配置文件示例

在 `appsettings.json` 中添加安全配置：

```json
{
  "Security": {
    "Csrf": {
      "HeaderName": "X-CSRF-TOKEN",
      "CookieName": "XSRF-TOKEN",
      "CookieHttpOnly": true,
      "CookieSecurePolicy": "Always"
    },
    "Hsts": {
      "Preload": true,
      "IncludeSubDomains": true,
      "MaxAge": "365.00:00:00"
    },
    "Headers": {
      "EnableXContentTypeOptions": true,
      "EnableXFrameOptions": true,
      "XFrameOptionsValue": "SAMEORIGIN",
      "EnableXXssProtection": true,
      "XXssProtectionValue": "1; mode=block",
      "EnableReferrerPolicy": true,
      "ReferrerPolicyValue": "strict-origin-when-cross-origin",
      "EnablePermissionsPolicy": true,
      "PermissionsPolicyValue": "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()"
    },
    "EnableHttpsRedirection": true
  }
}
```

## 改进的优势

1. **配置灵活性**：通过配置文件可以轻松调整安全设置，无需修改代码
2. **标准生命周期**：使用 `ModuleBase` 的标准方法，符合框架设计规范
3. **依赖注入**：配置通过依赖注入传递，便于测试和扩展
4. **默认值处理**：配置类提供了合理的默认值
5. **结构化配置**：配置按功能分组，结构清晰
6. **可维护性**：代码结构更清晰，易于维护和扩展

## 实现建议

1. **添加配置验证**：可以使用 FluentValidation 对配置进行验证
2. **环境特定配置**：支持不同环境的配置覆盖
3. **文档完善**：为配置选项添加详细的文档注释
4. **测试覆盖**：为安全模块添加单元测试

通过这种方式，SecurityModule 的实现会更加灵活、可维护，并且符合现代 .NET 应用的最佳实践。