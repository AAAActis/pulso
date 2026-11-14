using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Data.Sqlite;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.RateLimiting;
using Pulso.Api;
using Polly;
using Dapper;

var builder = WebApplication.CreateBuilder(args);

// ─── Configuración de servicios ───

builder.Services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromSeconds(60),
        LocalCacheExpiration = TimeSpan.FromSeconds(15)
    };
});

// Redis L2 cache for HybridCache (optional - falls back to L1 only if unavailable)
var redisConn = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConn))
{
    try
    {
        builder.Services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConn;
            options.InstanceName = "Pulso";
        });
    }
    catch
    {
        // Redis unavailable - HybridCache will use L1 only
    }
}

builder.Services.AddHttpClient("monitor", client =>
{
    client.Timeout = TimeSpan.FromSeconds(3);
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
})
.AddStandardResilienceHandler(options =>
{
    options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(3);
    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
    options.Retry.MaxRetryAttempts = 1;
    options.Retry.Delay = TimeSpan.FromMilliseconds(200);
    options.Retry.BackoffType = DelayBackoffType.Exponential;
    options.Retry.UseJitter = true;
    options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
    options.CircuitBreaker.MinimumThroughput = 5;
    options.CircuitBreaker.FailureRatio = 0.5;
});

builder.Services.AddSingleton<LabStateService>();
builder.Services.AddHostedService<MonitorWorker>();

// ─── Rate Limiting ───
var globalRateLimit = builder.Environment.IsDevelopment() ? 10000 : 60;
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var ip = ctx.Request.Headers["CF-Connecting-IP"].FirstOrDefault()
                 ?? ctx.Connection.RemoteIpAddress?.ToString()
                 ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = globalRateLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });

    options.AddFixedWindowLimiter("lab-simulate", limiterOptions =>
    {
        limiterOptions.PermitLimit = 5;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 0;
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// ─── CORS ───
var allowedOrigin = "https://s-portfolio.bbasolutions.ar";
builder.Services.AddCors(options =>
{
    options.AddPolicy("strict", policy =>
    {
        policy.WithOrigins(allowedOrigin)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()
              .SetPreflightMaxAge(TimeSpan.FromHours(1));
    });

    if (builder.Environment.IsDevelopment())
    {
        options.AddPolicy("dev", policy =>
        {
            policy.WithOrigins("http://localhost:5000", "http://localhost:5001", "http://127.0.0.1:5000", "http://127.0.0.1:5001")
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
    }
});

// ─── ProblemDetails (RFC 9457) ───
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ctx =>
    {
        ctx.ProblemDetails.Instance = ctx.HttpContext.Request.Path;
        ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
    };
});

var app = builder.Build();

// ─── Middleware pipeline ───

// Security Headers
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";

    var csp = "default-src 'self'; " +
              "script-src 'self' 'unsafe-inline'; " +
              "style-src 'self' 'unsafe-inline'; " +
              "img-src 'self' data:; " +
              "font-src 'self'; " +
              "connect-src 'self'; " +
              "frame-ancestors 'none'; " +
              "base-uri 'self'; " +
              "form-action 'self'";
    context.Response.Headers["Content-Security-Policy"] = csp;

    await next();
});

app.UseExceptionHandler("/error");
app.Map("/error", (HttpContext ctx) => Results.Problem(instance: ctx.Request.Path, extensions: new Dictionary<string, object?> { ["traceId"] = ctx.TraceIdentifier }));
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseCors("dev");
    // Rate limiting disabled in Development for testing
}
else
{
    app.UseCors("strict");
    app.UseRateLimiter();
}

// Middleware de medición de latencia real
app.Use(async (context, next) =>
{
    var labState = context.RequestServices.GetRequiredService<LabStateService>();
    var sw = System.Diagnostics.Stopwatch.StartNew();

    try
    {
        await next();
    }
    finally
    {
        sw.Stop();
        if (context.Request.Path.StartsWithSegments("/api/v1"))
        {
            labState.RecordLatency((int)sw.ElapsedMilliseconds);
        }
    }
});

// ─── Health Endpoints ───
app.MapGet("/health/live", () => new HealthResponse("Healthy"))
   .AllowAnonymous()
   .DisableRateLimiting();

app.MapGet("/health/ready", async (HttpContext ctx) =>
{
    try
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(Database.GetConnectionString());
        await connection.OpenAsync();
        return Results.Ok(new HealthResponse("Healthy"));
    }
    catch
    {
        return Results.Problem("Database not ready", statusCode: 503);
    }
})
   .AllowAnonymous()
   .DisableRateLimiting();

// ─── Helpers para ETag ───
static string ComputeETag(object obj)
{
    var json = System.Text.Json.JsonSerializer.Serialize(obj);
    using var sha = System.Security.Cryptography.SHA256.Create();
    var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
    return $"\"{Convert.ToHexString(hash)[..16]}\"";
}

// ─── Endpoint /api/v1/summary ───
app.MapGet("/api/v1/summary", async (HttpContext ctx, HybridCache cache, CancellationToken ct) =>
{
    var etag = ctx.Request.Headers.IfNoneMatch.FirstOrDefault();

    var summary = await cache.GetOrCreateAsync("summary", async _ =>
    {
        var uptime24h = 0.0;
        var uptime7d = 0.0;
        var serviceIds = new[] { "nutrition-app", "n8n-automation", "santiago-portfolio", "api-gateway", "demo-service" };

        foreach (var id in serviceIds)
        {
            uptime24h += await Database.CalculateUptime24hAsync(id, ct);
            uptime7d += await Database.CalculateUptime7dAsync(id, ct);
        }

        uptime24h = Math.Round(uptime24h / serviceIds.Length, 2);
        uptime7d = Math.Round(uptime7d / serviceIds.Length, 2);

        var labState = ctx.RequestServices.GetRequiredService<LabStateService>();

        return new SummaryDto(
            GlobalStatus: uptime24h >= 99.5 ? "healthy" : uptime24h >= 95 ? "degraded" : "critical",
            GlobalLabel: uptime24h >= 99.5 ? "Sistemas Operativos" : uptime24h >= 95 ? "Degradado" : "Crítico",
            Uptime24h: uptime24h,
            Uptime7d: uptime7d,
            CacheHitRatio: labState.HitRatioPercent,
            SimulationActive: labState.IsFailureSimulated(),
            TimestampUtc: DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
        );
    }, cancellationToken: ct);

    var currentETag = ComputeETag(summary);

    if (etag == currentETag)
    {
        return Results.StatusCode(StatusCodes.Status304NotModified);
    }

    ctx.Response.Headers.ETag = currentETag;
    ctx.Response.Headers.CacheControl = "public, max-age=15, stale-while-revalidate=60";

    return Results.Ok(summary);
})
.RequireCors(app.Environment.IsDevelopment() ? "dev" : "strict")
.RequireRateLimiting("lab-simulate"); // Using lab-simulate limiter for tighter control on summary too

// ─── Endpoint /api/v1/services ───
app.MapGet("/api/v1/services", async (HttpContext ctx, HybridCache cache, CancellationToken ct) =>
{
    var servicesResponse = await cache.GetOrCreateAsync("services", async _ =>
    {
        var serviceIds = new[] { "nutrition-app", "n8n-automation", "santiago-portfolio", "api-gateway", "demo-service" };
        var services = new List<ServiceDto>();

        foreach (var id in serviceIds)
        {
            var uptime24h = await Database.CalculateUptime24hAsync(id, ct);
            var bars24h = await Database.GetBars24hAsync(id, ct);

            // Determine latest status from DB
            await using var conn = new Microsoft.Data.Sqlite.SqliteConnection(Database.GetConnectionString());
            await conn.OpenAsync(ct);
            var latest = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT status_label, internal_ok, public_ok FROM check_results WHERE service_id = @id ORDER BY checked_at_utc DESC LIMIT 1;",
                new { id });

            string internalStatus = "DOWN";
            string publicStatus = "DOWN";

            if (latest != null)
            {
                internalStatus = Convert.ToInt64(latest.internal_ok) == 1 ? "UP" : "DOWN";
                publicStatus = Convert.ToInt64(latest.public_ok) == 1 ? "UP" : "DOWN";
            }

            var displayName = id switch
            {
                "nutrition-app" => "Nutrition App",
                "n8n-automation" => "n8n Automation",
                "santiago-portfolio" => "Santiago Portfolio",
                "api-gateway" => "API Gateway",
                "demo-service" => "Demo Service",
                _ => id.Split('-').Select(w => char.ToUpper(w[0]) + w[1..]).Aggregate((a, b) => a + " " + b)
            };

            services.Add(new ServiceDto(
                Id: id,
                Name: displayName,
                InternalStatus: internalStatus,
                PublicStatus: publicStatus,
                Uptime24h: uptime24h,
                Bars24h: bars24h
            ));
        }

        var labState = ctx.RequestServices.GetRequiredService<LabStateService>();
        var (p50, p95) = await Database.GetLatencyPercentilesAsync(ct);

        var lab = new LabDto(
            LatencySeries: new List<LatencyPoint> { new(p50, p95) },
            CurrentP50Ms: p50,
            CurrentP95Ms: p95,
            CacheAvailabilityPercent: 100.0, // HybridCache always available
            HitRatioPercent: labState.HitRatioPercent,
            StaleWhileRevalidateActive: labState.StaleWhileRevalidateActive,
            CircuitBreakerState: labState.IsCircuitBreakerOpen ? "OPEN" : "CLOSED",
            SimulationActive: labState.IsFailureSimulated(),
            SimulationRemainingSeconds: labState.SimulateFailureUntil.HasValue
                ? (int)(labState.SimulateFailureUntil.Value - DateTimeOffset.UtcNow).TotalSeconds
                : 0
        );

        return new ServicesResponseDto(
            CacheStatus: "HIT",
            StaleWhileRevalidate: labState.StaleWhileRevalidateActive,
            Services: services,
            Lab: lab
        );
    }, cancellationToken: ct);

    ctx.Response.Headers.CacheControl = "public, max-age=15, stale-while-revalidate=60";
    return Results.Ok(servicesResponse);
})
.RequireCors(app.Environment.IsDevelopment() ? "dev" : "strict");

// ─── SSE Endpoint /api/v1/stream ───
app.MapGet("/api/v1/stream", async (HttpContext ctx, HybridCache cache, CancellationToken ct) =>
{
    ctx.Response.Headers.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection = "keep-alive";

    var labState = ctx.RequestServices.GetRequiredService<LabStateService>();
    var lastPush = DateTimeOffset.MinValue;

    // Send initial event immediately
    try
    {
        var initialSummary = await cache.GetOrCreateAsync("summary", async _ =>
        {
            var uptime24h = 0.0;
            var uptime7d = 0.0;
            var serviceIds = new[] { "nutrition-app", "n8n-automation", "santiago-portfolio", "api-gateway", "demo-service" };

            foreach (var id in serviceIds)
            {
                uptime24h += await Database.CalculateUptime24hAsync(id, ct);
                uptime7d += await Database.CalculateUptime7dAsync(id, ct);
            }

            uptime24h = Math.Round(uptime24h / serviceIds.Length, 2);
            uptime7d = Math.Round(uptime7d / serviceIds.Length, 2);

            return new SummaryDto(
                GlobalStatus: uptime24h >= 99.5 ? "healthy" : uptime24h >= 95 ? "degraded" : "critical",
                GlobalLabel: uptime24h >= 99.5 ? "Sistemas Operativos" : uptime24h >= 95 ? "Degradado" : "Crítico",
                Uptime24h: uptime24h,
                Uptime7d: uptime7d,
                CacheHitRatio: labState.HitRatioPercent,
                SimulationActive: labState.IsFailureSimulated(),
                TimestampUtc: DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
            );
        }, cancellationToken: ct);

        var initialServices = await cache.GetOrCreateAsync("services", async _ =>
        {
            var serviceIds = new[] { "nutrition-app", "n8n-automation", "santiago-portfolio", "api-gateway", "demo-service" };
            var services = new List<ServiceDto>();

            foreach (var id in serviceIds)
            {
                var uptime24h = await Database.CalculateUptime24hAsync(id, ct);
                var bars24h = await Database.GetBars24hAsync(id, ct);

                await using var conn = new Microsoft.Data.Sqlite.SqliteConnection(Database.GetConnectionString());
                await conn.OpenAsync(ct);
                var latest = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT status_label, internal_ok, public_ok FROM check_results WHERE service_id = @id ORDER BY checked_at_utc DESC LIMIT 1;",
                    new { id });

                string internalStatus = "DOWN";
                string publicStatus = "DOWN";

                if (latest != null)
                {
                    internalStatus = Convert.ToInt64(latest.internal_ok) == 1 ? "UP" : "DOWN";
                    publicStatus = Convert.ToInt64(latest.public_ok) == 1 ? "UP" : "DOWN";
                }

                var displayName = id switch
                {
                    "nutrition-app" => "Nutrition App",
                    "n8n-automation" => "n8n Automation",
                    "santiago-portfolio" => "Santiago Portfolio",
                    "api-gateway" => "API Gateway",
                    "demo-service" => "Demo Service",
                    _ => id.Split('-').Select(w => char.ToUpper(w[0]) + w[1..]).Aggregate((a, b) => a + " " + b)
                };

                services.Add(new ServiceDto(
                    Id: id,
                    Name: displayName,
                    InternalStatus: internalStatus,
                    PublicStatus: publicStatus,
                    Uptime24h: uptime24h,
                    Bars24h: bars24h
                ));
            }

            var (p50, p95) = await Database.GetLatencyPercentilesAsync(ct);

            return new ServicesResponseDto(
                CacheStatus: "HIT",
                StaleWhileRevalidate: labState.StaleWhileRevalidateActive,
                Services: services,
                Lab: new LabDto(
                    LatencySeries: new List<LatencyPoint> { new(p50, p95) },
                    CurrentP50Ms: p50,
                    CurrentP95Ms: p95,
                    CacheAvailabilityPercent: 100.0,
                    HitRatioPercent: labState.HitRatioPercent,
                    StaleWhileRevalidateActive: labState.StaleWhileRevalidateActive,
                    CircuitBreakerState: labState.IsCircuitBreakerOpen ? "OPEN" : "CLOSED",
                    SimulationActive: labState.IsFailureSimulated(),
                    SimulationRemainingSeconds: labState.SimulateFailureUntil.HasValue
                        ? (int)(labState.SimulateFailureUntil.Value - DateTimeOffset.UtcNow).TotalSeconds
                        : 0
                )
            );
        }, cancellationToken: ct);

        var (ip50, ip95) = await Database.GetLatencyPercentilesAsync(ct);
        var initialLab = new LabDto(
            LatencySeries: new List<LatencyPoint> { new(ip50, ip95) },
            CurrentP50Ms: ip50,
            CurrentP95Ms: ip95,
            CacheAvailabilityPercent: 100.0,
            HitRatioPercent: labState.HitRatioPercent,
            StaleWhileRevalidateActive: labState.StaleWhileRevalidateActive,
            CircuitBreakerState: labState.IsCircuitBreakerOpen ? "OPEN" : "CLOSED",
            SimulationActive: labState.IsFailureSimulated(),
            SimulationRemainingSeconds: labState.SimulateFailureUntil.HasValue
                ? (int)(labState.SimulateFailureUntil.Value - DateTimeOffset.UtcNow).TotalSeconds
                : 0
        );

        var initialPulse = new PulseEvent(initialSummary, initialServices, initialLab);
        var initialJson = System.Text.Json.JsonSerializer.Serialize(initialPulse, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

        await ctx.Response.WriteAsync($"event: pulse\ndata: {initialJson}\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"SSE initial event error: {ex.Message}");
    }

    try
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(3000, ct);

            var summary = await cache.GetOrCreateAsync("summary", async _ =>
            {
                var uptime24h = 0.0;
                var uptime7d = 0.0;
                var serviceIds = new[] { "nutrition-app", "n8n-automation", "santiago-portfolio", "api-gateway", "demo-service" };

                foreach (var id in serviceIds)
                {
                    uptime24h += await Database.CalculateUptime24hAsync(id, ct);
                    uptime7d += await Database.CalculateUptime7dAsync(id, ct);
                }

                uptime24h = Math.Round(uptime24h / serviceIds.Length, 2);
                uptime7d = Math.Round(uptime7d / serviceIds.Length, 2);

                return new SummaryDto(
                    GlobalStatus: uptime24h >= 99.5 ? "healthy" : uptime24h >= 95 ? "degraded" : "critical",
                    GlobalLabel: uptime24h >= 99.5 ? "Sistemas Operativos" : uptime24h >= 95 ? "Degradado" : "Crítico",
                    Uptime24h: uptime24h,
                    Uptime7d: uptime7d,
                    CacheHitRatio: labState.HitRatioPercent,
                    SimulationActive: labState.IsFailureSimulated(),
                    TimestampUtc: DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                );
            }, cancellationToken: ct);

            var servicesResponse = await cache.GetOrCreateAsync("services", async _ =>
            {
                var serviceIds = new[] { "nutrition-app", "n8n-automation", "santiago-portfolio", "api-gateway", "demo-service" };
                var services = new List<ServiceDto>();

                foreach (var id in serviceIds)
                {
                    var uptime24h = await Database.CalculateUptime24hAsync(id, ct);
                    var bars24h = await Database.GetBars24hAsync(id, ct);

                    await using var conn = new Microsoft.Data.Sqlite.SqliteConnection(Database.GetConnectionString());
                    await conn.OpenAsync(ct);
                    var latest = await conn.QueryFirstOrDefaultAsync<dynamic>(
                        "SELECT status_label, internal_ok, public_ok FROM check_results WHERE service_id = @id ORDER BY checked_at_utc DESC LIMIT 1;",
                        new { id });

                    string internalStatus = "DOWN";
                    string publicStatus = "DOWN";

                    if (latest != null)
                    {
                        internalStatus = Convert.ToInt64(latest.internal_ok) == 1 ? "UP" : "DOWN";
                        publicStatus = Convert.ToInt64(latest.public_ok) == 1 ? "UP" : "DOWN";
                    }

                    var displayName = id switch
                    {
                        "nutrition-app" => "Nutrition App",
                        "n8n-automation" => "n8n Automation",
                        "santiago-portfolio" => "Santiago Portfolio",
                        "api-gateway" => "API Gateway",
                        "demo-service" => "Demo Service",
                        _ => id.Split('-').Select(w => char.ToUpper(w[0]) + w[1..]).Aggregate((a, b) => a + " " + b)
                    };

                    services.Add(new ServiceDto(
                        Id: id,
                        Name: displayName,
                        InternalStatus: internalStatus,
                        PublicStatus: publicStatus,
                        Uptime24h: uptime24h,
                        Bars24h: bars24h
                    ));
                }

                var (p50, p95) = await Database.GetLatencyPercentilesAsync(ct);

                return new ServicesResponseDto(
                    CacheStatus: "HIT",
                    StaleWhileRevalidate: labState.StaleWhileRevalidateActive,
                    Services: services,
                    Lab: new LabDto(
                        LatencySeries: new List<LatencyPoint> { new(p50, p95) },
                        CurrentP50Ms: p50,
                        CurrentP95Ms: p95,
                        CacheAvailabilityPercent: 100.0,
                        HitRatioPercent: labState.HitRatioPercent,
                        StaleWhileRevalidateActive: labState.StaleWhileRevalidateActive,
                        CircuitBreakerState: labState.IsCircuitBreakerOpen ? "OPEN" : "CLOSED",
                        SimulationActive: labState.IsFailureSimulated(),
                        SimulationRemainingSeconds: labState.SimulateFailureUntil.HasValue
                            ? (int)(labState.SimulateFailureUntil.Value - DateTimeOffset.UtcNow).TotalSeconds
                            : 0
                    )
                );
            }, cancellationToken: ct);

            var (sp50, sp95) = await Database.GetLatencyPercentilesAsync(ct);
            var lab = new LabDto(
                LatencySeries: new List<LatencyPoint> { new(sp50, sp95) },
                CurrentP50Ms: sp50,
                CurrentP95Ms: sp95,
                CacheAvailabilityPercent: 100.0,
                HitRatioPercent: labState.HitRatioPercent,
                StaleWhileRevalidateActive: labState.StaleWhileRevalidateActive,
                CircuitBreakerState: labState.IsCircuitBreakerOpen ? "OPEN" : "CLOSED",
                SimulationActive: labState.IsFailureSimulated(),
                SimulationRemainingSeconds: labState.SimulateFailureUntil.HasValue
                    ? (int)(labState.SimulateFailureUntil.Value - DateTimeOffset.UtcNow).TotalSeconds
                    : 0
            );

            var pulseEvent = new PulseEvent(summary, servicesResponse, lab);

            var json = System.Text.Json.JsonSerializer.Serialize(pulseEvent, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });

            try
            {
                await ctx.Response.WriteAsync($"event: pulse\ndata: {json}\n\n", ct);
                await ctx.Response.Body.FlushAsync(ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SSE write error: {ex.Message}");
                break;
            }

            // Push inmediato si cambió simulación
            if (lab.SimulationActive && lastPush != DateTimeOffset.MinValue)
            {
                var diff = DateTimeOffset.UtcNow - lastPush;
                if (diff.TotalSeconds < 3) continue;
            }
            lastPush = DateTimeOffset.UtcNow;
        }
    }
    catch (OperationCanceledException) { }
    catch (Exception ex)
    {
        Console.WriteLine($"SSE loop error: {ex.Message}");
    }
})
.RequireCors(app.Environment.IsDevelopment() ? "dev" : "strict")
.AllowAnonymous()
.DisableRateLimiting();

// ─── Endpoint /api/v1/lab/simulate-failure ───
app.MapPost("/api/v1/lab/simulate-failure", async (SimulateFailureRequest req, HttpContext ctx, CancellationToken ct) =>
{
    var labState = ctx.RequestServices.GetRequiredService<LabStateService>();
    var response = labState.StartFailureSimulation(45);

    // Invalidate cache to force fresh data on next request
    var cache = ctx.RequestServices.GetRequiredService<HybridCache>();
    await cache.RemoveAsync("summary", ct);
    await cache.RemoveAsync("services", ct);

    return Results.Ok(response);
})
.RequireCors(app.Environment.IsDevelopment() ? "dev" : "strict")
.RequireRateLimiting("lab-simulate");

// ─── Static files ───
app.UseDefaultFiles();
app.UseStaticFiles();

// Configure database connection string from configuration (for Docker /data mount)
var config = app.Services.GetRequiredService<IConfiguration>();
var dbPath = config.GetConnectionString("Default") ?? "Data Source=./data/pulso.db;Cache=Shared;Pooling=true;";
Database.SetConnectionStringForTesting(dbPath);

await Database.InitializeAsync(app.Lifetime.ApplicationStopping);

await app.RunAsync();

public sealed record HealthResponse(string Status);