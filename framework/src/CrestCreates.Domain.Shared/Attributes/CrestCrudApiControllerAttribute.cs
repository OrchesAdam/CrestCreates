using System;

namespace CrestCreates.Domain.Shared.Attributes
{
    /// <summary>
    /// Marks a CRUD application service contract that should be exposed by a generated MVC controller.
    /// </summary>
    [AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
    public sealed class CrestCrudApiControllerAttribute : Attribute
    {
        public CrestCrudApiControllerAttribute(string controllerName, string route)
        {
            ControllerName = controllerName;
            Route = route;
        }

        public string ControllerName { get; }

        public string Route { get; }
    }
}
