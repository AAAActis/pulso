using System.Data;
using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Pulso.Api;

public static class Database
{
    private static string _connectionString = "Data Source=./data/pulso.db;Cache=Shared;Pooling=true;";

    public static void SetConnectionStringForTesting(string connectionString)
    {
        _connectionString = connectionString;
    }

    public static string GetConnectionString() => _connectionString;

    private static readonly string[] ServiceIds =
    [
        "nutrition-app",
        "n8n-automation",
        "santiago-portfolio",
        "api-gateway",
        "demo-service"
    ];

    public static async Task InitializeAsync(CancellationToken ct = default)
    {
        var dataDir = Path.GetDirectoryName(_connectionString.Split('=')[1].Split(';')[0]);
        if (!string.IsNullOrEmpty(dataDir))
        {
            Directory.CreateDirectory(dataDir);
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await connection.ExecuteAsync(@"
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;

            CREATE TABLE IF NOT EXISTS check_results (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                service_id TEXT NOT NULL,
                checked_at_utc TEXT NOT NULL,
                internal_ok INTEGER NOT NULL,
                public_ok INTEGER NOT NULL,
                internal_latency_ms INTEGER NOT NULL,
                public_latency_ms INTEGER NOT NULL,
                status_label TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS incidents (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                service_id TEXT NOT NULL,
                started_at_utc TEXT NOT NULL,
                ended_at_utc TEXT,
                status_label TEXT NOT NULL,
                details TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_checks_service_time
            ON check_results(service_id, checked_at_utc DESC);

            CREATE INDEX IF NOT EXISTS idx_incidents_service_time
            ON incidents(service_id, started_at_utc DESC);");

        var count = await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM check_results;");

        if (count == 0)
        {
            await SeedInitialHistoryAsync(connection, ct);
        }
    }

    private static async Task SeedInitialHistoryAsync(SqliteConnection connection, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var parameters = new DynamicParameters();

        for (int i = 23; i >= 0; i--)
        {
            var checkedAt = now.AddHours(-i);
            var checkedAtStr = checkedAt.ToString("yyyy-MM-ddTHH:mm:ssZ");

            foreach (var serviceId in ServiceIds)
            {
                var safeId = serviceId.Replace("-", "_");
                parameters.Add($"service_id_{safeId}_{i}", serviceId);
                parameters.Add($"checked_at_{safeId}_{i}", checkedAtStr);
                parameters.Add($"internal_ok_{safeId}_{i}", 1);
                parameters.Add($"public_ok_{safeId}_{i}", 1);
                parameters.Add($"internal_latency_{safeId}_{i}", Random.Shared.Next(8, 25));
                parameters.Add($"public_latency_{safeId}_{i}", Random.Shared.Next(15, 50));
                parameters.Add($"status_label_{safeId}_{i}", "UP");
            }
        }

        var sql = new StringBuilder();
        sql.Append("INSERT INTO check_results (service_id, checked_at_utc, internal_ok, public_ok, internal_latency_ms, public_latency_ms, status_label) VALUES ");

        var valueClauses = new List<string>();
        for (int i = 23; i >= 0; i--)
        {
            foreach (var serviceId in ServiceIds)
            {
                var safeId = serviceId.Replace("-", "_");
                valueClauses.Add($"(@service_id_{safeId}_{i}, @checked_at_{safeId}_{i}, @internal_ok_{safeId}_{i}, @public_ok_{safeId}_{i}, @internal_latency_{safeId}_{i}, @public_latency_{safeId}_{i}, @status_label_{safeId}_{i})");
            }
        }

        sql.Append(string.Join(", ", valueClauses));
        sql.Append(";");

        await connection.ExecuteAsync(sql.ToString(), parameters);
    }

    public static async Task InsertCheckResultsAsync(IEnumerable<CheckResult> results, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        const string sql = """
            INSERT INTO check_results (service_id, checked_at_utc, internal_ok, public_ok, internal_latency_ms, public_latency_ms, status_label)
            VALUES (@ServiceId, @CheckedAtUtc, @InternalOk, @PublicOk, @InternalLatencyMs, @PublicLatencyMs, @StatusLabel);
            """;

        await connection.ExecuteAsync(sql, results);
    }

    public static async Task PurgeOldResultsAsync(CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await connection.ExecuteAsync("DELETE FROM check_results WHERE checked_at_utc < datetime('now', '-7 days');");
        await connection.ExecuteAsync("DELETE FROM incidents WHERE started_at_utc < datetime('now', '-7 days');");
    }

    public static async Task<double> CalculateUptime24hAsync(string serviceId, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        var total = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM check_results WHERE service_id = @serviceId AND checked_at_utc >= datetime('now', '-24 hours');",
            new { serviceId });

        if (total == 0) return 100.0;

        var upCount = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM check_results WHERE service_id = @serviceId AND checked_at_utc >= datetime('now', '-24 hours') AND status_label = 'UP';",
            new { serviceId });

        return Math.Round((double)upCount / total * 100, 2);
    }

    public static async Task<double> CalculateUptime7dAsync(string serviceId, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        var total = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM check_results WHERE service_id = @serviceId AND checked_at_utc >= datetime('now', '-7 days');",
            new { serviceId });

        if (total == 0) return 100.0;

        var upCount = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM check_results WHERE service_id = @serviceId AND checked_at_utc >= datetime('now', '-7 days') AND status_label = 'UP';",
            new { serviceId });

        return Math.Round((double)upCount / total * 100, 2);
    }

    public static async Task<IReadOnlyList<string>> GetBars24hAsync(string serviceId, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        var rows = await connection.QueryAsync<dynamic>(
            """
            SELECT status_label
            FROM check_results
            WHERE service_id = @serviceId AND checked_at_utc >= datetime('now', '-24 hours')
            ORDER BY checked_at_utc ASC
            LIMIT 24;
            """,
            new { serviceId });

        return rows.Select(r => r.status_label == "UP" ? "ok" : r.status_label == "WARN" ? "warn" : "down").ToList();
    }

    public static async Task<(int P50, int P95)> GetLatencyPercentilesAsync(CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        var latencies = await connection.QueryAsync<int>(
            """
            SELECT public_latency_ms
            FROM check_results
            WHERE checked_at_utc >= datetime('now', '-30 minutes')
            ORDER BY public_latency_ms;
            """);

        var list = latencies.ToList();
        if (list.Count == 0) return (12, 45);

        list.Sort();
        var p50 = list[list.Count / 2];
        var p95 = list[(int)Math.Ceiling(list.Count * 0.95) - 1];

        return (p50, p95);
    }
}