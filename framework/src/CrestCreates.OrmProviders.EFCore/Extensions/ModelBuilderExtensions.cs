using CrestCreates.Domain.Shared.Entities.Auditing;
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.OrmProviders.EFCore.Extensions;

public static class ModelBuilderExtensions
{
    public static ModelBuilder ConfigureConcurrencyStamp(this ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(IHasConcurrencyStamp).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .Property(nameof(IHasConcurrencyStamp.ConcurrencyStamp))
                    .IsConcurrencyToken();
            }
        }
        return modelBuilder;
    }
}
