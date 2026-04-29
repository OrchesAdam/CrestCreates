using System;
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.OrmProviders.EFCore.Configuration;

public interface IEfCoreDbContextOptionsContributor
{
    void Configure(IServiceProvider serviceProvider, DbContextOptionsBuilder optionsBuilder);
}
