using Microsoft.AspNetCore.Http;
using System;

namespace CrestCreates.Security.Configuration
{
    /// <summary>
    /// 安全配置选项
    /// </summary>
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
    
    /// <summary>
    /// CSRF 配置选项
    /// </summary>
    public class CsrfOptions
    {
        /// <summary>
        /// CSRF 头部名称
        /// </summary>
        public string HeaderName { get; set; } = "X-CSRF-TOKEN";
        
        /// <summary>
        /// CSRF Cookie 名称
        /// </summary>
        public string CookieName { get; set; } = "XSRF-TOKEN";
        
        /// <summary>
        /// Cookie 是否为 HttpOnly
        /// </summary>
        public bool CookieHttpOnly { get; set; } = true;
        
        /// <summary>
        /// Cookie 安全策略
        /// </summary>
        public CookieSecurePolicy CookieSecurePolicy { get; set; } = CookieSecurePolicy.Always;
    }
    
    /// <summary>
    /// HSTS 配置选项
    /// </summary>
    public class HstsOptions
    {
        /// <summary>
        /// 是否预加载
        /// </summary>
        public bool Preload { get; set; } = true;
        
        /// <summary>
        /// 是否包含子域名
        /// </summary>
        public bool IncludeSubDomains { get; set; } = true;
        
        /// <summary>
        /// 最大年龄
        /// </summary>
        public TimeSpan MaxAge { get; set; } = TimeSpan.FromDays(365);
    }
    
    /// <summary>
    /// 安全头配置选项
    /// </summary>
    public class SecurityHeadersOptions
    {
        /// <summary>
        /// 是否启用 X-Content-Type-Options
        /// </summary>
        public bool EnableXContentTypeOptions { get; set; } = true;
        
        /// <summary>
        /// 是否启用 X-Frame-Options
        /// </summary>
        public bool EnableXFrameOptions { get; set; } = true;
        
        /// <summary>
        /// X-Frame-Options 值
        /// </summary>
        public string XFrameOptionsValue { get; set; } = "SAMEORIGIN";
        
        /// <summary>
        /// 是否启用 X-XSS-Protection
        /// </summary>
        public bool EnableXXssProtection { get; set; } = true;
        
        /// <summary>
        /// X-XSS-Protection 值
        /// </summary>
        public string XXssProtectionValue { get; set; } = "1; mode=block";
        
        /// <summary>
        /// 是否启用 Referrer-Policy
        /// </summary>
        public bool EnableReferrerPolicy { get; set; } = true;
        
        /// <summary>
        /// Referrer-Policy 值
        /// </summary>
        public string ReferrerPolicyValue { get; set; } = "strict-origin-when-cross-origin";
        
        /// <summary>
        /// 是否启用 Permissions-Policy
        /// </summary>
        public bool EnablePermissionsPolicy { get; set; } = true;
        
        /// <summary>
        /// Permissions-Policy 值
        /// </summary>
        public string PermissionsPolicyValue { get; set; } = "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";
    }
}