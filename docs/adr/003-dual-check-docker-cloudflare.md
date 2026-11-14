# ADR 003: Monitoreo Doble Vía (Red Docker Interna vs Cloudflare Público)

## Contexto
Cada servicio publicado (Nutrition App, n8n Automation, Santiago Portfolio, API Gateway, Demo Service) es accesible por dos rutas con propiedades de fallo distintas.

## Decisión
Ejecutar **dos chequeos HTTP independientes por servicio** en cada ciclo (60s):
1. **Red Interna (Docker)**: `http://<container>:<port>/health/live` dentro de red `arch-net`.
2. **URL Pública (Cloudflare)**: `https://<subdomain>.bbasolutions.ar/health/live` vía túnel Cloudflare.

Combinar resultados en 4 estados:
| Red Interna | URL Pública | Estado Final | Badge |
|-------------|-------------|--------------|-------|
| OK (2xx) | OK (2xx) | **ONLINE** / **UP** | Verde |
| OK (2xx) | Fallo/Timeout | **FALLO DE TÚNEL** | Ámbar |
| Fallo/Timeout | OK (2xx) | **FALLO DE TÚNEL** | Ámbar |
| Fallo/Timeout | Fallo/Timeout | **DESCONECTADO** | Rojo |

## Por qué NO un solo chequeo
- Un túnel Cloudflare puede caer mientras el contenedor está sano (y viceversa).
- El operador necesita saber **dónde** está el fallo para actuar: reiniciar túnel vs reiniciar contenedor.
- Detecta problemas de DNS, TLS, routing de Cloudflare que un chequeo interno no ve.

## Implementación
- `MonitorWorker` ejecuta ambos cheques en paralelo con `Task.WhenAll`.
- Timeout 3s por chequeo (Polly), retry 1× con jitter 200ms.
- Circuit Breaker por endpoint protege contra cascada de fallos.
- Resultados persistidos en `check_results` con `internalOk` + `publicOk` booleanos.

## Estado
Aceptado — implementado en Fase 1/2.