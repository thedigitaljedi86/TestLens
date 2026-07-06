using System.Text.Json.Serialization;

namespace TestLens.Models;

/// <summary>One analysis run across all discovered projects.</summary>
public sealed class RunSnapshot
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("timestamp")] public DateTimeOffset Timestamp { get; set; }
    [JsonPropertyName("label")] public string? Label { get; set; }
    [JsonPropertyName("toolVersion")] public string ToolVersion { get; set; } = "";
    [JsonPropertyName("root")] public string Root { get; set; } = "";
    [JsonPropertyName("testsExecuted")] public bool TestsExecuted { get; set; }
    [JsonPropertyName("projects")] public List<ProjectResult> Projects { get; set; } = new();
}

public sealed class ProjectResult
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    /// <summary>Path relative to the scan root.</summary>
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    /// <summary>csharp | vue | angular | javascript</summary>
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    /// <summary>xunit | nunit | mstest | jest | vitest | karma-jasmine | mocha | playwright | unknown</summary>
    [JsonPropertyName("framework")] public string Framework { get; set; } = "";
    [JsonPropertyName("discovery")] public DiscoveryCounts Discovery { get; set; } = new();
    [JsonPropertyName("execution")] public ExecutionResult? Execution { get; set; }
}

/// <summary>Counts produced by static analysis of the test sources.</summary>
public sealed class DiscoveryCounts
{
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("ignored")] public int Ignored { get; set; }
    [JsonPropertyName("explicit")] public int Explicit { get; set; }
    [JsonPropertyName("commentedOut")] public int CommentedOut { get; set; }
    [JsonPropertyName("files")] public int Files { get; set; }
}

/// <summary>Result of actually executing the project's test suite.</summary>
public sealed class ExecutionResult
{
    /// <summary>completed | error | skipped</summary>
    [JsonPropertyName("status")] public string Status { get; set; } = "skipped";
    [JsonPropertyName("passed")] public int Passed { get; set; }
    [JsonPropertyName("failed")] public int Failed { get; set; }
    [JsonPropertyName("skipped")] public int Skipped { get; set; }
    [JsonPropertyName("durationMs")] public long DurationMs { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
}
