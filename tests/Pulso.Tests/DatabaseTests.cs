using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using Pulso.Api;
using FluentAssertions;
using Xunit;

namespace Pulso.Tests;

public class DatabaseTests : IAsyncLifetime
{
    private string _testDbPath = null!;
    private string _testConnectionString = null!;

    public async Task InitializeAsync()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"pulso_test_{Guid.NewGuid()}.db");
        _testConnectionString = $"Data Source={_testDbPath};Cache=Shared;Pooling=true;";

        // Configurar connection string para testing
        Database.SetConnectionStringForTesting(_testConnectionString);

        // Usar el método InitializeAsync de producción para crear el esquema
        await Database.InitializeAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        if (File.Exists(_testDbPath))
        {
            try { File.Delete(_testDbPath); } catch { }
        }

        var walFile = _testDbPath + "-wal";
        var shmFile = _testDbPath + "-shm";
        if (File.Exists(walFile)) { try { File.Delete(walFile); } catch { } }
        if (File.Exists(shmFile)) { try { File.Delete(shmFile); } catch { } }

        // Reset connection string to default to not interfere with other tests
        Database.SetConnectionStringForTesting("Data Source=./data/pulso.db;Cache=Shared;Pooling=true;");
    }

    [Fact]
    public async Task InitializeAsync_CreatesSchemaWithTablesAndIndexes()
    {
        // El esquema ya se creó en InitializeAsync, verificamos que existe
        await using var connection = new SqliteConnection(_testConnectionString);
        await connection.OpenAsync();

        var tables = await connection.QueryAsync<string>(
            "SELECT name FROM sqlite_master WHERE type='table' AND name IN ('check_results', 'incidents');");

        tables.Should().Contain("check_results");
        tables.Should().Contain("incidents");

        var indexes = await connection.QueryAsync<string>(
            "SELECT name FROM sqlite_master WHERE type='index' AND name IN ('idx_checks_service_time', 'idx_incidents_service_time');");

        indexes.Should().Contain("idx_checks_service_time");
        indexes.Should().Contain("idx_incidents_service_time");
    }

    [Fact]
    public async Task InsertCheckResultsAsync_StoresCorrectlyViaProductionCode()
    {
        var results = new List<CheckResult>
        {
            new("test-service", DateTime.UtcNow, true, true, 15, 30, "UP"),
            new("test-service", DateTime.UtcNow.AddMinutes(-1), true, true, 12, 25, "UP")
        };

        await Database.InsertCheckResultsAsync(results);

        await using var connection = new SqliteConnection(_testConnectionString);
        await connection.OpenAsync();

        var count = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM check_results WHERE service_id = 'test-service';");

        count.Should().Be(2);
    }

    [Fact]
    public async Task CalculateUptime24hAsync_ReturnsCorrectPercentageViaProductionCode()
    {
        var now = DateTime.UtcNow;

        // Insertar 10 registros: 8 UP, 2 DOWN
        var results = new List<CheckResult>();
        for (int i = 0; i < 10; i++)
        {
            var status = i < 8 ? "UP" : "DOWN";
            results.Add(new CheckResult(
                "uptime-test",
                now.AddHours(-i),
                true,
                true,
                10,
                20,
                status));
        }

        await Database.InsertCheckResultsAsync(results);

        var uptime = await Database.CalculateUptime24hAsync("uptime-test");

        uptime.Should().Be(80.0);
    }

    [Fact]
    public async Task PurgeOldResultsAsync_RemovesDataOlderThan7DaysViaProductionCode()
    {
        var now = DateTime.UtcNow;

        // Insertar 1 registro antiguo (> 7 días) y 1 reciente (< 7 días)
        var oldResults = new List<CheckResult>
        {
            new("purge-test", now.AddDays(-8), true, true, 10, 20, "UP"),
            new("purge-test", now.AddDays(-1), true, true, 10, 20, "UP")
        };

        await Database.InsertCheckResultsAsync(oldResults);

        // Verificar que hay 2 registros antes de la purga
        await using var connection = new SqliteConnection(_testConnectionString);
        await connection.OpenAsync();

        var beforePurge = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM check_results WHERE service_id = 'purge-test';");

        beforePurge.Should().Be(2);

        // Ejecutar purga via código de producción
        await Database.PurgeOldResultsAsync();

        // Verificar que solo queda 1 registro después de la purga
        var afterPurge = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM check_results WHERE service_id = 'purge-test';");

        afterPurge.Should().Be(1);
    }

    [Fact]
    public async Task GetBars24hAsync_ReturnsCorrectArrayViaProductionCode()
    {
        var now = DateTime.UtcNow;

        // Insertar 24 horas de datos: 23 UP, 1 DOWN (la última hora)
        var results = new List<CheckResult>();
        for (int i = 23; i >= 0; i--)
        {
            var status = i == 0 ? "DOWN" : "UP";
            results.Add(new CheckResult(
                "bars-test",
                now.AddHours(-i),
                true,
                true,
                10,
                20,
                status));
        }

        await Database.InsertCheckResultsAsync(results);

        var bars = await Database.GetBars24hAsync("bars-test");

        bars.Should().HaveCount(24);
        bars[23].Should().Be("down"); // La hora más reciente (i=0) es DOWN
        bars[0].Should().Be("ok");    // La hora más antigua (i=23) es UP
    }

    [Fact]
    public async Task SeedInitialHistoryAsync_Creates24HoursOfDataFor5Services()
    {
        // Database.InitializeAsync ya se ejecutó en InitializeAsync del test
        // Verificamos que creó la semilla inicial

        await using var connection = new SqliteConnection(_testConnectionString);
        await connection.OpenAsync();

        var totalRecords = await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM check_results;");

        // 5 servicios * 24 horas = 120 registros
        totalRecords.Should().Be(120);

        var distinctServices = await connection.QueryAsync<string>(
            "SELECT DISTINCT service_id FROM check_results;");

        distinctServices.Should().HaveCount(5);
    }
}