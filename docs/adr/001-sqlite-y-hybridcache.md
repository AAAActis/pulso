# ADR 001: SQLite + HybridCache para Persistencia y Caché

## Contexto
Pulso API necesita almacenar el historial de chequeos de salud (check_results) y servir datos con latencia mínima. Los requisitos son:
- Persistencia ligera sin servidor de BD separado (arquitectura single-binary en Arch Linux).
- Caché de dos niveles: L1 en memoria (TTL 15s) y L2 en Redis compartido (TTL 60s).
- Resiliencia: si Redis no está disponible, la app debe arrancar y operar solo con L1.

## Decisión
- **Base de datos:** SQLite en modo WAL (`PRAGMA journal_mode=WAL;`) con Dapper como micro-ORM.
- **Caché:** `Microsoft.Extensions.Caching.Hybrid` (HybridCache) con:
  - L1: `MemoryCache` (TTL 15s).
  - L2: `StackExchangeRedis` (TTL 60s, prefijo `pulso:`).
  - Fallback automático a solo L1 si `REDIS_CONNECTION_STRING` está vacío o Redis no responde.

## Consecuencias
- **Positivas:**
  - Zero-config en desarrollo local y CI (no requiere Redis).
  - Archivo único `./data/pulso.db` fácil de backup/migrar.
  - WAL permite lecturas concurrentes sin bloquear escrituras.
  - HybridCache abstrae la lógica de fallback y expiración.
- **Negativas:**
  - SQLite no escala horizontalmente (single-writer). Aceptable para carga de monitoreo (5 servicios × 1 check/min).
  - Redis opcional añade complejidad operativa en producción; se documenta en README.
- **Riesgos mitigados:**
  - Purga automática cada ciclo del `MonitorWorker` (`DELETE WHERE checked_at_utc < -7 days`) evita crecimiento ilimitado del disco.
  - `PRAGMA synchronous=NORMAL` balancea durabilidad y rendimiento en SSD.

## Alternativas consideradas
| Opción | Por qué no |
|--------|------------|
| PostgreSQL | Overhead operativo (contenedor, usuario, red) para 5 servicios. |
| LiteDB / Embedded NoSQL | Menos maduro, sin WAL nativo, Dapper no soporta nativamente. |
| Solo memoria (sin persistencia) | Pérdida de historial al reiniciar; rompe uptime 24h/7d. |
| Redis como única caché | Punto único de fallo; sin Redis la app crashea. |