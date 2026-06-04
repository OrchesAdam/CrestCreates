using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Tenants;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Data.EFCore.DatabaseProviders.SqlServer;

namespace CrestCreates.Data.EFCore.MultiTenancy
{
    [Obsolete("Use SqlServerTenantDatabaseProvisioner instead.")]
    public class EfCoreTenantDatabaseInitializer : ITenantDatabaseInitializer
    {
        private readonly SqlServerTenantDatabaseProvisioner _provisioner;

        public EfCoreTenantDatabaseInitializer(SqlServerTenantDatabaseProvisioner provisioner)
        {
            _provisioner = provisioner;
        }

        public async Task<TenantDatabaseInitializeResult> InitializeAsync(
            TenantInitializationContext context,
            CancellationToken cancellationToken = default)
        {
            return await _provisioner.InitializeAsync(context, cancellationToken);
        }
    }
}
