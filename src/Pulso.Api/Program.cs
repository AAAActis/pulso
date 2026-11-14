using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using System.Net.Http.Headers;
using Pulso.Api;
using Polly;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromSeconds(60),
        LocalCacheExpiration = TimeSpan.FromSeconds(15)
    };
});

builder.Services.AddStackExchangeRedisCache(options =>
{
    var redisConn = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
    options.Configuration = redisConn;
});

builder.Services.AddHttpClient("monitor", client =>
{
    client.Timeout = TimeSpan.FromSeconds(3);
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
})
.AddStandardResilienceHandler(options =>
{
    options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(3);
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

var app = builder.Build();

await Database.InitializeAsync(app.Lifetime.ApplicationStopping);

app.MapGet("/health/live", () => new HealthResponse("Healthy"));
app.MapGet("/health/ready", () => new HealthResponse("Healthy"));

await app.RunAsync();

public sealed record HealthResponse(string Status);