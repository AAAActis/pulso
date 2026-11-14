# ESTADO DEL PROYECTO: PULSO API (`status.bbasolutions.ar`)

> **Instrucción para el Agente Principal (Claude Code):**
> Este archivo es tu **memoria persistente** entre sesiones.
> 1. Al iniciar cualquier sesión, leé `../CONTEXTO-ARCH-Y-PROYECTOS.md`, `./CONTEXTO-BACKEND.md`, `./CONTEXTO-FRONTEND.md` y este archivo `ESTADO.md`.
> 2. Buscá la primera tarea pendiente (`[ ]`) en orden secuencial.
> 3. **REGLA DE ORO:** Tenés **PROHIBIDO** marcar una Fase como completada (`[x]`) por tu cuenta sin antes invocar al subagente **`verificador`** y obtener su veredicto **APTO**.
> 4. Al terminar cada fase aprobada por el `verificador`, actualizá la tabla de "Registro de Sesiones" al final de este documento y hacé un commit.

---

## Resumen de Progreso Actual
- **Fase Actual:** Fase 3 (Frontend "Dark Telemetry Glass") — **EN PROGRESO**
- **Último Commit Verificado:** 48b72ac (Fase 2)
- **Estado del Verificador:** APTO (Fase 2) — Pendiente Fase 3
- **Bloqueos / Dudas para Santiago:** Ninguno

---

## Plan de Tareas Completo (Checklist)

### FASE 0: Esqueleto, Solución .NET 10 y Base de Datos SQLite
- [x] **0.1.** Crear estructura de carpetas (`src/Pulso.Api`, `tests/Pulso.Tests`, `docs/adr`) y archivo `.gitignore` para .NET + archivos `.env` y carpeta `data/`.
- [x] **0.2.** Crear `src/Pulso.Api/Pulso.Api.csproj` (.NET 10) con paquetes mínimos: `Microsoft.Data.Sqlite`, `Dapper`, `Microsoft.Extensions.Caching.Hybrid`, `Microsoft.Extensions.Caching.StackExchangeRedis`, `Microsoft.Extensions.Http.Resilience`.
- [x] **0.3.** Crear `src/Pulso.Api/Models.cs` con los records inmutables de dominio y respuestas JSON (según `CONTEXTO-BACKEND.md`).
- [x] **0.4.** Crear `src/Pulso.Api/Database.cs` con inicialización de SQLite en `WAL` mode, tablas `check_results` e `incidents`, inserción de chequeos y purga automática (> 7 días).
- [x] **0.5.** Crear `tests/Pulso.Tests/Pulso.Tests.csproj` (xUnit + `Microsoft.AspNetCore.Mvc.Testing`) y 6 tests reales que ejercitan el código de producción: creación de esquema, inserción, uptime 24h/7d, purga 7 días, barras 24h, semilla inicial (120 registros).
- [x] **0.6. [GATE]** Invocado al subagente **`verificador`** sobre la Fase 0. Veredicto: **APTO** — compilación OK, 6/6 tests en verde, mutación detectada (uptime 80% → 100% falla), sin filtraciones internas, ADRs creados.

### FASE 1: Motor de Monitoreo "Doble Vía", Resiliencia (Polly) y Laboratorio
- [x] **1.1.** Crear `src/Pulso.Api/LabStateService.cs` (Singleton thread-safe) que gestione:
  - Ring-buffer de las últimas 30 mediciones de latencia de la API (cálculo real de `p50` y `p95`).
  - Contadores de `CacheHits`, `CacheMisses`, `HitRatioPercent` y flag `StaleWhileRevalidateActive`.
  - Estado de simulación de caída (`SimulateFailureUntil`, estado del Circuit Breaker) que dura 45 segundos y se restaura solo.
- [x] **1.2.** Crear `src/Pulso.Api/MonitorWorker.cs` (`BackgroundService` con `PeriodicTimer` de 60s y semilla inicial al arrancar) que ejecute el chequeo **Doble Vía** (Red Interna Docker vs URL Pública Cloudflare) para los 5 servicios (`nutrition-app`, `n8n-automation`, `santiago-portfolio`, `api-gateway`, `demo-service`).
- [x] **1.3.** Configurar Polly v8 (`Microsoft.Extensions.Http.Resilience`) con Timeout (3s), Retry con Jitter (200ms) y Circuit Breaker en el cliente HTTP `monitor`, más fallback de `HybridCache` (L1 15s + L2 Redis 60s).
- [x] **1.4.** Escribir tests en `Pulso.Tests`: 7 tests `LabStateServiceTests` (ring-buffer percentiles, cache hit ratio, simulación caída 45s, circuit breaker, stale-while-revalidate) + 5 tests `MonitorLogicTests` (4 combinaciones estado: UP/FALLO DE TÚNEL/DESCONECTADO, ServiceIds, CheckResult).
- [x] **1.5. [GATE]** Invocado al subagente **`verificador`** sobre la Fase 1. Veredicto: **APTO** — compilación OK, 20/20 tests en verde, mutación detectada (p95 0.95→0.50 falla, HitRatio precisión 1→2 decimales falla), sin filtraciones internas, código Fase 1 completo.

### FASE 2: Endpoints Minimal API, Seguridad (CORS, Rate Limit, Headers) y SSE
- [x] **2.1.** Implementar en `src/Pulso.Api/Program.cs`:
  - Middleware de medición de latencia real alimentando a `LabStateService`.
  - Cabeceras de seguridad obligatorias (`CSP`, `X-Content-Type-Options: nosniff`, `Referrer-Policy`, `Permissions-Policy`) y manejo de errores `ProblemDetails` (RFC 9457).
  - CORS estricto: permitir únicamente `https://s-portfolio.bbasolutions.ar` (y `localhost` solo en Development).
  - Rate Limiting por IP real leyendo `CF-Connecting-IP` (máx. 5 req/min en `/api/v1/lab/simulate-failure` y 60 req/min global).
- [x] **2.2.** Exponer los endpoints `/api/v1/summary` (con soporte de `ETag` y respuesta `304 Not Modified`), `/api/v1/services`, `/api/v1/stream` (SSE cada 3 segundos y push inmediato al simular caída), `/api/v1/lab/simulate-failure`, `/health/live` y `/health/ready`.
- [x] **2.3.** Escribir tests de integración con `WebApplicationFactory` en `Pulso.Tests`:
  - Test de CORS (origen permitido vs origen prohibido `https://evil.com`).
  - Test de `ETag` / `If-None-Match` devolviendo `304`.
  - Test de Rate Limiting devolviendo `429 Too Many Requests` en `/lab/simulate-failure`.
  - Test de No-Exposición (verificar que el JSON de `/services` jamás contenga nombres de contenedores como `nutricion-api` ni puertos como `:8080` o `:5678`).
- [x] **2.4. [GATE]** Invocado al subagente **`verificador`** sobre la Fase 2. Veredicto: **APTO** — compilación OK, 25/25 tests en verde, mutación detectada, CORS/ETag/RateLimit/No-Exposición validados.

### FASE 3: Frontend "Dark Telemetry Glass" (`wwwroot/index.html`) y Verificación Visual
- [ ] **3.1.** Construir `src/Pulso.Api/wwwroot/index.html` usando los skills `impeccable`, `Superdesign`, `frontend-design`, `emil-design-eng` y `animate` siguiendo al pie de la letra `CONTEXTO-FRONTEND.md` y el mockup de referencia:
  - Sidebar izquierda fija con íconos SVG y modales informativos de Consola SSE y Arquitectura.
  - Header superior con `PULSO API / Status Dashboard`, píldora global `Sistemas Operativos` y las 3 tarjetas KPI (`Uptime 24h`, `Uptime 7d`, `Caché Hit Ratio`) con `tabular-nums`.
  - Tabla de 5 servicios con píldoras (`ONLINE`, `UP`, `FALLO DE TÚNEL`, `DESCONECTADO`) y las 24 barritas verticales de uptime (`0h`, `12h`, `24h`).
  - Panel de Laboratorio dividido en 2: Gráfico SVG Bezier en vivo (`p50` cian y `p95` violeta) + Gauge semicircular `100%`, barra `Hit Ratio`, badge `Stale-While-Revalidate activo` y el botón ámbar **`SIMULAR CAÍDA (Servicio de Prueba)`**.
  - Panel derecho `Endpoints de la API (v1)` interactivo + candado verde `CORS: s-portfolio.bbasolutions.ar`.
- [ ] **3.2.** Levantar la aplicación localmente y verificarla con los MCPs `playwright` y `chrome-devtools` + skill `break-ui` (comprobar que hay 0 errores en consola, que el SSE actualiza la gráfica y que el clic en `SIMULAR CAÍDA` cambia los badges a naranja/rojo en vivo).
- [ ] **3.3. [GATE]** Invocar al subagente **`verificador`** sobre la Fase 3. Solo avanzar si devuelve **APTO**.

### FASE 4: Docker Endurecido, CI/CD, Documentación de Portafolio e Integración
- [ ] **4.1.** Crear `Dockerfile` multi-etapa (SDK 10 -> Runtime Alpine non-root `1654`), `docker-compose.yml` endurecido (`read_only: true`, `cap_drop: ALL`, `mem_limit: 192m`, puerto `127.0.0.1:5002:8080`, red `arch-net`) y `.env.example`.
- [ ] **4.2.** Crear `.github/workflows/ci-cd.yml` (Build, Test, Trivy Security Scan y publicación en `ghcr.io/aaaactis/pulso:latest`).
- [ ] **4.3.** Redactar los 2 ADRs en `docs/adr/` y el `README.md` de nivel portafolio (diagrama Mermaid, qué probar en 60 segundos, decisiones técnicas y tabla de modelo de amenazas OWASP API Top 10).
- [ ] **4.4. [GATE FINAL]** Invocar al subagente **`verificador`** sobre el proyecto completo.
- [ ] **4.5.** *(Solo con confirmación explícita de Santiago)* Desplegar en la Arch, verificar presupuesto de RAM (`docker stats`) y actualizar `~/portfolio/index.html` para que consuma `/api/v1/summary` en vivo y muestre la tarjeta de Pulso.

---

## Registro de Sesiones (Bitácora del Agente)

| Fecha | Fase Completada | Commit Hash | Veredicto del Verificador | Notas / Qué falta hacer en la próxima sesión |
|---|---|---|---|---|
| 13/11/2026 | Inicialización | - | - | Creados archivos de contexto y `ESTADO.md`. Listo para arrancar Fase 0. |
| 13/11/2026 | Fase 0 | 773ac30 | APTO | Fase 0 completa: solución .NET 10, SQLite WAL, HybridCache, 6 tests de producción, ADRs. Próximo: Fase 1 (MonitorWorker, LabStateService, Polly). |
| 14/11/2026 | Fase 1 | 538b965 | APTO | Fase 1 completa: Motor Doble Vía (MonitorWorker + LabStateService), Polly resilience, HybridCache, 14 tests Fase 1. Próximo: Fase 2 (Endpoints Minimal API, CORS, Rate Limit, SSE). |
| 14/11/2026 | Fase 2 | 48b72ac | APTO | Fase 2 completa: Endpoints Minimal API (/summary, /services, /stream SSE, /lab/simulate-failure, /health), Seguridad (CORS estricto, Rate Limiting por CF-Connecting-IP, Headers, ProblemDetails), 5 tests integración + 20 tests unitarios = 25/25 verde. Próximo: Fase 3 (Frontend Dark Telemetry Glass). |