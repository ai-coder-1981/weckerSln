using Quartz;
using Wecker.Service.Services;

namespace Wecker.Service.Jobs;

[DisallowConcurrentExecution]
public class AlarmJob(AlarmService alarms) : IJob
{
    public Task Execute(IJobExecutionContext context) =>
        alarms.FireAsync(context.MergedJobDataMap.GetInt("id"));
}
