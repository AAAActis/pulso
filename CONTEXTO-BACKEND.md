# CONTEXTO BACKEND: PULSO API (`status.bbasolutions.ar`)

> **Instrucción para Claude Code:** Este documento contiene toda la especificación técnica, contratos JSON, esquema SQLite y reglas de negocio del backend de **Pulso**. Debe cumplirse en conjunto con `../CONTEXTO-ARCH-Y-PROYECTOS.md` y `./ESTADO.md`.

---

## 1. Arquitectura y Stack (Minimalismo de Nivel Producción)

- **Framework:** .NET 10 (`net10.0` — o `net9.0`/`net8.0` según el SDK instalado en la máquina local de Santiago; **verificar primero con `dotnet --list-sdks` y usar el más reciente instalado**).
- **Estilo:** ASP.NET Core Minimal APIs.
- **Persistencia:** SQLite en `./data/pulso.db` usando `Microsoft.Data.Sqlite` y `Dapper`. Modo `PRAGMA journal_mode=WAL;` activado al iniciar.
- **Caché:** `HybridCache` (.NET) con L1 en memoria (TTL 15s) y L2 en Redis compartido (TTL 60s, prefijo `pulso:`).
  - **Regla de Resiliencia de Redis:** Si la variable `REDIS_CONNECTION_STRING` está vacía o Redis no responde, la app arranca y opera normalmente usando solo L1 (Memoria) sin lanzar excepciones no controladas.
- **Resiliencia HTTP:** `Microsoft.Extensions.Http.Resilience` (Polly v8) con:
  - Timeout por request: `3 segundos`.
  - Retry: 1 reintento rápido (`200ms` con jitter) para errores transitorios.
  - Circuit Breaker: se abre si fallan llamadas consecutivas, protegiendo al servidor.

---

## 2. Estructura de Archivos

```text
Pulso/
├── .claude/agents/
│   └── verificador.md            # Subagente auditor independiente
├── .github/workflows/
│   └── ci-cd.yml                 # Build, Test, Trivy y Push a GHCR
├── docs/adr/
│   ├── 001-sqlite-y-hybridcache.md
│   └── 002-monitoreo-doble-via.md
├── src/Pulso.Api/
│   ├── Pulso.Api.csproj
│   ├── Program.cs                # DI, Middlewares, Seguridad, Endpoints
│   ├── Models.cs                 # Records y DTOs públicos
│   ├── Database.cs               # SQLite + Dapper + Purga > 7 días
│   ├── LabStateService.cs        # Métricas en vivo (p50/p95, Hit Ratio, Simulación)
│   ├── MonitorWorker.cs          # BackgroundService que chequea cada 60s
│   └── wwwroot/
│       └── index.html            # Dashboard SPA en Vanilla HTML/CSS/JS
├── tests/Pulso.Tests/
│   ├── Pulso.Tests.csproj
│   └── ApiIntegrationTests.cs    # Suite de pruebas con WebApplicationFactory
├── Dockerfile
├── docker-compose.yml
├── .env.example
├── ESTADO.md
├── CONTEXTO-BACKEND.md
├── CONTEXTO-FRONTEND.md
└── README.md
```

---

## 3. Esquema SQLite (`Database.cs`)

Al iniciar la aplicación, `Database.InitializeAsync()` debe crear el archivo SQLite (si no existe) y ejecutar:

```sql
PRAGMA journal_mode=WAL;
PRAGMA synchronous=NORMAL;

CREATE TABLE IF NOT EXISTS check_results (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    service_id TEXT NOT NULL,
    checked_at_utc TEXT NOT NULL,          -- ISO 8601 UTC
    internal_ok INTEGER NOT NULL,          -- 1 = ONLINE, 0 = DOWN
    public_ok INTEGER NOT NULL,            -- 1 = UP, 0 = FAIL
    internal_latency_ms INTEGER NOT NULL,
    public_latency_ms INTEGER NOT NULL,
    status_label TEXT NOT NULL             -- 'UP' | 'FALLO DE TÚNEL' | 'DESCONECTADO'
);

CREATE INDEX IF NOT EXISTS idx_checks_service_time 
ON check_results(service_id, checked_at_utc DESC);
```

- **Semilla inicial (Seed):** Si la tabla `check_results` está vacía al arrancar por primera vez, insertar un historial inicial limpio de las últimas 24 horas para los 5 servicios (para que las 24 barritas de la UI se vean completas desde el minuto cero y no vacías), y luego continuar insertando chequeos reales cada 60 segundos.
- **Purga automática:** En cada ciclo del `MonitorWorker`, ejecutar `DELETE FROM check_results WHERE checked_at_utc < datetime('now', '-7 days');` para que el disco de la Arch nunca crezca.

---

## 4. Lógica de los 5 Servicios y Monitoreo "Doble Vía"

**PROHIBICIÓN DE SEGURIDAD:** Jamás serializar en los endpoints públicos las URLs internas (`http://nutricion-api:8080`, etc.). Esas URLs viven solo en la configuración interna del servidor.

| `id` | `name` (Público) | Check Interno (`arch-net`) | Check Público (Cloudflare) |
|---|---|---|---|
| `nutrition-app` | `Nutrition App` | Configurable por env (`CHECK_INTERNAL_NUTRITION`) | `https://nutricion.bbasolutions.ar` |
| `n8n-automation` | `n8n Automation` | Configurable por env (`CHECK_INTERNAL_N8N`) | `https://n8n.bbasolutions.ar` |
| `santiago-portfolio` | `Santiago Portfolio` | Configurable por env (`CHECK_INTERNAL_PORTFOLIO`) | `https://s-portfolio.bbasolutions.ar` |
| `api-gateway` | `API Gateway` | Verifica conectividad interna del gateway | Verifica alcance del túnel público |
| `demo-service` | `Demo Service` | Virtual interno (controlado por `LabStateService`) | Virtual público (controlado por `LabStateService`) |

*Nota para desarrollo local / CI:* Si las variables `CHECK_INTERNAL_*` no están definidas (porque corre en la PC de desarrollo o en CI fuera de `arch-net`), el `MonitorWorker` usa un modo seguro local que verifica las URLs públicas o responde `ONLINE` sin crashear.

### Regla de Determinación de Estado:
1. Si `internal_ok == true` y `public_ok == true`:
   - `internalStatus`: `"ONLINE"` (en `demo-service` puede mostrarse `"UP"` como en el mockup)
   - `publicStatus`: `"UP"`
2. Si `internal_ok == true` y `public_ok == false` (o durante simulación en `api-gateway`):
   - `internalStatus`: `"ONLINE"`
   - `publicStatus`: `"FALLO DE TÚNEL"` (Color Ámbar `#f59e0b`)
3. Si `internal_ok == false` y `public_ok == false` (o durante simulación en `demo-service`):
   - `internalStatus`: `"UP"` o `"DOWN"` (según el caso)
   - `publicStatus`: `"DESCONECTADO"` (Color Rojo `#ef4444`)

---

## 5. Contratos JSON Exactos de los Endpoints (`/api/v1`)

### 5.1. `GET /api/v1/summary`
Consumido por el Header de Pulso y por el portafolio `s-portfolio.bbasolutions.ar`.
Soporta `ETag` (hash débil o fuerte del payload) y devuelve `304 Not Modified` si `If-None-Match` coincide.

```json
{
  "globalStatus": "operational",
  "globalLabel": "Sistemas Operativos",
  "uptime24h": 99.98,
  "uptime7d": 99.95,
  "cacheHitRatio": 94.2,
  "simulationActive": false,
  "timestampUtc": "2026-11-13T23:00:00Z"
}
```
*(Si la simulación de caída está activa, `globalStatus` pasa a `"degraded"` y `globalLabel` pasa a `"Degradación Parcial (Lab Activo)"`).*

### 5.2. `GET /api/v1/services`
Devuelve las 5 filas para la tabla principal y el estado del laboratorio:

```json
{
  "cacheStatus": "HIT",
  "staleWhileRevalidate": true,
  "services": [
    {
      "id": "nutrition-app",
      "name": "Nutrition App",
      "internalStatus": "ONLINE",
      "publicStatus": "UP",
      "uptime24h": 100.0,
      "bars24h": ["ok","ok","ok","ok","ok","ok","ok","ok","ok","ok","ok","ok","ok","ok","ok","ok","ok","ok","ok","ok","ok","ok","ok","ok"]
    }
  ],
  "lab": {
    "latencySeries": [
      { "p50Ms": 12, "p95Ms": 45 }
    ],
    "currentP50Ms": 12,
    "currentP95Ms": 45,
    "cacheAvailabilityPercent": 100,
    "hitRatioPercent": 94.2,
    "staleWhileRevalidateActive": true,
    "circuitBreakerState": "Closed",
    "simulationActive": false,
    "simulationRemainingSeconds": 0
  }
}
```

### 5.3. `POST /api/v1/lab/simulate-failure`
- **Rate Limit:** Máximo 5 ejecuciones por minuto por IP (`429 Too Many Requests` en formato `ProblemDetails` si se excede).
- **Efecto (dura 45 segundos):**
  1. `demo-service` pasa inmediatamente a `publicStatus: "DESCONECTADO"` y su última barra (24h) pasa a `"down"`.
  2. `api-gateway` pasa a `publicStatus: "FALLO DE TÚNEL"` y su última barra pasa a `"warn"`.
  3. El `circuitBreakerState` pasa a `"Open"` y `staleWhileRevalidateActive` sirve la respuesta desde la caché híbrida marcando `cacheStatus: "STALE"`.
  4. Agrega un pico de latencia simulado en la serie para que el reclutador vea el impacto visual en la curva SVG, seguido de lecturas rápidas de caché (`1ms - 4ms`).
  5. Dispara un evento inmediato a todos los clientes conectados a `/api/v1/stream` (SSE).

### 5.4. `GET /api/v1/stream` (Server-Sent Events)
- Cabecera `Content-Type: text/event-stream`, `Cache-Control: no-cache`.
- Envía cada 3 segundos un evento `event: pulse\ndata: <JSON combinado de summary + services + lab>\n\n`.

### 5.5. `GET /health/live` y `GET /health/ready`
- `/health/live`: `200 OK` `{"status": "Healthy"}`.
- `/health/ready`: Verifica que SQLite responda `SELECT 1;` y devuelve `200 OK`.