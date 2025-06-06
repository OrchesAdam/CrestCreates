using System;

namespace CrestCreates.Domain.Shared.Attributes
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class ServiceAttribute : Attribute
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
    }
}
