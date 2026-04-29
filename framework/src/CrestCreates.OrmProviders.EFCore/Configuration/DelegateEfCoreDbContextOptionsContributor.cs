using System;
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.OrmProviders.EFCore.Configuration;

public sealed class DelegateEfCoreDbContextOptionsContributor : IEfCoreDbContextOptionsContributor
{
    private readonly Action<IServiceProvider, DbContextOptionsBuilder> _configure;

    public DelegateEfCoreDbContextOptionsContributor(Action<DbContextOptionsBuilder> configure)
        : this((_, optionsBuilder) => configure(optionsBuilder))
    {
    }

    public DelegateEfCoreDbContextOptionsContributor(Action<IServiceProvider, DbContextOptionsBuilder> configure)
    {
        _configure = configure ?? throw new ArgumentNullException(nameof(configure));
    }

    public void Configure(IServiceProvider serviceProvider, DbContextOptionsBuilder optionsBuilder)
    {
        _configure(serviceProvider, optionsBuilder);
    }
}
