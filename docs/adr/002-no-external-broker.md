# ADR 002: Sin Broker Externo (RabbitMQ, Kafka, etc.)

## Contexto
El monitoreo doble vía (Docker interno + Cloudflare público) requiere ejecutar chequeos HTTP cada 60s y emitir eventos SSE cada 3s. Se evaluó usar un message broker para desacoplar el MonitorWorker del SSE.

## Decisión
**No usar broker externo**. El `MonitorWorker` (BackgroundService con `PeriodicTimer`) escribe directo a SQLite y actualiza `LabStateService` (Singleton thread-safe). El endpoint SSE lee de `LabStateService` y emite eventos.

## Alternativas consideradas
| Opción | Por qué NO |
|--------|------------|
| RabbitMQ | Dependencia extra, complejidad operacional, overkill para 1 mensaje/3s |
| Kafka | Mismo que arriba + retención de logs innecesaria |
| Redis Pub/Sub | Requiere Redis siempre disponible (ya lo usamos para L2, pero añade acoplamiento) |
| **In-process (BackgroundService + Singleton)** ✅ | Cero dependencias, latencia mínima, testable, suficiente para throughput actual |

## Justificación técnica
- Throughput: 5 servicios × 2 rutas × 1 check/60s = **0.17 req/s** de escritura.
- SSE: 1 evento/3s por cliente conectado (máx. ~10 concurrentes en dashboard).
- `LabStateService` usa `ConcurrentDictionary` + `ReaderWriterLockSlim` para ring-buffer, contadores y estado de simulación — todo thread-safe, sin locks globales.
- Si se escala a >2 instancias: cada una monitorea su subset y el dashboard agrega vía `/api/v1/services` (ya diseñado para multi-instancia).

## Estado
Aceptado — implementado en Fase 1.