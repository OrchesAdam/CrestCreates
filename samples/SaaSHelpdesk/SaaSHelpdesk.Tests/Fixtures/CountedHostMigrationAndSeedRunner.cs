using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.EFCore;
using Microsoft.Extensions.Logging;

namespace SaaSHelpdesk.Tests.Fixtures;

/// <summary>
/// Decorator for <see cref="HostMigrationAndSeedRunner"/> that increments
/// <see cref="MigrationSeedCallCounter"/> each time <c>RunAsync</c> is called.
/// Registered via <c>services.RemoveAll&lt;HostMigrationAndSeedRunner&gt;()</c>
/// followed by <c>services.AddSingleton&lt;HostMigrationAndSeedRunner&gt;(...)</c>
/// in <see cref="HelpdeskWebApplicationFactory.ConfigureTestServices"/>.
/// </summary>
internal sealed class CountedHostMigrationAndSeedRunner : HostMigrationAndSeedRunner
{
    private readonly MigrationSeedCallCounter _counter;

    public CountedHostMigrationAndSeedRunner(
        IEnumerable<Type> dbContextTypes,
        ILogger<HostMigrationAndSeedRunner> logger,
        MigrationSeedCallCounter counter)
        : base(dbContextTypes, logger)
    {
        _counter = counter;
    }

    public override async Task RunAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        _counter.Increment();
        await base.RunAsync(serviceProvider, cancellationToken);
    }
}
