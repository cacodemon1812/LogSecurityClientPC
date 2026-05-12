using System.Diagnostics;
using System.Management.Automation;
using PolicyCollector.Agent.Infrastructure;

namespace PolicyCollector.Agent.Collectors;

public sealed class ScheduledTaskCollector : ICollector<List<TaskEntry>>
{
    private readonly PowerShellRunner _ps;

    public ScheduledTaskCollector(PowerShellRunner ps)
    {
        _ps = ps;
    }

    public string ModuleName => "ScheduledTasks";

    public async Task<CollectorResult<List<TaskEntry>>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var objects = await _ps.RunScriptAsync(@"
                Get-ScheduledTask | Where-Object { $_.TaskPath -notlike '\Microsoft\*' -or $_.TaskPath -eq '\' } |
                ForEach-Object {
                    $info = $_ | Get-ScheduledTaskInfo -ErrorAction SilentlyContinue
                    [PSCustomObject]@{
                        TaskName = $_.TaskName
                        TaskPath = $_.TaskPath
                        State = $_.State.ToString()
                        RunAsUser = $_.Principal.UserId
                        LastRunTime = $info.LastRunTime
                        LastRunResult = $info.LastTaskResult
                        NextRunTime = $info.NextRunTime
                    }
                } | ConvertTo-Json -Compress", ct);

            var tasks = ParseTasks(objects);
            return CollectorResult<List<TaskEntry>>.Ok(tasks, sw.Elapsed);
        }
        catch (Exception ex)
        {
            return CollectorResult<List<TaskEntry>>.Fail("ScheduledTasks collection failed", ex.ToString());
        }
    }

    private List<TaskEntry> ParseTasks(IReadOnlyList<PSObject> objects)
    {
        var tasks = new List<TaskEntry>();

        foreach (var obj in objects)
        {
            if (obj?.Properties == null) continue;

            var lastRunTime = obj.Properties["LastRunTime"]?.Value is DateTime lrt
                ? (DateTimeOffset?)new DateTimeOffset(lrt, TimeSpan.Zero)
                : null;

            var nextRunTime = obj.Properties["NextRunTime"]?.Value is DateTime nrt
                ? (DateTimeOffset?)new DateTimeOffset(nrt, TimeSpan.Zero)
                : null;

            tasks.Add(new TaskEntry
            {
                TaskName = obj.Properties["TaskName"]?.Value?.ToString(),
                TaskPath = obj.Properties["TaskPath"]?.Value?.ToString(),
                State = obj.Properties["State"]?.Value?.ToString(),
                RunAsUser = obj.Properties["RunAsUser"]?.Value?.ToString(),
                LastRunTime = lastRunTime,
                LastRunResult = obj.Properties["LastRunResult"]?.Value is int lr ? lr : null,
                NextRunTime = nextRunTime
            });
        }

        return tasks;
    }
}
