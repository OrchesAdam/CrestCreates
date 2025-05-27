using System;

namespace CrestCreates.DependencyInjection
{
    /// <summary>
    /// 忽略自动服务注册
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class IgnoreServiceAttribute : Attribute
    {
    }
}

