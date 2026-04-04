using System;

namespace CrestCreates.Domain.Shared.Attributes
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class CrestServiceAttribute : Attribute
    {
        public enum ServiceLifetime
        {
            Scoped,
            Singleton,
            Transient
        }
        
        public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Scoped;
        public bool GenerateController { get; set; } = true;
        public string Route { get; set; } = "";
        
        // 授权配置
        public bool GenerateAuthorization { get; set; } = false;
        public string ResourceName { get; set; } = "";
        public bool GenerateCrudPermissions { get; set; } = true;
        public string[] DefaultRoles { get; set; } = null;
        public bool RequireAll { get; set; } = false;
        public bool RequireAuthorizationForAll { get; set; } = true;
    }
}
