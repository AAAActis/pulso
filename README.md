# Pulso API — Status Dashboard & Resilience Lab

> **Live endpoint:** `https://status.bbasolutions.ar` (served via Cloudflare Tunnel)  
> **API base:** `https://status.bbasolutions.ar/api/v1`  
> **Container image:** `ghcr.io/aaaactis/pulso:latest`

A production-grade .NET 10 monitoring API with a **Dark Telemetry Glass** frontend. Monitors 5 services via **dual-path checks** (internal Docker network + public Cloudflare tunnels), exposes real-time metrics over **Server-Sent Events**, and includes a **chaos lab** to simulate failures and observe circuit breaker / stale-cache behavior.

---

## Architecture Overview

```mermaid
flowchart TB
    subgraph Client["🌐 Client Browser"]
        UI["Dark Telemetry Glass Dashboard<br/>(Vanilla HTML/CSS/JS, zero deps)"]
    end

    subgraph Edge["☁️ Cloudflare Edge"]
        CF[("Cloudflare Tunnel<br/>s-portfolio.bbasolutions.ar")]
        WAF["WAF + DDoS Protection"]
    end

    subgraph Host["🖥️ Arch Linux Host (Same machine monitored)"]
        subgraph Docker["🐳 Docker Network: arch-net"]
            API["Pulso API Container<br/>:8080 (non-root 1654)"]
            REDIS[("Redis (optional L2 cache)")]
            DATA[("SQLite WAL\n/data/pulso.db")]
        end

        subgraph Monitored["📦 Monitored Services (on same host)")
            N8N["n8n Automation\n:5678"]
            NUT["Nutrition App\n:8081"]
            PORT["Santiago Portfolio\n:3000"]
            GW["API Gateway\n:8080"]
            DEMO["Demo Service\n:8082"]
        end
    end

    UI -->|SSE /api/v1/stream| CF
    UI -->|REST /api/v1/*| CF
    CF --> WAF --> API
    API -->|HybridCache L2| REDIS
    API -->|SQLite| DATA
    API -->|Dual HTTP Checks| N8N
    API -->|Dual HTTP Checks| NUT
    API -->|Dual HTTP Checks| PORT
    API -->|Dual HTTP Checks| GW
    API -->|Dual HTTP Checks| DEMO
    API -.->|Internal Docker\narch-net| N8N
    API -.->|Internal Docker\narch-net| NUT
    API -.->|Internal Docker\narch-net| PORT
    API -.->|Internal Docker\narch-net| GW
    API -.->|Internal Docker\narch-net| DEMO

    classDef api fill:#131b2e,stroke:#38bdf8,color:#f1f5f9;
    classDef ext fill:#0e1320,stroke:#f59e0b,color:#f1f5f9;
    classDef db fill:#182238,stroke:#10b981,color:#f1f5f9;
    classDef mon fill:#0b0f19,stroke:#64748b,color:#94a3b8;
    class API,REDIS,DATA api;
    class CF,WAF ext;
    class N8N,NUT,PORT,GW,DEMO mon;
```

---

## Quick Start (3 Commands)

```bash
# 1. Clone & configure
git clone https://github.com/AAAActis/Pulso.git && cd Pulso
cp .env.example .env   # edit REDIS_CONNECTION if using Redis

# 2. Build & run locally (development)
docker compose up --build -d
# Dashboard at http://localhost:5000

# 3. Deploy to production (Arch Linux)
docker compose -f docker-compose.prod.yml --env-file .env up -d
# Dashboard at https://status.bbasolutions.ar
```

**Requirements:** Docker 24+, Docker Compose v2, Arch Linux (or any Linux with `arch-net` bridge network).

---

## What to Test in 60 Seconds

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Open `https://status.bbasolutions.ar` | Dark dashboard loads, SSE connects (pulsing green dot) |
| 2 | Check **Services** table | 5 rows, all `UP`/`ONLINE`, 24 uptime bars each |
| 3 | Check **Lab Panel** | Latency chart animating (p50 cyan, p95 violet), gauge 100%, hit ratio ~94% |
| 4 | Click **SIMULAR CAÍDA** | Button → amber countdown 45s, Demo Service → `DOWN`/`DESCONECTADO` (red), API Gateway → `FALLO DE TÚNEL` (amber), global status → `Degradación Parcial`, stale badge → `Circuit Breaker: OPEN` |
| 5 | Wait 45s | Auto-restores: all green, badge → `Stale-While-Revalidate inactivo` |
| 6 | Open **Console** modal (sidebar) | Live SSE logs scrolling |
| 7 | Open **Architecture** modal | Resilience, Monitoring, Security, Endpoints docs |
| 8 | Resize browser < 768px | Sidebar horizontal, tables collapse, grids single-column |

---

## API Reference (v1)

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/v1/summary` | Global KPIs (uptime 24h/7d, hit ratio). Supports `ETag` / `304 Not Modified`. |
| `GET` | `/api/v1/services` | 5 services with internal/public status, 24h bars, lab metrics. |
| `GET` | `/api/v1/stream` | **SSE** — emits `event: pulse` every 3s + immediate push on simulation. |
| `POST` | `/api/v1/lab/simulate-failure` | Activates 45s failure simulation (rate limited: 5 req/min). |
| `GET` | `/health/live` | Liveness probe (always 200 if process alive). |
| `GET` | `/health/ready` | Readiness probe (checks DB + optional Redis). |

**Example:**
```bash
curl -s https://status.bbasolutions.ar/api/v1/summary | jq .
# {"uptime24h":99.98,"uptime7d":99.95,"cacheHitRatio":94.2,"simulationActive":false}
```

---

## Resilience Architecture

| Layer | Implementation |
|-------|----------------|
| **HTTP Client** | `Microsoft.Extensions.Http.Resilience` (Polly v8): Timeout 3s, Retry 1× jitter 200ms, Circuit Breaker |
| **Cache** | **HybridCache**: L1 `MemoryCache` (15s TTL) + L2 `Redis` (60s TTL, prefix `pulso:`). Graceful degradation to L1-only if Redis unavailable. |
| **Circuit Breaker** | Tracks consecutive failures per endpoint; opens after threshold, returns stale cache (stale-while-revalidate) |
| **Rate Limiting** | IP-based (reads `CF-Connecting-IP`): 60 req/min global, 5 req/min on `/lab/simulate-failure` |
| **CORS** | Strict: only `https://s-portfolio.bbasolutions.ar` (localhost in Development) |
| **Security Headers** | CSP, `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy` |
| **Error Format** | RFC 9457 `ProblemDetails` |

---

## Threat Model (OWASP API Top 10)

| # | Threat | Mitigation in Pulso |
|---|--------|---------------------|
| **API1** | Broken Object Level Auth | N/A — no user objects, read-only public dashboard |
| **API2** | Broken Authentication | N/A — no auth on public endpoints (monitoring data is public) |
| **API3** | Broken Object Property Level Auth | N/A — no PII, fixed schema |
| **API4** | Unrestricted Resource Consumption | **Rate limiting** (5/min lab, 60/min global), `mem_limit: 192m`, `cpus: 0.5`, request timeout 3s |
| **API5** | Broken Function Level Auth | N/A — only safe methods (GET) + one POST rate-limited |
| **API6** | Unrestricted Access to Sensitive Business Flows | **Simulation endpoint** rate-limited, idempotent, auto-expires 45s |
| **API7** | Server-Side Request Forgery | **No outbound calls to user-supplied URLs** — monitors fixed internal/public endpoints only |
| **API8** | Security Misconfiguration | **Hardened container**: non-root (1654), `read_only`, `cap_drop: ALL`, `no-new-privileges`, `tmpfs /tmp noexec` |
| **API9** | Improper Inventory Management | **OpenAPI not exposed** — minimal surface, versioned `/api/v1/` |
| **API10** | Unsafe Consumption of APIs | **Polly resilience** on all outbound HTTP; input validation on simulation endpoint |

**Honest limitation:** Pulso runs on the **same machine it monitors**. A host-level compromise (kernel, Docker daemon, host network) bypasses all container isolation. This is acceptable for a personal portfolio project; production multi-tenant monitoring would require separate monitoring infrastructure.

---

## Project Structure

```
Pulso/
├── src/Pulso.Api/           # .NET 10 Minimal API
│   ├── Program.cs           # Endpoints, middleware, DI, Polly, HybridCache
│   ├── Models.cs            # Immutable records (DTOs, domain)
│   ├── Database.cs          # SQLite WAL init, Dapper queries
│   ├── LabStateService.cs   # Ring-buffer p50/p95, cache counters, simulation state
│   ├── MonitorWorker.cs     # BackgroundService dual-check every 60s
│   └── wwwroot/index.html   # Dark Telemetry Glass SPA (1000+ lines)
├── tests/Pulso.Tests/       # 25 tests (unit + integration)
│   ├── LabStateServiceTests.cs
│   ├── MonitorLogicTests.cs
│   └── IntegrationTests.cs  # WebApplicationFactory: CORS, ETag, RateLimit, No-Leak
├── docs/adr/                # Architecture Decision Records
│   ├── 001-sqlite-hybridcache.md
│   ├── 002-no-external-broker.md
│   └── 003-dual-check-docker-cloudflare.md
├── Dockerfile               # Multi-stage, Alpine, non-root 1654
├── docker-compose.prod.yml  # Production: mem_limit 192m, cpus 0.5, read_only, cap_drop ALL
├── .env.example             # Environment template
├── .github/workflows/ci-cd.yml  # Build, Test, CodeQL, Trivy, GHCR push
└── ESTADO.md                # Phase tracker (this project's memory)
```

---

## Key Design Decisions (ADRs)

1. **[SQLite + HybridCache](docs/adr/001-sqlite-hybridcache.md)** — Zero external deps, sub-ms L1, graceful L2 degradation.
2. **[No Message Broker](docs/adr/002-no-external-broker.md)** — In-process `BackgroundService` + thread-safe Singleton sufficient for 0.17 writes/s.
3. **[Dual-Path Monitoring](docs/adr/003-dual-check-docker-cloudflare.md)** — Detects tunnel vs container failures independently.

---

## Tech Stack

| Layer | Choice |
|-------|--------|
| Runtime | .NET 10 (ASP.NET Core Minimal APIs) |
| Database | SQLite 3 (WAL mode) + Dapper |
| Cache | `Microsoft.Extensions.Caching.Hybrid` (Memory + Redis) |
| Resilience | `Microsoft.Extensions.Http.Resilience` (Polly v8) |
| Frontend | Vanilla HTML/CSS/JS (ES2022), zero dependencies |
| Real-time | Server-Sent Events (native `EventSource`) |
| Container | Multi-stage Docker, Alpine, non-root UID 1654 |
| CI/CD | GitHub Actions + CodeQL + Trivy → GHCR |
| Host | Arch Linux, `arch-net` Docker network, Cloudflare Tunnel |

---

## License

MIT — Free for portfolio demonstration and learning.

---

**Built by [Santiago](https://s-portfolio.bbasolutions.ar) · [GitHub](https://github.com/AAAActis)**