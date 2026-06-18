using System;
using System.Threading.Tasks;
using CrestCreates.Aop.Abstractions.Interfaces;

namespace CrestCreates.Aop.Abstractions.Interfaces;

public interface IDynamicConfigurationProvider
{
    Task<T?> GetAsync<T>(string key);
    Task SetAsync<T>(string key, T value);
    Task InvalidateAsync(string key);
    Task<bool> ExistsAsync(string key);
}
