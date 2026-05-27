using System.Text;
using Microsoft.Data.Sqlite;

namespace DeepSeekBrowser.Services.Harness;

/// <summary>.deepseek/sessions.db — 会话、patch、指标持久化。</summary>
public sealed class HarnessSessionStore
{
    private readonly string _dbPath;

    public HarnessSessionStore(string workspaceRoot)
    {
        var dir = Path.Combine(workspaceRoot, ".deepseek");
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, "sessions.db");
        EnsureSchema();
    }

    public void SaveRunStart(string sessionId, string runId, string prompt)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT OR REPLACE INTO runs(session_id, run_id, prompt, started_utc) VALUES ($s,$r,$p,$t)";
        cmd.Parameters.AddWithValue("$s", sessionId);
        cmd.Parameters.AddWithValue("$r", runId);
        cmd.Parameters.AddWithValue("$p", prompt);
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void SavePatchSnapshot(string runId, string path, string contentBefore)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO patch_snapshots(run_id, path, content_before, created_utc) VALUES ($r,$p,$c,$t)";
        cmd.Parameters.AddWithValue("$r", runId);
        cmd.Parameters.AddWithValue("$p", path);
        cmd.Parameters.AddWithValue("$c", contentBefore);
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void RecordPatchMetric(string runId, bool accepted)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = accepted
            ? "UPDATE runs SET patches_accepted = patches_accepted + 1 WHERE run_id = $r"
            : "UPDATE runs SET patches_rejected = patches_rejected + 1 WHERE run_id = $r";
        cmd.Parameters.AddWithValue("$r", runId);
        cmd.ExecuteNonQuery();
    }

    public bool TryUndoSessionRun(string runId, string workspaceRoot, out int restoredFiles)
    {
        restoredFiles = 0;
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT path, content_before FROM patch_snapshots WHERE run_id = $r ORDER BY id DESC";
        cmd.Parameters.AddWithValue("$r", runId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var rel = reader.GetString(0);
            var content = reader.GetString(1);
            try
            {
                var full = WorkspacePathGuard.ResolveUnderWorkspace(workspaceRoot, rel);
                File.WriteAllText(full, content, Encoding.UTF8);
                restoredFiles++;
            }
            catch
            {
                // skip
            }
        }

        return restoredFiles > 0;
    }

    private void EnsureSchema()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS runs(
              session_id TEXT,
              run_id TEXT PRIMARY KEY,
              prompt TEXT,
              started_utc TEXT,
              patches_accepted INT DEFAULT 0,
              patches_rejected INT DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS patch_snapshots(
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              run_id TEXT,
              path TEXT,
              content_before TEXT,
              created_utc TEXT
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection("Data Source=" + _dbPath);
        conn.Open();
        return conn;
    }
}
