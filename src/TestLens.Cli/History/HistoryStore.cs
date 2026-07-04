using System.Text.Json;
using TestLens.Models;

namespace TestLens.History;

/// <summary>
/// Persists one JSON file per run under &lt;out&gt;/history and reads the full
/// run history back, ordered oldest to newest.
/// </summary>
public sealed class HistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public string OutputDir { get; }
    public string HistoryDir { get; }

    public HistoryStore(string outputDir)
    {
        OutputDir = outputDir;
        HistoryDir = Path.Combine(outputDir, "history");
    }

    public string Save(RunSnapshot snapshot)
    {
        Directory.CreateDirectory(HistoryDir);

        var baseId = snapshot.Timestamp.ToString("yyyyMMdd-HHmmss");
        var id = baseId;
        for (int n = 2; File.Exists(RunFile(id)); n++) id = $"{baseId}-{n}";
        snapshot.Id = id;

        var file = RunFile(id);
        File.WriteAllText(file, JsonSerializer.Serialize(snapshot, JsonOptions));
        return file;
    }

    public List<RunSnapshot> LoadAll()
    {
        if (!Directory.Exists(HistoryDir)) return new List<RunSnapshot>();

        var runs = new List<RunSnapshot>();
        foreach (var file in Directory.EnumerateFiles(HistoryDir, "run-*.json"))
        {
            try
            {
                var snapshot = JsonSerializer.Deserialize<RunSnapshot>(File.ReadAllText(file));
                if (snapshot is not null) runs.Add(snapshot);
            }
            catch (JsonException)
            {
                Console.Error.WriteLine($"warning: skipping unreadable history file {Path.GetFileName(file)}");
            }
        }
        return runs.OrderBy(r => r.Timestamp).ToList();
    }

    private string RunFile(string id) => Path.Combine(HistoryDir, $"run-{id}.json");
}
