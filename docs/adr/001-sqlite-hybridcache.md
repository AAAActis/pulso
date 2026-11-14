# ADR 001: SQLite + HybridCache para Persistencia y Caché

## Contexto
Pulso API necesita almacenar resultados de chequeos de salud (check_results) e incidentes, y servir datos con latencia mínima para el dashboard en tiempo real.

## Decisión
- **SQLite en modo WAL** como base de datos principal (archivo único, sin servidor separado).
- **HybridCache (L1 + L2)**: MemoryCache en-proceso (TTL 15s) + Redis compartido (TTL 60s, prefijo `pulso:`).
- Si Redis no está disponible, la app degrada a solo L1 sin errores.

## Alternativas consideradas
| Opción | Ventajas | Desventajas |
|--------|----------|-------------|
| PostgreSQL + Redis | Escalabilidad, concurrencia alta | Overhead operacional, 2 servicios extra |
| SQLite solo | Simplicidad total | Sin caché distribuida, latencia mayor en picos |
| **SQLite + HybridCache** ✅ | Cero dependencias externas obligatorias, latencia sub-ms en L1, resiliencia si Redis cae | L1 por instancia (no compartida entre réplicas) |

## Consecuencias
- **Positivas**: Despliegue en un solo contenedor, coste cero en infra, ideal para 1-2 instancias en la misma máquina monitoreada.
- **Negativas**: L1 no sincroniza entre réplicas (aceptable: cada instancia monitorea sus propios endpoints). No apto para clústeres >2 nodos sin Redis.

## Estado
Aceptado — implementado en Fase 0/1.