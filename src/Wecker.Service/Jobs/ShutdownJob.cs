using Quartz;
using Wecker.Service.Services;

namespace Wecker.Service.Jobs;

[DisallowConcurrentExecution]
public class ShutdownJob(ShutdownService shutdown) : IJob
{
    public Task Execute(IJobExecutionContext context)
    {
        shutdown.ExecuteScheduled();
        return Task.CompletedTask;
    }
}
