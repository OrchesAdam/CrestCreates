using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.Domain.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.Web;

/// <summary>
/// Fails the host before serving when the final service graph carries duplicate EF Core provider
/// bindings, or when a host registered the unit-of-work services without any provider binding.
/// The registry itself is the #137 single-owner authority; this validator only forces it to build
/// at startup instead of at the first request.
/// </summary>
internal sealed class UnitOfWorkCompositionValidator(IServiceProvider serviceProvider) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var serviceProbe = serviceProvider.GetService<IServiceProviderIsService>();
        if (serviceProbe?.IsService(typeof(IUnitOfWorkManager)) != true)
        {
            return Task.CompletedTask;
        }

        // Duplicate provider bindings throw while the registry index is built.
        var registry = serviceProvider.GetRequiredService<UnitOfWorkProviderBindingRegistry>();
        if (!registry.Bindings.Any())
        {
            throw new InvalidOperationException(
                "CCHTTP007: the host registered unit-of-work services but no ORM provider binding. "
                + "Register exactly one provider binding from the persistence module before startup.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
