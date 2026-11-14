using System.Net.Http.Headers;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Pulso.Api;

public sealed class MonitorWorker : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly HybridCache _cache;
    private readonly LabStateService _labState;
    private readonly ILogger<MonitorWorker> _logger;
    private readonly IConfiguration _configuration;
    private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(60));

    private static readonly string[] ServiceIds =
    [
        "nutrition-app",
        "n8n-automation",
        "santiago-portfolio",
        "api-gateway",
        "demo-service"
    ];

    public MonitorWorker(
        IHttpClientFactory httpClientFactory,
        HybridCache cache,
        LabStateService labState,
        ILogger<MonitorWorker> logger,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _labState = labState;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MonitorWorker iniciado - chequeo cada 60s");

        await RunChecksAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _timer.WaitForNextTickAsync(stoppingToken);
                await RunChecksAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en ciclo de monitoreo");
            }
        }
    }

    private async Task RunChecksAsync(CancellationToken ct)
    {
        var results = new List<CheckResult>();
        var now = DateTimeOffset.UtcNow;

        foreach (var serviceId in ServiceIds)
        {
            var result = await CheckServiceAsync(serviceId, now, ct);
            results.Add(result);
        }

        await Database.InsertCheckResultsAsync(results, ct);
        await Database.PurgeOldResultsAsync(ct);

        _logger.LogDebug("Ciclo completado: {Count} servicios verificados", results.Count);
    }

    private async Task<CheckResult> CheckServiceAsync(string serviceId, DateTimeOffset checkedAt, CancellationToken ct)
    {
        var internalUrl = _configuration[$"CHECK_INTERNAL_{serviceId.ToUpperInvariant().Replace("-", "_")}"];
        var publicUrl = _configuration[$"CHECK_PUBLIC_{serviceId.ToUpperInvariant().Replace("-", "_")}"];

        var (internalOk, internalLatency) = await CheckEndpointAsync("internal", internalUrl, serviceId, ct);
        var (publicOk, publicLatency) = await CheckEndpointAsync("public", publicUrl, serviceId, ct);

        var statusLabel = DetermineStatus(internalOk, publicOk);

        if (serviceId == "demo-service" && _labState.IsFailureSimulated())
        {
            statusLabel = "DESCONECTADO";
            _labState.SetStaleWhileRevalidate(true);
        }
        else if (serviceId == "demo-service")
        {
            _labState.SetStaleWhileRevalidate(false);
        }

        _labState.RecordLatency(publicLatency > 0 ? publicLatency : internalLatency);

        return new CheckResult(
            serviceId,
            checkedAt.UtcDateTime,
            internalOk,
            publicOk,
            internalLatency,
            publicLatency,
            statusLabel
        );
    }

    private async Task<(bool Ok, int LatencyMs)> CheckEndpointAsync(string label, string? url, string serviceId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            _logger.LogDebug("Sin URL configurada para {Label} check de {ServiceId}", label, serviceId);
            return (false, 0);
        }

        var client = _httpClientFactory.CreateClient("monitor");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var response = await client.GetAsync(url, ct);
            stopwatch.Stop();

            if (response.IsSuccessStatusCode)
            {
                return (true, (int)stopwatch.ElapsedMilliseconds);
            }

            _logger.LogWarning("{Label} check falló para {ServiceId}: {StatusCode}", label, serviceId, response.StatusCode);
            return (false, (int)stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "{Label} check error para {ServiceId}: {Message}", label, serviceId, ex.Message);
            return (false, (int)stopwatch.ElapsedMilliseconds);
        }
    }

    private static string DetermineStatus(bool internalOk, bool publicOk)
    {
        if (internalOk && publicOk) return "UP";
        if (internalOk && !publicOk) return "FALLO DE TÚNEL";
        return "DESCONECTADO";
    }
}