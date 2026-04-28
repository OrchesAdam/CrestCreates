using System;

namespace CrestCreates.Domain.Shared.Attributes
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class DynamicApiIgnoreAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class DynamicApiRouteAttribute : Attribute
    {
        public DynamicApiRouteAttribute(string template)
        {
            Template = template;
        }

        public string Template { get; }
    }
}
