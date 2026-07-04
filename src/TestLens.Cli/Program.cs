using System.Reflection;
using TestLens.Analysis;
using TestLens.Discovery;
using TestLens.Execution;
using TestLens.History;
using TestLens.Models;
using TestLens.Report;

namespace TestLens;

internal static class Program
{
    private static readonly string Version =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? "0.0.0";

    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }
        if (args[0] is "-v" or "--version" or "version")
        {
            Console.WriteLine(Version);
            return 0;
        }

        try
        {
            // `testlens <dir>` is shorthand for `testlens scan <dir>`.
            return args[0] switch
            {
                "scan" => Scan(args.Skip(1).ToArray()),
                "report" => Report(args.Skip(1).ToArray()),
                "history" => HistoryCommand(args.Skip(1).ToArray()),
                "demo" => Demo(args.Skip(1).ToArray()),
                _ when !args[0].StartsWith('-') && Directory.Exists(args[0]) => Scan(args),
                _ => UnknownCommand(args[0]),
            };
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"error: {e.Message}");
            return 1;
        }
    }

    private static int Scan(string[] args)
    {
        var options = CliOptions.Parse(args, requireDirectory: true);
        if (options is null) return 2;

        Banner();
        Console.WriteLine($"Scanning {options.Root} ...");

        var discovered = ProjectDiscoverer.Discover(options.Root);
        if (discovered.Count == 0)
        {
            Console.WriteLine("No test projects found (C#, Vue or Angular).");
            return 0;
        }
        Console.WriteLine($"Found {discovered.Count} test project(s).");
        Console.WriteLine();

        var snapshot = new RunSnapshot
        {
            Timestamp = DateTimeOffset.UtcNow,
            Label = options.Label,
            ToolVersion = Version,
            Root = Path.GetFullPath(options.Root),
            TestsExecuted = options.RunTests,
        };

        foreach (var project in discovered)
        {
            Console.Write($"  {Icon(project.Kind)} {project.Name} [{project.Framework}] ... ");

            var result = new ProjectResult
            {
                Name = project.Name,
                Path = project.RelativePath,
                Kind = project.Kind,
                Framework = project.Framework,
                Discovery = project.Kind == "csharp"
                    ? CSharpTestScanner.Scan(project.AbsolutePath)
                    : JsTestScanner.Scan(project.AbsolutePath),
            };

            if (options.RunTests)
            {
                if (project.Kind != "csharp" && options.NpmInstall &&
                    !Directory.Exists(Path.Combine(project.AbsolutePath, "node_modules")))
                {
                    Console.Write("npm install ... ");
                    var npm = OperatingSystem.IsWindows() ? "npm.cmd" : "npm";
                    ProcessRunner.Run(npm, "install --no-audit --no-fund", project.AbsolutePath, options.Timeout);
                }

                result.Execution = project.Kind == "csharp"
                    ? DotnetTestRunner.Run(project.AbsolutePath, options.Timeout)
                    : JsTestRunner.Run(project.AbsolutePath, project.Framework, options.Timeout);
            }
            else
            {
                result.Execution = new ExecutionResult { Status = "skipped" };
            }

            snapshot.Projects.Add(result);
            Console.WriteLine(Summary(result));
        }

        var store = new HistoryStore(options.OutputDir);
        var savedTo = store.Save(snapshot);
        var runs = store.LoadAll();
        var reportPath = ReportGenerator.Generate(runs, options.OutputDir, Version);

        Console.WriteLine();
        PrintTotals(snapshot);
        Console.WriteLine();
        Console.WriteLine($"  Snapshot : {savedTo}");
        Console.WriteLine($"  Report   : {reportPath}  ({runs.Count} run(s) in history)");
        Console.WriteLine();
        Console.WriteLine("Powered by IT Performance ApS");

        bool anyFailed = snapshot.Projects.Any(p => p.Execution?.Failed > 0 || p.Execution?.Status == "error");
        return options.FailOnErrors && anyFailed ? 1 : 0;
    }

    private static int Report(string[] args)
    {
        var options = CliOptions.Parse(args, requireDirectory: false);
        if (options is null) return 2;

        var store = new HistoryStore(options.OutputDir);
        var runs = store.LoadAll();
        if (runs.Count == 0)
        {
            Console.Error.WriteLine($"error: no history found in {store.HistoryDir}. Run 'testlens scan <dir>' first.");
            return 1;
        }

        var reportPath = ReportGenerator.Generate(runs, options.OutputDir, Version);
        Console.WriteLine($"Report regenerated: {reportPath}  ({runs.Count} run(s) in history)");
        return 0;
    }

    private static int HistoryCommand(string[] args)
    {
        var options = CliOptions.Parse(args, requireDirectory: false);
        if (options is null) return 2;

        var runs = new HistoryStore(options.OutputDir).LoadAll();
        if (runs.Count == 0)
        {
            Console.WriteLine("No runs recorded yet.");
            return 0;
        }

        Console.WriteLine($"{"Run",-18} {"When (UTC)",-18} {"Projects",8} {"Tests",8} {"Passed",8} {"Failed",8}");
        foreach (var run in runs)
        {
            int tests = run.Projects.Sum(p => p.Discovery.Total);
            int passed = run.Projects.Sum(p => p.Execution?.Passed ?? 0);
            int failed = run.Projects.Sum(p => p.Execution?.Failed ?? 0);
            Console.WriteLine($"{run.Id,-18} {run.Timestamp:yyyy-MM-dd HH:mm}   {run.Projects.Count,8} {tests,8} {passed,8} {failed,8}");
        }
        return 0;
    }

    private static int Demo(string[] args)
    {
        var options = CliOptions.Parse(args, requireDirectory: false, defaultOut: "testlens-demo");
        if (options is null) return 2;

        Banner();
        var runs = DemoData.Generate(Version);
        var store = new HistoryStore(options.OutputDir);
        foreach (var run in runs) store.Save(run);
        var reportPath = ReportGenerator.Generate(store.LoadAll(), options.OutputDir, Version);

        Console.WriteLine($"Demo report with {runs.Count} runs generated:");
        Console.WriteLine($"  {reportPath}");
        Console.WriteLine();
        Console.WriteLine("Powered by IT Performance ApS");
        return 0;
    }

    private static int UnknownCommand(string arg)
    {
        Console.Error.WriteLine(Directory.Exists(arg) || !arg.StartsWith('-')
            ? $"error: unknown command or directory not found: '{arg}'"
            : $"error: unknown option '{arg}' (the scan directory must come first)");
        Console.Error.WriteLine("Run 'testlens --help' for usage.");
        return 2;
    }

    private static string Icon(string kind) => kind switch
    {
        "csharp" => "C#",
        "vue" => "Vue",
        "angular" => "Ng",
        _ => "JS",
    };

    private static string Summary(ProjectResult p)
    {
        var d = p.Discovery;
        var parts = new List<string> { $"{d.Total} tests" };
        if (d.Ignored > 0) parts.Add($"{d.Ignored} ignored");
        if (d.Explicit > 0) parts.Add($"{d.Explicit} explicit");
        if (d.CommentedOut > 0) parts.Add($"{d.CommentedOut} commented out");

        if (p.Execution is { Status: "completed" } e)
            parts.Add($"{e.Passed} passed, {e.Failed} failed" + (e.Skipped > 0 ? $", {e.Skipped} skipped" : ""));
        else if (p.Execution is { Status: "error" } err)
            parts.Add($"run failed: {Truncate(err.Error ?? "unknown", 90)}");

        return string.Join("  |  ", parts);
    }

    private static void PrintTotals(RunSnapshot snapshot)
    {
        int total = snapshot.Projects.Sum(p => p.Discovery.Total);
        int ignored = snapshot.Projects.Sum(p => p.Discovery.Ignored);
        int expl = snapshot.Projects.Sum(p => p.Discovery.Explicit);
        int commented = snapshot.Projects.Sum(p => p.Discovery.CommentedOut);
        int passed = snapshot.Projects.Sum(p => p.Execution?.Passed ?? 0);
        int failed = snapshot.Projects.Sum(p => p.Execution?.Failed ?? 0);

        Console.WriteLine($"  Totals: {total} tests in {snapshot.Projects.Count} projects  |  " +
                          $"{passed} passed, {failed} failed  |  " +
                          $"{ignored} ignored, {expl} explicit, {commented} commented out");
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";

    private static void Banner()
    {
        Console.WriteLine($"TestLens v{Version} — test insight for C#, Vue & Angular");
        Console.WriteLine();
    }

    private static void PrintHelp()
    {
        Banner();
        Console.WriteLine("""
            Usage:
              testlens <directory> [options]        Scan, run tests and record a snapshot
              testlens scan <directory> [options]   Same as above
              testlens report [options]             Regenerate the HTML report from history
              testlens history [options]            List recorded runs
              testlens demo [options]               Generate a demo report with sample data

            Options:
              --out <dir>        Output directory (default: <directory>/.testlens)
              --no-run           Static analysis only - discover tests without executing them
              --npm-install      Run 'npm install' for JS projects missing node_modules
              --label <text>     Attach a label to this run (shown in the report timeline)
              --timeout <sec>    Per-project test run timeout (default: 600)
              --fail-on-errors   Exit code 1 when tests fail or a runner errors (for CI)
              -v, --version      Print version
              -h, --help         Show this help

            The report is a single self-contained HTML file: <out>/index.html.
            Every scan appends to <out>/history/ so you can travel back and forth
            through past runs directly in the report.

            Powered by IT Performance ApS
            """);
    }
}

internal sealed class CliOptions
{
    public string Root = ".";
    public string OutputDir = "";
    public bool RunTests = true;
    public bool NpmInstall;
    public string? Label;
    public bool FailOnErrors;
    public TimeSpan Timeout = TimeSpan.FromSeconds(600);

    public static CliOptions? Parse(string[] args, bool requireDirectory, string? defaultOut = null)
    {
        var options = new CliOptions();
        string? dir = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out": options.OutputDir = Next(args, ref i, "--out"); break;
                case "--no-run": options.RunTests = false; break;
                case "--npm-install": options.NpmInstall = true; break;
                case "--label": options.Label = Next(args, ref i, "--label"); break;
                case "--fail-on-errors": options.FailOnErrors = true; break;
                case "--timeout":
                    if (!int.TryParse(Next(args, ref i, "--timeout"), out var seconds) || seconds <= 0)
                    {
                        Console.Error.WriteLine("error: --timeout expects a positive number of seconds");
                        return null;
                    }
                    options.Timeout = TimeSpan.FromSeconds(seconds);
                    break;
                default:
                    if (args[i].StartsWith('-') || dir is not null)
                    {
                        Console.Error.WriteLine($"error: unexpected argument '{args[i]}'");
                        return null;
                    }
                    dir = args[i];
                    break;
            }
        }

        if (requireDirectory)
        {
            if (dir is null)
            {
                Console.Error.WriteLine("error: a directory to scan is required");
                return null;
            }
            if (!Directory.Exists(dir))
            {
                Console.Error.WriteLine($"error: directory not found: {dir}");
                return null;
            }
            options.Root = Path.GetFullPath(dir);
        }

        if (options.OutputDir.Length == 0)
            options.OutputDir = requireDirectory
                ? Path.Combine(options.Root, ".testlens")
                : defaultOut ?? Path.Combine(".", ".testlens");
        options.OutputDir = Path.GetFullPath(options.OutputDir);
        return options;
    }

    private static string Next(string[] args, ref int i, string option)
    {
        if (i + 1 >= args.Length) throw new ArgumentException($"option {option} expects a value");
        return args[++i];
    }
}
