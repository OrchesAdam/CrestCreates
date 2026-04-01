using Microsoft.AspNetCore.Mvc;
using CrestCreates.Scheduling.Services;
using CrestCreates.Scheduling.Jobs;
using CrestCreates.Sample.Web.Jobs;

namespace CrestCreates.Sample.Web.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SchedulingController : ControllerBase
    {
        private readonly ISchedulerService _schedulerService;

        public SchedulingController(ISchedulerService schedulerService)
        {
            _schedulerService = schedulerService;
        }

        /// <summary>
        /// 立即执行示例任务
        /// </summary>
        [HttpPost("execute-sample")]
        public async Task<IActionResult> ExecuteSampleJob()
        {
            await _schedulerService.ExecuteJobAsync<SampleJob>();
            return Ok("Sample job executed immediately");
        }

        /// <summary>
        /// 延迟执行任务
        /// </summary>
        [HttpPost("schedule-delay")]
        public async Task<IActionResult> ScheduleDelayedJob(int delaySeconds = 5)
        {
            var jobId = await _schedulerService.ScheduleJobAsync<SampleJob>(TimeSpan.FromSeconds(delaySeconds));
            return Ok(new { jobId, message = $"Job scheduled to run in {delaySeconds} seconds" });
        }

        /// <summary>
        /// 定时执行任务（Cron 表达式）
        /// </summary>
        [HttpPost("schedule-cron")]
        public async Task<IActionResult> ScheduleCronJob(string cronExpression = "*/5 * * * * ?")
        {
            var jobId = await _schedulerService.ScheduleJobAsync<MemoryCheckJob>(cronExpression, "System");
            return Ok(new { jobId, message = $"Job scheduled with cron expression: {cronExpression}" });
        }

        /// <summary>
        /// 注册定时任务
        /// </summary>
        [HttpPost("register-job")]
        public async Task<IActionResult> RegisterJob()
        {
            var metadata = new JobMetadata
            {
                Name = "SampleJob",
                Group = "Samples",
                CronExpression = "*/10 * * * * ?", // 每10秒执行一次
                Description = "Sample job that runs every 10 seconds",
                Enabled = true
            };

            await _schedulerService.RegisterJobAsync<SampleJob>(metadata);
            return Ok("Job registered successfully");
        }

        /// <summary>
        /// 获取所有任务
        /// </summary>
        [HttpGet("jobs")]
        public async Task<IActionResult> GetAllJobs()
        {
            var jobs = await _schedulerService.GetAllJobsAsync();
            return Ok(jobs);
        }
    }
}