namespace Pulso.Api;

/// <summary>
/// Representa un servicio monitoreado con su estado y métricas.
/// </summary>
public sealed record ServiceDto(
    string Id,
    string Name,
    string InternalStatus,
    string PublicStatus,
    double Uptime24h,
    IReadOnlyList<string> Bars24h
);

/// <summary>
/// Estado del laboratorio (métricas en vivo, caché, simulación).
/// </summary>
public sealed record LabDto(
    IReadOnlyList<LatencyPoint> LatencySeries,
    int CurrentP50Ms,
    int CurrentP95Ms,
    double CacheAvailabilityPercent,
    double HitRatioPercent,
    bool StaleWhileRevalidateActive,
    string CircuitBreakerState,
    bool SimulationActive,
    int SimulationRemainingSeconds
);

/// <summary>
/// Punto de latencia (p50/p95) para el gráfico.
/// </summary>
public sealed record LatencyPoint(
    int P50Ms,
    int P95Ms
);

/// <summary>
/// Respuesta del endpoint /api/v1/summary.
/// </summary>
public sealed record SummaryDto(
    string GlobalStatus,
    string GlobalLabel,
    double Uptime24h,
    double Uptime7d,
    double CacheHitRatio,
    bool SimulationActive,
    string TimestampUtc
);

/// <summary>
/// Respuesta completa del endpoint /api/v1/services.
/// </summary>
public sealed record ServicesResponseDto(
    string CacheStatus,
    bool StaleWhileRevalidate,
    IReadOnlyList<ServiceDto> Services,
    LabDto Lab
);

/// <summary>
/// Request para simular una caída (POST /api/v1/lab/simulate-failure).
/// </summary>
public sealed record SimulateFailureRequest(
    // Reserved for future options
);

/// <summary>
/// Respuesta de simulación de caída.
/// </summary>
public sealed record SimulateFailureResponse(
    bool Success,
    string Message,
    int DurationSeconds
);

/// <summary>
/// Health check response.
/// </summary>
public sealed record HealthResponse(
    string Status
);

/// <summary>
/// Configuración de un servicio monitoreado.
/// </summary>
public sealed record ServiceConfig(
    string Id,
    string Name,
    string? InternalUrlEnvVar,
    string PublicUrl,
    bool IsVirtual
);

/// <summary>
/// Resultado de un chequeo doble vía (interno + público).
/// </summary>
public sealed record CheckResult(
    string ServiceId,
    DateTime CheckedAtUtc,
    bool InternalOk,
    bool PublicOk,
    int InternalLatencyMs,
    int PublicLatencyMs,
    string StatusLabel
);

/// <summary>
/// Evento SSE enviado cada 3 segundos.
/// </summary>
public sealed record PulseEvent(
    SummaryDto Summary,
    ServicesResponseDto Services,
    LabDto Lab
);