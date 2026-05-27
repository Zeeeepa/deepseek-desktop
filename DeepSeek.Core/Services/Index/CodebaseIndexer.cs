using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using DeepSeekBrowser.Services.Harness.Memory;

namespace DeepSeekBrowser.Services.Index;

public sealed class CodebaseChunk
{
    public required string Id { get; init; }
    public required string Path { get; init; }
    public required string Text { get; init; }
    public string? Symbol { get; init; }
}

public sealed class CodebaseSearchHit
{
    public required string Path { get; init; }
    public required string Snippet { get; init; }
    public double Score { get; init; }
}

/// <summary>工作区代码索引：文件监听 + BM25（向量槽位预留）。</summary>
public sealed class CodebaseIndexer : IDisposable
{
    private static readonly ConcurrentDictionary<string, CodebaseIndexer> Instances = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "bin", "obj", ".deepseek", "dist", "build", ".vs"
    };

    private readonly string _root;
    private readonly object _lock = new();
    private List<CodebaseChunk> _chunks = [];
    private HarnessBm25Index? _bm25;
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;

    private CodebaseIndexer(string workspaceRoot)
    {
        _root = Path.GetFullPath(workspaceRoot);
    }

    public static CodebaseIndexer GetOrCreate(string workspaceRoot)
    {
        var key = Path.GetFullPath(workspaceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return Instances.GetOrAdd(key, r => new CodebaseIndexer(r));
    }

    public void EnsureWatching()
    {
        if (_watcher is not null) return;
        try
        {
            _watcher = new FileSystemWatcher(_root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            _watcher.Changed += (_, _) => ScheduleRebuild();
            _watcher.Created += (_, _) => ScheduleRebuild();
            _watcher.Deleted += (_, _) => ScheduleRebuild();
            _watcher.Renamed += (_, _) => ScheduleRebuild();
            _watcher.EnableRaisingEvents = true;
        }
        catch
        {
            // read-only or permission issue
        }

        Rebuild();
    }

    public IReadOnlyList<CodebaseSearchHit> HybridSearch(string query, int topK = 5)
    {
        lock (_lock)
        {
            if (_bm25 is null || _chunks.Count == 0)
                RebuildCore();
            if (_bm25 is null)
                return Array.Empty<CodebaseSearchHit>();

            var ranked = _bm25.Rank(query, topK * 2);
            return ranked
                .Select(r =>
                {
                    var chunk = _chunks.FirstOrDefault(c => c.Id == r.Id);
                    return chunk is null
                        ? null
                        : new CodebaseSearchHit
                        {
                            Path = chunk.Path,
                            Snippet = Truncate(chunk.Text, 600),
                            Score = r.Score
                        };
                })
                .Where(h => h is not null)
                .Cast<CodebaseSearchHit>()
                .Take(topK)
                .ToList();
        }
    }

    public IndexStatus GetStatus()
    {
        lock (_lock)
        {
            return new IndexStatus(_chunks.Count, LastRebuildUtc);
        }
    }

    public DateTime? LastRebuildUtc { get; private set; }

    public readonly record struct IndexStatus(int ChunkCount, DateTime? LastUpdatedUtc);

    private void ScheduleRebuild()
    {
        _debounce?.Dispose();
        _debounce = new Timer(_ => Rebuild(), null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
    }

    public void Rebuild()
    {
        lock (_lock)
            RebuildCore();
    }

    private void RebuildCore()
    {
        var chunks = new List<CodebaseChunk>();
        if (!Directory.Exists(_root))
        {
            _chunks = chunks;
            _bm25 = HarnessBm25Index.Build([]);
            return;
        }

        foreach (var file in SafeEnumerateSourceFiles(_root))
        {
            try
            {
                var rel = Path.GetRelativePath(_root, file).Replace('\\', '/');
                var text = File.ReadAllText(file, Encoding.UTF8);
                if (text.Length > 200_000)
                    text = text[..200_000];
                foreach (var piece in ChunkFile(rel, text))
                    chunks.Add(piece);
            }
            catch
            {
                // skip unreadable
            }
        }

        _chunks = chunks;
        _bm25 = HarnessBm25Index.Build(chunks.Select(c => (c.Id, c.Text)).ToList());
        LastRebuildUtc = DateTime.UtcNow;
        PersistMeta();
    }

    private void PersistMeta()
    {
        try
        {
            var dir = Path.Combine(_root, ".deepseek", "index");
            Directory.CreateDirectory(dir);
            var meta = new { chunkCount = _chunks.Count, updatedUtc = LastRebuildUtc };
            File.WriteAllText(
                Path.Combine(dir, "meta.json"),
                JsonSerializer.Serialize(meta),
                Encoding.UTF8);
        }
        catch
        {
            // ignore
        }
    }

    private static IEnumerable<string> SafeEnumerateSourceFiles(string root)
    {
        var files = new List<string>();
        try
        {
            files.AddRange(EnumerateSourceFiles(root));
        }
        catch
        {
            // ignore traversal errors
        }

        return files;
    }

    private static IEnumerable<string> EnumerateSourceFiles(string root)
    {
        var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".go", ".rs", ".java", ".md", ".json", ".html", ".css", ".sql"
        };
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            if (ShouldSkip(file, root)) continue;
            if (!exts.Contains(Path.GetExtension(file))) continue;
            yield return file;
        }
    }

    private static bool ShouldSkip(string fullPath, string root)
    {
        var rel = Path.GetRelativePath(root, fullPath);
        foreach (var part in rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (SkipDirs.Contains(part))
                return true;
        }

        return false;
    }

    private static IEnumerable<CodebaseChunk> ChunkFile(string rel, string text)
    {
        const int size = 80;
        var lines = text.Split('\n');
        var buf = new StringBuilder();
        var startLine = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            buf.AppendLine(lines[i]);
            if (buf.Length < size * 40 && i < lines.Length - 1)
                continue;
            var chunkText = buf.ToString();
            var sym = ExtractSymbol(lines, startLine);
            yield return new CodebaseChunk
            {
                Id = rel + "#" + startLine,
                Path = rel,
                Text = chunkText,
                Symbol = sym
            };
            buf.Clear();
            startLine = i + 1;
        }
    }

    private static string? ExtractSymbol(string[] lines, int start)
    {
        for (var i = start; i < Math.Min(lines.Length, start + 5); i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("class ", StringComparison.Ordinal)
                || line.StartsWith("public class ", StringComparison.Ordinal)
                || line.StartsWith("function ", StringComparison.Ordinal)
                || line.StartsWith("def ", StringComparison.Ordinal))
                return line.Length > 120 ? line[..120] : line;
        }

        return null;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce?.Dispose();
    }
}
