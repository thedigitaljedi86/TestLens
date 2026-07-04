using System.Diagnostics;
using System.Text;

namespace TestLens.Execution;

public sealed record ProcessResult(int ExitCode, string Stdout, string Stderr, bool TimedOut);

public static class ProcessRunner
{
    public static ProcessResult Run(string fileName, string arguments, string workingDirectory, TimeSpan timeout,
        IDictionary<string, string>? environment = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment["CI"] = "true";
        if (environment is not null)
            foreach (var (key, value) in environment) psi.Environment[key] = value;

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (stdout) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (stderr) stderr.AppendLine(e.Data); };

        try { process.Start(); }
        catch (Exception e)
        {
            return new ProcessResult(-1, "", $"Could not start '{fileName}': {e.Message}", false);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            process.WaitForExit(5000);
            return new ProcessResult(-1, stdout.ToString(), stderr.ToString(), TimedOut: true);
        }

        process.WaitForExit(); // flush async output handlers
        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString(), false);
    }
}
