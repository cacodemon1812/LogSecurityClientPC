using System.Diagnostics;
using PolicyCollector.Agent.Infrastructure;

namespace PolicyCollector.Agent.Collectors;

public sealed class ScheduledTaskCollector : ICollector<List<TaskEntry>>
{
    private readonly ProcessRunner _process;
    private readonly ILogger<ScheduledTaskCollector> _logger;

    public ScheduledTaskCollector(ProcessRunner process, ILogger<ScheduledTaskCollector> logger)
    {
        _process = process;
        _logger = logger;
    }

    public string ModuleName => "ScheduledTasks";

    public async Task<CollectorResult<List<TaskEntry>>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _process.RunAsync(
                "schtasks.exe",
                "/Query /FO CSV /V /NH",
                timeout: TimeSpan.FromSeconds(30),
                ct: ct);

            if (result.ExitCode != 0)
            {
                _logger.LogWarning("schtasks.exe returned {Code}: {Error}", result.ExitCode, result.Stderr);
                return CollectorResult<List<TaskEntry>>.Fail("ScheduledTasks collection failed", result.Stderr);
            }

            var tasks = ParseTasks(result.Stdout);
            return CollectorResult<List<TaskEntry>>.Ok(tasks, sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ScheduledTasks collection failed");
            return CollectorResult<List<TaskEntry>>.Fail("ScheduledTasks collection failed", ex.ToString());
        }
    }

    private List<TaskEntry> ParseTasks(string stdout)
    {
        var tasks = new List<TaskEntry>();

        foreach (var line in stdout.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var cols = ParseCsvLine(line);
            if (cols.Count < 15)
                continue;

            var fullTaskName = cols[1].Trim();
            if (string.IsNullOrEmpty(fullTaskName))
                continue;

            // Skip Microsoft built-in task folders.
            if (fullTaskName.StartsWith("\\Microsoft\\", StringComparison.OrdinalIgnoreCase))
                continue;

            SplitTaskPath(fullTaskName, out var taskPath, out var taskName);

            tasks.Add(new TaskEntry
            {
                TaskName = taskName,
                TaskPath = taskPath,
                State = NormalizeState(cols[3]),
                RunAsUser = NormalizeText(cols[14]),
                LastRunTime = ParseDateTime(cols[5]),
                LastRunResult = ParseLastRunResult(cols[6]),
                NextRunTime = ParseDateTime(cols[2])
            });
        }

        return tasks;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var cols = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                cols.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        cols.Add(current.ToString());
        return cols;
    }

    private static void SplitTaskPath(string fullTaskName, out string taskPath, out string taskName)
    {
        var normalized = fullTaskName.Replace('/', '\\');
        var lastSeparator = normalized.LastIndexOf('\\');

        if (lastSeparator <= 0)
        {
            taskPath = "\\";
            taskName = normalized.TrimStart('\\');
            return;
        }

        taskPath = normalized.Substring(0, lastSeparator + 1);
        taskName = normalized[(lastSeparator + 1)..];
    }

    private static string? NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.Equals("N/A", StringComparison.OrdinalIgnoreCase))
            return null;

        return trimmed;
    }

    private static string? NormalizeState(string? value)
    {
        var normalized = NormalizeText(value);
        return normalized ?? "Unknown";
    }

    private static DateTimeOffset? ParseDateTime(string? value)
    {
        var normalized = NormalizeText(value);
        if (normalized is null)
            return null;

        if (DateTime.TryParse(normalized, out var parsed))
            return new DateTimeOffset(parsed);

        return null;
    }

    private static int? ParseLastRunResult(string? value)
    {
        var normalized = NormalizeText(value);
        if (normalized is null)
            return null;

        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(normalized[2..], System.Globalization.NumberStyles.HexNumber, null, out var hex))
            return hex;

        if (int.TryParse(normalized, out var decimalValue))
            return decimalValue;

        return null;
    }
}
