using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using CrestCreates.MultiTenancy;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.Extensions.Options;
using Quartz.Impl;
using Quartz.Listener;
using Quartz;
using JobStatus = CrestCreates.Scheduling.Services.JobStatus;
using IJob = CrestCreates.Scheduling.Jobs.IJob;
using System.Threading.Channels;
using CrestCreates.Scheduling.Jobs;
using CrestCreates.Scheduling.Services;
using CrestCreates.Scheduling.Quartz.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace CrestCreates.Scheduling.IntegrationTests;

public class QuartzSchedulerIntegrationTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(8);
    private const string Cron = "0 0 0 1 1 ? 2099";
    private static async Task<T> Read<T>(Channel<T> channel) => await channel.Reader.ReadAsync().AsTask().WaitAsync(Limit);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task TypedArgs_WithOrWithoutHistory_ImmediateAndDelayed(bool history, bool delayed)
    {
        await using var host = new Harness(history, tenants: true);
        var args = new Payload("typed value", 42, Guid.NewGuid());
        var constructedBeforeDecode = Payload.Constructed;
        var tenant = Guid.NewGuid(); var org = Guid.NewGuid(); var user = Guid.NewGuid();
        var id = delayed
            ? await host.Scheduler.ScheduleAsync<PayloadJob, Payload>(TimeSpan.FromMilliseconds(30), args, tenant, org, user)
            : await host.Scheduler.ExecuteNowAsync<PayloadJob, Payload>(args, tenant, org, user);
        var received = await Read(host.Probe.Payloads);
        Assert.Equal(args, received.Args);
        Assert.Equal(constructedBeforeDecode + 1, Payload.Constructed);
        Assert.Equal(id, received.JobId);
        Assert.Equal(tenant, received.TenantId); Assert.Equal(org, received.OrganizationId); Assert.Equal(user, received.UserId);
        await Read(host.Probe.Releases);
        Assert.Equal(1, host.Probe.Created); Assert.Equal(1, host.Probe.Released);
        if (history)
        {
            Assert.Contains(host.Probe.Records, r => r.Result == JobExecutionResult.Succeeded && r.JobUuid == id.Uuid);
            Assert.All(host.Probe.Records, r => Assert.Equal("typed value", System.Text.Json.JsonSerializer.Deserialize(r.ArgsJson!, SchedulingJsonContext.Default.Payload)!.Text));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NamedCron_TypedArgs_AndQueryTypes(bool history)
    {
        await using var host = new Harness(history);
        var native = await host.Native();
        var args = new Payload("cron", 7, Guid.NewGuid());
        var id = await host.Scheduler.RegisterAsync<PayloadJob, Payload>(new JobMetadata { Name = "named-cron", CronExpression = Cron }, args);
        await native.TriggerJob(new JobKey(id.Name!, id.Group!));
        var received = await Read(host.Probe.Payloads);
        await Read(host.Probe.Releases);
        Assert.Equal(args, received.Args); Assert.Equal(id, received.JobId);
        var info = Assert.Single(await host.Scheduler.GetAllAsync());
        Assert.Equal(id, info.Id); Assert.Equal(typeof(PayloadJob), info.JobType); Assert.Equal(typeof(Payload), info.ArgType);
        Assert.Equal(Cron, info.CronExpression); Assert.NotNull(info.NextFireTime);
        var cron = Assert.IsAssignableFrom<ICronTrigger>(Assert.Single(await native.GetTriggersOfJob(new(id.Name!, id.Group!))));
        Assert.Equal(MisfireInstruction.CronTrigger.FireOnceNow, cron.MisfireInstruction);
    }

    [Fact]
    public async Task MissingCodec_AndParameterizedLegacyOverload_RefuseBeforeScheduling()
    {
        await using var host = new Harness(codec: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Scheduler.ExecuteNowAsync<PayloadJob, Payload>(new("missing", 1, Guid.NewGuid())));
        await Assert.ThrowsAsync<ArgumentException>(() => host.Scheduler.RegisterAsync<PayloadJob, Payload>(new JobMetadata { Name = "invalid" }));
        Assert.Empty(await host.Scheduler.GetAllAsync()); Assert.Equal(0, host.Probe.Created);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task RetryBudget_StopsAndPreservesCron_EachFireRestartsAtOne(bool history, bool perJob)
    {
        await using var host = new Harness(history, retry: perJob ? 0 : 2);
        host.Probe.Action = _ => throw new InvalidOperationException("business failed");
        var native = await host.Native();
        var id = await host.Scheduler.RegisterAsync<ProbeJob>(new JobMetadata
        {
            Name = "retry-cron", CronExpression = Cron,
            Retry = perJob ? new JobRetryOptions { MaxRetries = 2, InitialDelay = TimeSpan.FromMilliseconds(20) } : null
        });
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await native.TriggerJob(new(id.Name!, id.Group!));
            DateTimeOffset? occurrence = null;
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                var observation = await Read(host.Probe.Completed);
                var execution = await Read(host.Probe.Executions);
                occurrence ??= execution.ScheduledAt;
                Assert.Equal(occurrence.Value, execution.ScheduledAt);
                Assert.Equal(attempt, observation.Trigger.JobDataMap.ContainsKey("AttemptNumber") ? observation.Trigger.JobDataMap.GetInt("AttemptNumber") : 1);
                Assert.Equal(id.Uuid.ToString(), observation.JobDetail.JobDataMap.GetString("JobUuid"));
                await Read(host.Probe.Releases);
            }
            var future = (await native.GetTriggersOfJob(new(id.Name!, id.Group!))).Where(t => t.GetNextFireTimeUtc() > DateTimeOffset.UtcNow).ToArray();
            Assert.Single(future); Assert.IsAssignableFrom<ICronTrigger>(future[0]);
        }
        Assert.Equal(6, host.Probe.Created); Assert.Equal(6, host.Probe.Released);
        if (history) Assert.Equal(new[] { 1, 2, 3, 1, 2, 3 }, host.Probe.Records.Where(r => r.Result == JobExecutionResult.Failed).Select(r => r.AttemptNumber));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NamedCancellation_AndTimeout_ReachBusinessTokenWithoutRetry(bool timeout)
    {
        await using var host = new Harness(history: true, retry: 2);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Probe.Action = async c =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, c.CancellationToken); }
            catch (OperationCanceledException) { canceled.TrySetResult(); throw; }
        };
        var native = await host.Native();
        var id = await host.Scheduler.RegisterAsync<ProbeJob>(new JobMetadata
        {
            Name = "cancel-named", CronExpression = Cron, Timeout = timeout ? TimeSpan.FromMilliseconds(80) : null
        });
        await native.TriggerJob(new(id.Name!, id.Group!));
        var execution = await Read(host.Probe.Executions);
        if (!timeout) await host.Scheduler.CancelAsync(id);
        await canceled.Task.WaitAsync(Limit);
        await Read(host.Probe.Releases);
        Assert.True(execution.CancellationToken.IsCancellationRequested);
        Assert.Equal(1, host.Probe.Created); Assert.Equal(1, host.Probe.Released);
        Assert.Single(host.Probe.Records, r => r.Result == JobExecutionResult.Cancelled);
        Assert.Single(await native.GetTriggersOfJob(new(id.Name!, id.Group!)), t => t.GetNextFireTimeUtc() > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task DisabledImmediateJob_IsPausedUntilResume_ControlsUseNamedKey()
    {
        await using var host = new Harness();
        var native = await host.Native();
        var id = await host.Scheduler.RegisterAsync<ProbeJob>(new JobMetadata { Name = "disabled", Enabled = false });
        var info = Assert.Single(await host.Scheduler.GetAllAsync(JobStatus.Paused));
        Assert.Equal(id, info.Id); Assert.Equal(0, host.Probe.Created);
        Assert.True(await host.Scheduler.ExistsAsync(id));
        await host.Scheduler.ResumeAsync(id);
        Assert.Equal(id, (await Read(host.Probe.Executions)).JobId);
        await Read(host.Probe.Releases);
        await host.Scheduler.PauseAsync(id);
        await host.Scheduler.DeleteAsync(id);
        Assert.False(await host.Scheduler.ExistsAsync(id));
    }

    [Fact]
    public async Task ConcurrentStart_CreatesOneScheduler_AndStopIsTerminal()
    {
        await using var host = new Harness();
        await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => host.Scheduler.StartAsync()));
        var native = await host.Native();
        Assert.Single(SchedulerRepository.Instance.LookupAll(), s => !host.PriorSchedulers.Contains(s.SchedulerName));
        await host.Scheduler.StopAsync();
        Assert.DoesNotContain(native, SchedulerRepository.Instance.LookupAll());
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Scheduler.StartAsync());
    }

    [Fact]
    public async Task CompletionHookFailure_DoesNotRetrySuccessfulBusiness()
    {
        await using var host = new Harness(history: true, retry: 2, failSuccessHook: true);
        await host.Native();
        var id = await host.Scheduler.ExecuteNowAsync<ProbeJob>();
        var completed = await Read(host.Probe.Completed);
        await Read(host.Probe.Releases);
        Assert.NotNull(completed); Assert.Equal(1, host.Probe.Created);
        Assert.DoesNotContain(host.Probe.Records, r => r.Result == JobExecutionResult.Failed);
        Assert.Equal(0, host.Probe.FailureNotifications);
        Assert.Equal(1, host.Probe.SuccessNotifications);
        var native = await host.Native();
        Assert.DoesNotContain(await native.GetTriggersOfJob(new(id.Name!, id.Group!)), t => t.GetNextFireTimeUtc() > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task ConstructorFailure_StillReleasesFactoryScope()
    {
        await using var host = new Harness();
        await host.Native();
        await host.Scheduler.ExecuteNowAsync<ConstructorFailureJob>();
        await Read(host.Probe.Releases);
        Assert.Equal(1, host.Probe.Created); Assert.Equal(1, host.Probe.Released);
    }

    [Fact]
    public async Task TenantHostAB_RestoreAmbient_AndSeparateHostsDoNotShareScheduler()
    {
        await using var first = new Harness(tenants: true);
        var nativeA = await first.Native();
        await using var second = new Harness(tenants: true);
        var nativeB = await second.Native();
        Assert.NotEqual(nativeA.SchedulerName, nativeB.SchedulerName);
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        foreach (var tenant in new Guid?[] { null, a, b, null })
        {
            var id = await first.Scheduler.ExecuteNowAsync<ProbeJob>(tenant);
            var c = await Read(first.Probe.Executions);
            Assert.Equal(tenant, c.TenantId);
            Assert.Equal(tenant?.ToString(), await Read(first.Probe.Ambient));
            await Read(first.Probe.Releases);
            Assert.Null(first.CurrentTenant.Id);
            Assert.Empty(await second.Scheduler.GetAllAsync());
        }
        Assert.Equal(4, first.Probe.Released); Assert.Equal(0, second.Probe.Created);
    }

    [Fact]
    public async Task UnknownTenant_RefusesBusinessAndReleasesScope()
    {
        await using var host = new Harness(tenants: true, unknownTenant: true);
        await host.Native();
        await host.Scheduler.ExecuteNowAsync<ProbeJob>(Guid.NewGuid());
        await Read(host.Probe.Completed);
        Assert.False(host.Probe.Executions.Reader.TryRead(out _));
        Assert.Equal(0, host.Probe.Created);
        Assert.Null(host.CurrentTenant.Id);
    }

    [Theory]
    [InlineData(false, true, "ICurrentTenant")]
    [InlineData(true, false, "ITenantProvider")]
    public async Task TenantWithoutRequiredService_RefusesBeforeJobConstructionAndReleasesScope(bool currentTenant, bool tenantProvider, string missingService)
    {
        await using var host = new Harness(tenants: currentTenant, tenantProvider: tenantProvider, observeAdapterScope: true);
        await host.Native();
        await host.Scheduler.ExecuteNowAsync<ProbeJob>(Guid.NewGuid());
        await Read(host.Probe.Completed);
        await Read(host.Probe.Releases);
        var failure = Assert.Single(host.Probe.QuartzFailures);
        Assert.Contains(missingService, failure.ToString());
        Assert.Equal(0, host.Probe.JobsConstructed);
        Assert.False(host.Probe.Executions.Reader.TryRead(out _));
        Assert.Equal(1, host.Probe.Created);
        Assert.Equal(1, host.Probe.Released);
    }

    [Fact]
    public async Task TypedFailureHistory_PreservesOriginalJsonAndIdentity()
    {
        await using var host = new Harness(history: true);
        host.Probe.FailPayload = true;
        await host.Native();
        var args = new Payload("failed payload", 92, Guid.NewGuid());
        var id = await host.Scheduler.ExecuteNowAsync<PayloadJob, Payload>(args);
        await Read(host.Probe.Completed);
        await Read(host.Probe.Releases);
        var failed = Assert.Single(host.Probe.Records, r => r.Result == JobExecutionResult.Failed);
        Assert.Equal(id.Uuid, failed.JobUuid);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(args, SchedulingJsonContext.Default.Payload), failed.ArgsJson);
        Assert.Equal(args, System.Text.Json.JsonSerializer.Deserialize(failed.ArgsJson!, SchedulingJsonContext.Default.Payload));
    }

    [Fact]
    public async Task SameJob_QueuesSecondTriggerUntilFirstScopeCompletes()
    {
        await using var host = new Harness();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Probe.Action = _ => release.Task;
        var native = await host.Native();
        var id = await host.Scheduler.RegisterAsync<ProbeJob>(new JobMetadata { Name = "serial-job", CronExpression = Cron });
        var key = new JobKey(id.Name!, id.Group!);
        await native.TriggerJob(key);
        await Read(host.Probe.Executions);
        try
        {
            var second = TriggerBuilder.Create().WithIdentity("second").ForJob(key).StartNow().Build();
            await native.ScheduleJob(second);
            Assert.Equal(TriggerState.Blocked, await native.GetTriggerState(second.Key));
            Assert.Single(await native.GetCurrentlyExecutingJobs());
            Assert.Equal(1, host.Probe.Created);
        }
        finally { release.TrySetResult(); }
        await Read(host.Probe.Completed); await Read(host.Probe.Releases);
        await Read(host.Probe.Executions); await Read(host.Probe.Completed); await Read(host.Probe.Releases);
        Assert.Equal(2, host.Probe.Created); Assert.Equal(2, host.Probe.Released);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoArgs_DelayedOverloads_AndLegacyRegistration(bool absoluteTime)
    {
        await using var host = new Harness();
        var id = absoluteTime ? await host.Scheduler.ScheduleAsync<ProbeJob>(DateTimeOffset.UtcNow.AddMilliseconds(20))
            : await host.Scheduler.ScheduleAsync<ProbeJob>(TimeSpan.FromMilliseconds(20));
        Assert.NotNull((await Read(host.Probe.Executions)).Args);
        await Read(host.Probe.Releases);
        Assert.Equal(id.Uuid, Assert.Single(await host.Scheduler.GetAllAsync()).Id.Uuid);
        var recurring = await host.Scheduler.RegisterAsync<ProbeJob, NoArgs>(new JobMetadata { Name = "legacy-noargs", CronExpression = Cron });
        Assert.NotEqual(Guid.Empty, recurring.Uuid);
    }

    [Fact]
    public async Task ModuleRegistration_UsesTheSameSchedulerComposition()
    {
        var services = new ServiceCollection();
        new CrestCreates.Scheduling.Quartz.Modules.SchedulingQuartzModule().OnConfigureServices(services);
        services.AddQuartzScheduling();
        await using var provider = services.BuildServiceProvider();
        Assert.Single(provider.GetServices<ISchedulerService>());
        await provider.GetRequiredService<ISchedulerService>().StartAsync();
        await provider.GetRequiredService<ISchedulerService>().StopAsync();
    }

    [Fact]
    public async Task NoHandler_ReceivesNoArgsAndStableIdentity()
    {
        await using var host = new Harness();
        var id = await host.Scheduler.ExecuteNowAsync<ProbeJob>();
        var context = await host.Probe.Executions.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(4));
        Assert.NotNull(context.Args);
        Assert.Equal(id.Uuid, context.JobId.Uuid);
    }

    [Fact]
    public async Task NamedJob_WithHistory_ExecutesWithRegisteredIdentity()
    {
        await using var host = new Harness(history: true);
        var id = await host.Scheduler.RegisterAsync<ProbeJob>(new JobMetadata { Name = "named", Group = "cron" });
        var context = await host.Probe.Executions.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(4));
        Assert.NotEqual(Guid.Empty, id.Uuid);
        Assert.Equal(id, context.JobId);
    }

    [Fact]
    public async Task Fire_DisposesAsyncOnlyDependencyExactlyOnce()
    {
        await using var host = new Harness();
        await host.Scheduler.ExecuteNowAsync<ProbeJob>();
        await host.Probe.Executions.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(4));
        await host.Probe.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(4));
        Assert.Equal(1, host.Probe.Created);
        Assert.Equal(1, host.Probe.Released);
    }

    public sealed class Probe
    {
        public Channel<JobExecutionContext<NoArgs>> Executions { get; } = Channel.CreateUnbounded<JobExecutionContext<NoArgs>>();
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Func<JobExecutionContext<NoArgs>, Task>? Action;
        public bool FailPayload;
        public int SuccessNotifications;
        public int FailureNotifications;
        public Channel<JobExecutionContext<Payload>> Payloads { get; } = Channel.CreateUnbounded<JobExecutionContext<Payload>>();
        public Channel<IJobExecutionContext> Completed { get; } = Channel.CreateUnbounded<IJobExecutionContext>();
        public Channel<int> Releases { get; } = Channel.CreateUnbounded<int>();
        public Channel<string?> Ambient { get; } = Channel.CreateUnbounded<string?>();
        public ConcurrentQueue<IJobRecord> Records { get; } = new();
        public int Created;
        public int Released;
        public int JobsConstructed;
        public ConcurrentQueue<JobExecutionException> QuartzFailures { get; } = new();
    }

    public sealed class AsyncDependency : IAsyncDisposable
    {
        private readonly Probe _probe;
        public AsyncDependency(Probe probe) { _probe = probe; Interlocked.Increment(ref probe.Created); }
        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _probe.Released);
            _probe.Disposed.TrySetResult();
            _probe.Releases.Writer.TryWrite(_probe.Released);
            return ValueTask.CompletedTask;
        }
    }

    public sealed class ProbeJob(Probe probe, AsyncDependency dependency, ICurrentTenant? tenant = null) : IJob
    {
        public async Task ExecuteAsync(JobExecutionContext<NoArgs> context, CancellationToken ct = default)
        {
            GC.KeepAlive(dependency);
            await Task.Yield();
            probe.Executions.Writer.TryWrite(context);
            probe.Ambient.Writer.TryWrite(tenant?.Id);
            if (probe.Action != null) await probe.Action(context);
        }
    }

    public sealed class PayloadJob(Probe probe, AsyncDependency dependency) : CrestCreates.Scheduling.Jobs.IJob<Payload>
    {
        public Task ExecuteAsync(JobExecutionContext<Payload> context, CancellationToken ct = default)
        {
            GC.KeepAlive(dependency);
            probe.Payloads.Writer.TryWrite(context);
            if (probe.FailPayload) throw new InvalidOperationException("payload failure");
            return Task.CompletedTask;
        }
    }

    public sealed class ConstructorFailureJob : IJob
    {
        public ConstructorFailureJob(AsyncDependency dependency) => throw new InvalidOperationException("constructor failed");
        public Task ExecuteAsync(JobExecutionContext<NoArgs> context, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class CompletionListener(Probe probe) : JobListenerSupport
    {
        public override string Name => "observations";
        public override Task JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
        {
            if (jobException != null) probe.QuartzFailures.Enqueue(jobException);
            probe.Completed.Writer.TryWrite(context);
            return Task.CompletedTask;
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        public Probe Probe { get; } = new();
        public ISchedulerService Scheduler { get; }
        private global::Quartz.IScheduler? _native;
        public HashSet<string> PriorSchedulers { get; } = SchedulerRepository.Instance.LookupAll().Select(s => s.SchedulerName).ToHashSet();
        public ICurrentTenant CurrentTenant => _provider.GetRequiredService<ICurrentTenant>();
        public async Task<global::Quartz.IScheduler> Native()
        {
            if (_native != null) return _native;
            await Scheduler.StartAsync();
            _native = SchedulerRepository.Instance.LookupAll().Single(s => !PriorSchedulers.Contains(s.SchedulerName));
            _native.ListenerManager.AddJobListener(new CompletionListener(Probe));
            return _native;
        }
        public Harness(bool history = false, bool codec = true, int retry = 0, bool failSuccessHook = false, bool tenants = false, bool unknownTenant = false, bool? tenantProvider = null, bool observeAdapterScope = false)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(Probe);
            services.AddScoped<AsyncDependency>();
            services.AddScoped<ProbeJob>(provider =>
            {
                Interlocked.Increment(ref Probe.JobsConstructed);
                return ActivatorUtilities.CreateInstance<ProbeJob>(provider);
            });
            services.AddScoped<PayloadJob>();
            services.AddScoped<ConstructorFailureJob>();
            if (codec) services.AddQuartzJobArgs(SchedulingJsonContext.Default.Payload);
            services.Configure<JobRetryOptions>(o => { });
            services.AddSingleton<IOptions<JobRetryOptions>>(Options.Create(new JobRetryOptions { MaxRetries = retry, InitialDelay = TimeSpan.FromMilliseconds(20) }));
            if (history)
            {
                var repository = new Mock<IJobHistoryRepository>();
                repository.Setup(r => r.CreateAsync(It.IsAny<IJobRecord>(), It.IsAny<CancellationToken>()))
                    .Callback<IJobRecord, CancellationToken>((record, _) => Probe.Records.Enqueue(record)).Returns<IJobRecord, CancellationToken>((record, _) => Task.FromResult<IJobRecord>(record));
                services.AddSingleton(repository.Object);
                if (!failSuccessHook) services.AddScoped<IJobExecutionHandler, DefaultJobExecutionHandler>();
                else
                {
                    var handler = new Mock<IJobExecutionHandler>();
                    handler.Setup(h => h.HandleAsync(It.IsAny<JobFailureContext>(), It.IsAny<CancellationToken>())).Callback(() => Interlocked.Increment(ref Probe.FailureNotifications)).Returns(Task.CompletedTask);
                    handler.Setup(h => h.OnJobSucceededAsync(It.IsAny<JobSucceededContext>(), It.IsAny<CancellationToken>())).Callback(() => Interlocked.Increment(ref Probe.SuccessNotifications)).ThrowsAsync(new InvalidOperationException("history failed"));
                    handler.Setup(h => h.ShouldRetry(It.IsAny<JobFailureContext>())).Returns(true);
                    services.AddSingleton(handler.Object);
                }
            }
            if (tenants) services.AddSingleton<ICurrentTenant, CurrentTenant>();
            if (tenantProvider ?? tenants)
            {
                var provider = new Mock<ITenantProvider>();
                provider.Setup(p => p.GetTenantAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns<string, CancellationToken>((id, _) => Task.FromResult(unknownTenant ? null : Mock.Of<ITenantInfo>(t => t.Id == id && t.Name == "display-name")));
                services.AddSingleton(provider.Object);
            }
            services.AddQuartzScheduling();
            if (observeAdapterScope)
            {
                // Attach the disposal probe to the factory's scope before tenant admission can reject the job.
                var adapterType = services.Single(d => d.ServiceType.Name == "QuartzJobAdapter`2").ServiceType
                    .MakeGenericType(typeof(ProbeJob), typeof(NoArgs));
                services.AddTransient(adapterType, provider =>
                {
                    provider.GetRequiredService<AsyncDependency>();
                    return ActivatorUtilities.CreateInstance(provider, adapterType);
                });
            }
            _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            Scheduler = _provider.GetRequiredService<ISchedulerService>();
        }
        public async ValueTask DisposeAsync()
        {
            await Scheduler.StopAsync();
            await _provider.DisposeAsync();
        }
    }
}

public sealed record Payload : IJobArgs
{
    public static int Constructed;
    public string Text { get; init; }
    public int Count { get; init; }
    public Guid Target { get; init; }
    public Payload(string text, int count, Guid target)
    {
        Text = text; Count = count; Target = target;
        Interlocked.Increment(ref Constructed);
    }
}
[JsonSerializable(typeof(Payload))]
internal partial class SchedulingJsonContext : JsonSerializerContext;
