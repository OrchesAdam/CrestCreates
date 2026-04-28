using System;

namespace CrestCreates.Domain.Shared.Attributes
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
    public class MultiTenantAttribute : Attribute
    {
        public bool Required { get; set; } = true;
    }
}
