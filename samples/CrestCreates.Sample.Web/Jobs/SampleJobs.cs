using CrestCreates.Scheduling.Jobs;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Sample.Web.Jobs
{
    /// <summary>
    /// 示例定时任务
    /// </summary>
    public class SampleJob : IJob
    {
        private readonly ILogger<SampleJob> _logger;

        public SampleJob(ILogger<SampleJob> logger)
        {
            _logger = logger;
        }

        public async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Sample job executed at: {time}", DateTime.Now);
            
            // 模拟任务执行
            await Task.Delay(1000, cancellationToken);
            
            _logger.LogInformation("Sample job completed at: {time}", DateTime.Now);
        }
    }

    /// <summary>
    /// 内存检查任务
    /// </summary>
    public class MemoryCheckJob : IJob
    {
        private readonly ILogger<MemoryCheckJob> _logger;

        public MemoryCheckJob(ILogger<MemoryCheckJob> logger)
        {
            _logger = logger;
        }

        public async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            var allocated = GC.GetTotalMemory(false);
            var memoryInfo = GC.GetGCMemoryInfo();
            
            _logger.LogInformation("Memory usage: {allocated} bytes", allocated);
            _logger.LogInformation("GC memory info: TotalMemory: {total}, HighMemoryThreshold: {highThreshold}", 
                memoryInfo.TotalAvailableMemoryBytes, memoryInfo.HighMemoryLoadThresholdBytes);
            
            await Task.CompletedTask;
        }
    }
}