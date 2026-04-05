using System.Reflection;

namespace CrestCreates.Aop.Abstractions.Interfaces;

public interface ICacheKeyGenerator
{
    string GenerateKey(string prefix, MethodInfo method, object?[] arguments);
}
