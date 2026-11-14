using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pulso.Api;

namespace Pulso.Tests;

public class PulsoWebApplicationFactory : WebApplicationFactory<Program>
{
    private string _testDbPath = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((context, config) =>
        {
            // Use in-memory SQLite for tests
            _testDbPath = Path.Combine(Path.GetTempPath(), $"pulso_test_{Guid.NewGuid()}.db");
            var connString = $"Data Source={_testDbPath};Cache=Shared;Pooling=true;";
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Redis"] = "localhost:6379", // Will fail gracefully, HybridCache falls back to L1
                ["ConnectionStrings:Default"] = connString
            }!);

            // Sync Database static connection string so app startup uses test DB
            Database.SetConnectionStringForTesting(connString);
        });

        builder.ConfigureServices(services =>
        {
            // Remove Redis cache registration to avoid connection issues in tests
            var redisDescriptors = services.Where(d =>
                d.ServiceType.FullName?.Contains("Redis") == true ||
                d.ImplementationType?.FullName?.Contains("Redis") == true).ToList();
            foreach (var d in redisDescriptors)
            {
                services.Remove(d);
            }
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        return base.CreateHost(builder);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !string.IsNullOrEmpty(_testDbPath))
        {
            try { File.Delete(_testDbPath); } catch { }
            var walFile = _testDbPath + "-wal";
            var shmFile = _testDbPath + "-shm";
            if (File.Exists(walFile)) { try { File.Delete(walFile); } catch { } }
            if (File.Exists(shmFile)) { try { File.Delete(shmFile); } catch { } }
        }
        // Reset Database connection string to default to not interfere with other tests
        Database.SetConnectionStringForTesting("Data Source=./data/pulso.db;Cache=Shared;Pooling=true;");
        base.Dispose(disposing);
    }
}