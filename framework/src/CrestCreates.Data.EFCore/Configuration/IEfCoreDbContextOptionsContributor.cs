using System;
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.Data.EFCore.Configuration;

public interface IEfCoreDbContextOptionsContributor
{
    void Configure(IServiceProvider serviceProvider, DbContextOptionsBuilder optionsBuilder);
}
