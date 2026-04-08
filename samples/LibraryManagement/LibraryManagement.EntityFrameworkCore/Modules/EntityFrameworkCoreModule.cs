using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using CrestCreates.OrmProviders.Abstract;
using CrestCreates.OrmProviders.EFCore.DbContexts;
using CrestCreates.OrmProviders.EFCore.UnitOfWork;
using LibraryManagement.Application.Modules;
using LibraryManagement.Domain.Repositories;
using LibraryManagement.EntityFrameworkCore.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LibraryManagement.EntityFrameworkCore.Modules;

[CrestModule(typeof(ApplicationModule), Order = -50)]
public class EntityFrameworkCoreModule : ModuleBase
{
    
    public override void OnConfigureServices(IServiceCollection services)
    {
        // 注册 DbContext
        services.AddDbContext<LibraryDbContext>((serviceProvider, options) =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var connectionString = configuration.GetConnectionString("Default");
            options.UseSqlServer(connectionString);
        });

        services.AddUnitOfWork(OrmProvider.EfCore);
        services.AddScoped<EfCoreUnitOfWork>();
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<LibraryDbContext>());
        services.AddScoped<IEntityFrameworkCoreDbContext>(sp =>
            new EfCoreDbContextAdapter(sp.GetRequiredService<LibraryDbContext>()));
        services.AddScoped<IDataBaseContext>(sp =>
            sp.GetRequiredService<IEntityFrameworkCoreDbContext>());

        // 注册仓储
        services.AddScoped<IBookRepository, BookRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IMemberRepository, MemberRepository>();
        services.AddScoped<ILoanRepository, LoanRepository>();
    }
}
