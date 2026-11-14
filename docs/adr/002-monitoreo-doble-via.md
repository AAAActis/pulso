# ADR 002: Monitoreo "Doble Vía" (Red Interna Docker vs Cloudflare Público)

## Contexto
Pulso API debe reportar el estado de 5 servicios desde dos perspectivas:
1. **Red Interna (Docker `arch-net`):** Conectividad entre contenedores sin salir a Internet.
2. **Público (Cloudflare Tunnel):** Alcance real desde Internet a través de `*.bbasolutions.ar`.

El estado combinado determina la etiqueta visible:
- `internal=OK, public=OK` → `UP` (Verde)
- `internal=OK, public=FAIL` → `FALLO DE TÚNEL` (Ámbar)
- `internal=FAIL, public=FAIL` → `DESCONECTADO` (Rojo)

## Decisión
- **Configuración por variables de entorno:** Cada servicio define su check interno via `CHECK_INTERNAL_<SERVICE>` (ej. `CHECK_INTERNAL_NUTRITION=http://nutricion-api:8080/health`). El check público es URL fija HTTPS.
- **Modo seguro local/CI:** Si `CHECK_INTERNAL_*` no está definido, el `MonitorWorker` usa modo degradado: verifica solo la URL pública o responde `ONLINE` sin crashear.
- **Seguridad (PROHIBICIÓN):** Jamás serializar URLs internas (`http://nutricion-api:8080`, puertos, nombres de contenedores) en endpoints públicos `/api/v1/*`. Solo viven en configuración del servidor.
- **Implementación:** `MonitorWorker` (`BackgroundService` + `PeriodicTimer` 60s) ejecuta ambos chequeos en paralelo con `HttpClient` configurado con Polly (Timeout 3s, Retry 1×200ms+jitter, Circuit Breaker).
- **Resultado:** Se persiste en `check_results` con `status_label` calculado según la regla de determinación.

## Consecuencias
- **Positivas:**
  - Detección precisa de fallos de túnel Cloudflare vs fallos reales del servicio.
  - Configuración externa (12-factor) sin recompilar para cambiar endpoints.
  - Modo seguro permite desarrollo local y CI sin red `arch-net`.
- **Negativas:**
  - Doble latencia por chequeo (interno + público) → ~2× tiempo. Mitigado con `Task.WhenAll` en paralelo.
  - Complejidad en lógica de estado (3 combinaciones). Documentada en CONTEXTO-BACKEND.md §4.
- **Riesgos mitigados:**
  - Circuit Breaker protege al servidor si un endpoint interno falla persistentemente.
  - `HybridCache` sirve datos *stale* durante simulación de caída (Circuit Breaker `Open`).

## Alternativas consideradas
| Opción | Por qué no |
|--------|------------|
| Solo check público | No distingue "servicio caído" de "túnel caído"; falsos positivos. |
| Solo check interno | No valida alcance real desde Internet (Cloudflare, DNS, TLS). |
| Agente sidecar por servicio | Overhead operativo; Pulso es single-binary. |
| Sintético externo (UptimeRobot, etc.) | Costo, latencia de alerta, no integrado en dashboard propio. |