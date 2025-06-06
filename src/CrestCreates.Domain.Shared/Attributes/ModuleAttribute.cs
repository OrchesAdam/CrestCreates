using System;

namespace CrestCreates.Domain.Shared.Attributes
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class ModuleAttribute : Attribute
    {
        public string[] DependsOn { get; set; } = Array.Empty<string>();
        public bool AutoRegisterServices { get; set; } = true;
    }
}
