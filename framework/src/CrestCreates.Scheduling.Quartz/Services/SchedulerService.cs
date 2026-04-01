using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CrestCreates.Scheduling.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Quartz.Impl;

// 使用别名避免命名冲突
using QuartzJob = Quartz.IJob;
using SchedulingJob = CrestCreates.Scheduling.Jobs.IJob;

namespace CrestCreates.Scheduling.Quartz.Services
{
    public class SchedulerService : CrestCreates.Scheduling.Services.ISchedulerService
    {
        private readonly IServiceProvider _serviceProvider;
        private IScheduler _scheduler;
        private readonly List<JobMetadata> _jobs = new List<JobMetadata>();

        public SchedulerService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task StartAsync()
        {
            var factory = new StdSchedulerFactory();
            _scheduler = await factory.GetScheduler();
            await _scheduler.Start();
        }

        public async Task StopAsync()
        {
            if (_scheduler != null)
            {
                await _scheduler.Shutdown();
            }
        }

        public async Task RegisterJobAsync<TJob>(JobMetadata metadata) where TJob : SchedulingJob
        {
            if (_scheduler == null)
            {
                await StartAsync();
            }

            var jobKey = new JobKey(metadata.Name, metadata.Group);
            var triggerKey = new TriggerKey($"{metadata.Name}Trigger", metadata.Group);

            if (await _scheduler.CheckExists(jobKey))
            {
                await _scheduler.DeleteJob(jobKey);
            }

            var job = JobBuilder.Create<QuartzJobAdapter<TJob>>()
                .WithIdentity(jobKey)
                .WithDescription(metadata.Description)
                .Build();

            var trigger = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .WithCronSchedule(metadata.CronExpression)
                .Build();

            await _scheduler.ScheduleJob(job, trigger);
            _jobs.Add(metadata);
        }

        public async Task<string> ScheduleJobAsync<TJob>(TimeSpan delay) where TJob : SchedulingJob
        {
            if (_scheduler == null)
            {
                await StartAsync();
            }

            var jobKey = new JobKey(Guid.NewGuid().ToString());
            var triggerKey = new TriggerKey($"{jobKey.Name}Trigger");

            var job = JobBuilder.Create<QuartzJobAdapter<TJob>>()
                .WithIdentity(jobKey)
                .Build();

            var trigger = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .StartAt(DateTimeOffset.UtcNow.Add(delay))
                .Build();

            await _scheduler.ScheduleJob(job, trigger);
            return jobKey.Name;
        }

        public async Task<string> ScheduleJobAsync<TJob>(DateTimeOffset scheduledTime) where TJob : SchedulingJob
        {
            if (_scheduler == null)
            {
                await StartAsync();
            }

            var jobKey = new JobKey(Guid.NewGuid().ToString());
            var triggerKey = new TriggerKey($"{jobKey.Name}Trigger");

            var job = JobBuilder.Create<QuartzJobAdapter<TJob>>()
                .WithIdentity(jobKey)
                .Build();

            var trigger = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .StartAt(scheduledTime)
                .Build();

            await _scheduler.ScheduleJob(job, trigger);
            return jobKey.Name;
        }

        public async Task<string> ScheduleJobAsync<TJob>(string cronExpression, string? group = null) where TJob : SchedulingJob
        {
            if (_scheduler == null)
            {
                await StartAsync();
            }

            var jobName = Guid.NewGuid().ToString();
            var jobKey = new JobKey(jobName, group ?? "Default");
            var triggerKey = new TriggerKey($"{jobName}Trigger", group ?? "Default");

            var job = JobBuilder.Create<QuartzJobAdapter<TJob>>()
                .WithIdentity(jobKey)
                .Build();

            var trigger = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .WithCronSchedule(cronExpression)
                .Build();

            await _scheduler.ScheduleJob(job, trigger);
            return jobKey.Name;
        }

        public async Task ExecuteJobAsync<TJob>() where TJob : SchedulingJob
        {
            if (_scheduler == null)
            {
                await StartAsync();
            }

            var jobKey = new JobKey(typeof(TJob).Name, "Default");
            await _scheduler.TriggerJob(jobKey);
        }

        public async Task PauseJobAsync(string jobName, string jobGroup = "Default")
        {
            if (_scheduler != null)
            {
                var jobKey = new JobKey(jobName, jobGroup);
                await _scheduler.PauseJob(jobKey);
            }
        }

        public async Task ResumeJobAsync(string jobName, string jobGroup = "Default")
        {
            if (_scheduler != null)
            {
                var jobKey = new JobKey(jobName, jobGroup);
                await _scheduler.ResumeJob(jobKey);
            }
        }

        public async Task DeleteJobAsync(string jobName, string jobGroup = "Default")
        {
            if (_scheduler != null)
            {
                var jobKey = new JobKey(jobName, jobGroup);
                await _scheduler.DeleteJob(jobKey);
                _jobs.RemoveAll(j => j.Name == jobName && j.Group == jobGroup);
            }
        }

        public async Task CancelJobAsync(string jobId)
        {
            if (_scheduler != null)
            {
                var jobKey = new JobKey(jobId);
                await _scheduler.DeleteJob(jobKey);
            }
        }

        public async Task PauseJobAsync(string jobId)
        {
            if (_scheduler != null)
            {
                var jobKey = new JobKey(jobId);
                await _scheduler.PauseJob(jobKey);
            }
        }

        public async Task ResumeJobAsync(string jobId)
        {
            if (_scheduler != null)
            {
                var jobKey = new JobKey(jobId);
                await _scheduler.ResumeJob(jobKey);
            }
        }

        public async Task<bool> JobExistsAsync(string jobId)
        {
            if (_scheduler != null)
            {
                var jobKey = new JobKey(jobId);
                return await _scheduler.CheckExists(jobKey);
            }
            return false;
        }

        public Task<IEnumerable<JobMetadata>> GetAllJobsAsync()
        {
            return Task.FromResult<IEnumerable<JobMetadata>>(_jobs);
        }

        // Quartz任务适配器
        private class QuartzJobAdapter<TJob> : QuartzJob where TJob : SchedulingJob
        {
            private readonly IServiceProvider _serviceProvider;

            public QuartzJobAdapter(IServiceProvider serviceProvider)
            {
                _serviceProvider = serviceProvider;
            }

            public async Task Execute(IJobExecutionContext context)
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var job = scope.ServiceProvider.GetRequiredService<TJob>();
                    await job.ExecuteAsync(context.CancellationToken);
                }
            }
        }
    }
}