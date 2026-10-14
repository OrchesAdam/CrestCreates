using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Event;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.DeadLetter.EFCore;
using CrestCreates.EventBus.Local;
using CrestCreates.EventBus.Local.Channel;
using CrestCreates.Generated;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace CrestCreates.EventBus.Local.Tests.Integration;

/// <summary>
/// R1-LE-02：单例 recorder/executor/后台批次不得从根 provider 捕获 scoped 的 EF dead-letter store。
/// 这些用例以 ValidateScopes=true + AddScoped&lt;IDeadLetterStore, EfCoreDeadLetterStore&gt; 的真实装配，
/// 覆盖同步总线、人工重放与后台批次三条入口。
/// </summary>
public class LocalDeadLetterScopedStoreTests
{
    private static ServiceProvider BuildScopedEfComposition(
        Action<LocalEventDispatchRegistrationBuilder> declare,
        Action<IServiceCollection>? configure = null)
    {
        var root = new InMemoryDatabaseRoot();
        var databaseName = "scoped-dlq-" + Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEventValidator, PassThroughEventValidator>();
        services.AddDbContext<DeadLetterDbContext>(options => options
            .UseInMemoryDatabase(databaseName, root)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddScoped<IDeadLetterStore, EfCoreDeadLetterStore>();
        services.AddCrestLocalEventDispatch(declare);
        configure?.Invoke(services);

        LocalEventJsonCodecContributions.AddCodecs(services);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Fact]
    public async Task ScopedEfStore_SyncBus_RecordsFailure_UnderScopeValidation()
    {
        var control = new HandlerControl { ShouldFail = true };
        using var provider = BuildScopedEfComposition(
            declare: builder => builder.Declare<ReplayableFailingEvent>(replayable: true),
            configure: services =>
            {
                services.AddSingleton(control);
                services.AddScoped<ILocalEventHandler<ReplayableFailingEvent>, ReplayableFailingHandler>();
            });

        // 修复前：单例 recorder 在根 provider 上 GetRequiredService<IDeadLetterStore>() → ValidateScopes 抛
        // "Cannot resolve scoped service ... from root provider"，总线根本无法解析。
        using var scope = provider.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<ILocalEventBus>();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => bus.PublishAsync(new ReplayableFailingEvent { Message = "boom" }));
        failure.Message.Should().Contain("handler failure");

        using var verifyScope = provider.CreateScope();
        var store = verifyScope.ServiceProvider.GetRequiredService<IDeadLetterStore>();
        var pending = await store.GetPendingAsync(0, 10, CancellationToken.None);
        pending.Should().ContainSingle(message => message.EventName.Contains("ReplayableFailingEvent"));
    }

    [Fact]
    public void ScopedEfStore_IsResolvedPerScope_NotCapturedFromRoot()
    {
        using var provider = BuildScopedEfComposition(
            declare: builder => builder.Declare<ReplayableFailingEvent>(replayable: true));

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var firstStore = first.ServiceProvider.GetRequiredService<IDeadLetterStore>();
        var secondStore = second.ServiceProvider.GetRequiredService<IDeadLetterStore>();

        firstStore.Should().NotBeSameAs(secondStore);
    }

    [Fact]
    public async Task ScopedEfStore_ManualReplay_TransitionsToRetried()
    {
        var control = new HandlerControl { ShouldFail = false };
        using var provider = BuildScopedEfComposition(
            declare: builder => builder.Declare<ReplayableFailingEvent>(replayable: true),
            configure: services =>
            {
                services.AddSingleton(control);
                services.AddScoped<ILocalEventHandler<ReplayableFailingEvent>, ReplayableFailingHandler>();
            });

        var payload = LocalEventTestComposition.SerializeWithCodec(new ReplayableFailingEvent());
        var message = LocalEventTestComposition.CreateModernMessage<ReplayableFailingEvent>(
            payload, LocalEventIdentity.BuildLocalReplayKey(typeof(ReplayableFailingEvent)));

        using (var seedScope = provider.CreateScope())
        {
            await seedScope.ServiceProvider.GetRequiredService<IDeadLetterStore>()
                .EnqueueAsync(message, CancellationToken.None);
        }

        DeadLetterRetryResult result;
        using (var replayScope = provider.CreateScope())
        {
            var manager = replayScope.ServiceProvider.GetRequiredService<ILocalDeadLetterManager>();
            result = await manager.RetryAsync(message.MessageId);
        }

        result.Success.Should().BeTrue(result.ErrorMessage);

        using var verifyScope = provider.CreateScope();
        var store = verifyScope.ServiceProvider.GetRequiredService<IDeadLetterStore>();
        (await store.GetByIdAsync(message.MessageId, CancellationToken.None))!.Status
            .Should().Be(DeadLetterStatus.Retried);
        control.Calls.Should().Be(1);
    }

    [Fact]
    public async Task ScopedEfStore_SecondRetryOnSameManagerScope_RejectsAfterFirstSuccess()
    {
        var control = new HandlerControl { ShouldFail = false };
        using var provider = BuildScopedEfComposition(
            declare: builder => builder.Declare<ReplayableFailingEvent>(replayable: true),
            configure: services =>
            {
                services.AddSingleton(control);
                services.AddScoped<ILocalEventHandler<ReplayableFailingEvent>, ReplayableFailingHandler>();
            });

        var payload = LocalEventTestComposition.SerializeWithCodec(new ReplayableFailingEvent());
        var message = LocalEventTestComposition.CreateModernMessage<ReplayableFailingEvent>(
            payload, LocalEventIdentity.BuildLocalReplayKey(typeof(ReplayableFailingEvent)));

        using (var seedScope = provider.CreateScope())
        {
            await seedScope.ServiceProvider.GetRequiredService<IDeadLetterStore>()
                .EnqueueAsync(message, CancellationToken.None);
        }

        // First retry succeeds; handler runs once.
        DeadLetterRetryResult firstResult;
        using (var replayScope = provider.CreateScope())
        {
            var manager = replayScope.ServiceProvider.GetRequiredService<ILocalDeadLetterManager>();
            firstResult = await manager.RetryAsync(message.MessageId);

            // Second retry on the same manager scope must reject; handler must not run again.
            var secondResult = await manager.RetryAsync(message.MessageId);
            secondResult.Success.Should().BeFalse("message is already Retried; second attempt must be rejected");
        }

        firstResult.Success.Should().BeTrue(firstResult.ErrorMessage);
        control.Calls.Should().Be(1, "handler must execute exactly once across both attempts");

        using var verifyScope = provider.CreateScope();
        var store = verifyScope.ServiceProvider.GetRequiredService<IDeadLetterStore>();
        (await store.GetByIdAsync(message.MessageId, CancellationToken.None))!.Status
            .Should().Be(DeadLetterStatus.Retried);
    }

    [Fact]
    public async Task ScopedEfStore_BackgroundBatch_TransitionsToRetried()
    {
        var control = new HandlerControl { ShouldFail = false };
        using var provider = BuildScopedEfComposition(
            declare: builder => builder.Declare<ReplayableFailingEvent>(replayable: true),
            configure: services =>
            {
                services.AddSingleton(control);
                services.AddScoped<ILocalEventHandler<ReplayableFailingEvent>, ReplayableFailingHandler>();
                services.Configure<LocalDeadLetterOptions>(options => options.RetryIntervalSeconds = 1);
            });

        var payload = LocalEventTestComposition.SerializeWithCodec(new ReplayableFailingEvent());
        var message = LocalEventTestComposition.CreateModernMessage<ReplayableFailingEvent>(
            payload, LocalEventIdentity.BuildLocalReplayKey(typeof(ReplayableFailingEvent)));
        using (var seedScope = provider.CreateScope())
        {
            await seedScope.ServiceProvider.GetRequiredService<IDeadLetterStore>()
                .EnqueueAsync(message, CancellationToken.None);
        }

        var service = new LocalDeadLetterBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ILocalDeadLetterReplayExecutor>(),
            provider.GetRequiredService<IOptions<LocalDeadLetterOptions>>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LocalDeadLetterBackgroundService>>());

        await service.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            DeadLetterStatus status = DeadLetterStatus.Pending;
            while (DateTime.UtcNow < deadline)
            {
                using var scope = provider.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IDeadLetterStore>();
                status = (await store.GetByIdAsync(message.MessageId, CancellationToken.None))!.Status;
                if (status == DeadLetterStatus.Retried)
                {
                    break;
                }

                await Task.Delay(100);
            }

            status.Should().Be(DeadLetterStatus.Retried);
            control.Calls.Should().Be(1);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }
}
