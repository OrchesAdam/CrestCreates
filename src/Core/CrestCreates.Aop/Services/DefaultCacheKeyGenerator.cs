using System.Linq;
using System.Reflection;
using CrestCreates.Aop.Abstractions.Interfaces;
using Rougamo.Context;

namespace CrestCreates.Aop.Services;

public class DefaultCacheKeyGenerator : ICacheKeyGenerator
{
    public string GenerateKey(string prefix, MethodInfo method, object?[] arguments)
    {
        var args = string.Join(":", arguments.Select(a => a?.ToString() ?? "null"));
        return $"{prefix}:{method.Name}:{args}";
    }
}
