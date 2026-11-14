using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/health/live", () => new HealthResponse("Healthy"));
app.MapGet("/health/ready", () => new HealthResponse("Healthy"));

await app.RunAsync();

public sealed record HealthResponse(string Status);