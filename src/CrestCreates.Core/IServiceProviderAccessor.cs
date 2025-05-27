using System;

namespace CrestCreates;

public interface IServiceProviderAccessor
{
    IServiceProvider ServiceProvider { get; }
}