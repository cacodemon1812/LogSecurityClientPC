using System.Diagnostics;

namespace PolicyCollector.Agent.Infrastructure;

public sealed class ProcessRunner
{
    public record ProcessResult(int ExitCode, string Stdout, string Stderr);

    public async Task<ProcessResult> RunAsync(
        string executable,
        string arguments,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            var psi = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
                return new ProcessResult(-1, string.Empty, "Failed to start process");

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill();
                throw;
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            return new ProcessResult(process.ExitCode, stdout, stderr);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to run process: {ex.Message}", ex);
        }
    }
}
