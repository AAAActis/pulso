# CONTEXTO FRONTEND Y UI: PULSO API (`status.bbasolutions.ar`)

> **Instrucción para Claude Code:** Usá tus skills **`impeccable`**, **`Superdesign`**, **`frontend-design`**, **`emil-design-eng`** y **`animate`** para construir una interfaz idéntica en estructura y calidad estética al mockup de referencia de Santiago. Prohibido hacer una página genérica o sosa. Todo va en `src/Pulso.Api/wwwroot/index.html` (HTML5 + CSS3 moderno + JS Vanilla, cero dependencias externas).

---

## 1. Dirección Estética y Paleta de Colores ("Dark Telemetry Glass")

La interfaz tiene la estética de una consola de observabilidad moderna de alta ingeniería (estilo Linear / Cloudflare / Datadog Dark).

### Variables CSS exactas (`:root`):
```css
:root {
  /* Fondos */
  --bg-canvas: #0b0f19;          /* Fondo profundo principal (coincide con el portafolio) */
  --bg-sidebar: #0e1320;         /* Barra lateral izquierda */
  --bg-card: #131b2e;            /* Fondo de los paneles/tarjetas principales */
  --bg-card-inner: #182238;      /* Fondo de elementos internos (cajas de endpoints, KPIs) */
  --bg-row-hover: rgba(255, 255, 255, 0.025);

  /* Bordes y divisores (clave para el look técnico) */
  --border-subtle: rgba(255, 255, 255, 0.08);
  --border-highlight: rgba(56, 189, 248, 0.25);

  /* Textos */
  --text-primary: #f1f5f9;       /* Blanco humo nítido */
  --text-secondary: #94a3b8;     /* Gris azulado para subtítulos y cabeceras de tabla */
  --text-muted: #64748b;         /* Etiquetas menores (0h, 12h, 24h) */

  /* Acentos de Marca y Estados */
  --brand-blue: #38bdf8;         /* Azul luminoso para "PULSO API" e ícono de pulso */
  --status-green: #10b981;       /* Esmeralda para ONLINE / UP / Barras OK */
  --status-green-bg: rgba(16, 185, 129, 0.14);
  --status-green-border: rgba(16, 185, 129, 0.35);

  --status-amber: #f59e0b;       /* Naranja/Ámbar para FALLO DE TÚNEL y botón SIMULAR CAÍDA */
  --status-amber-bg: rgba(245, 158, 11, 0.14);
  --status-amber-border: rgba(245, 158, 11, 0.45);

  --status-red: #ef4444;         /* Rojo coral para DESCONECTADO / Caída */
  --status-red-bg: rgba(239, 68, 68, 0.15);
  --status-red-border: rgba(239, 68, 68, 0.40);

  /* Gráficos de Latencia */
  --chart-p50: #38bdf8;          /* Línea cian/azul con área sombreada translúcida */
  --chart-p95: #818cf8;          /* Línea violeta/índigo con área sombreada translúcida */

  /* Tipografía del sistema (Cero Google Fonts externas) */
  --font-sans: system-ui, -apple-system, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
  --font-mono: 'JetBrains Mono', 'Fira Code', ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
}
```

**Regla tipográfica (`emil-design-eng`):** Todos los números que cambian en vivo (porcentajes `99.98%`, milisegundos `12ms`, contadores) **DEBEN** tener `font-variant-numeric: tabular-nums;` para que la interfaz no tiemble cuando se actualizan por SSE.

---

## 2. Layout Exacto (Estructura de la Pantalla)

La pantalla se organiza en un layout tipo **App Shell** que entra en una vista de escritorio sin necesidad de hacer scroll excesivo (y se adapta a una columna en celulares):

### A. Barra Lateral Izquierda (Sidebar Fija de `60px` de ancho)
- Fondo `--bg-sidebar`, borde derecho `1px solid var(--border-subtle)`.
- Arriba de todo: Ícono SVG de electrocardiograma/pulso en color `--brand-blue` (`#38bdf8`).
- Navegación central (botones con íconos SVG limpios que hacen scroll suave o filtran vistas):
  1. **Dashboard** (Ícono de grilla 2x2 - Activo por defecto con fondo redondeado azul oscuro).
  2. **Consola / Logs en vivo** (Ícono `>_` terminal: abre un pequeño cajón o modal con el log de eventos SSE en tiempo real).
  3. **Arquitectura / Seguridad** (Ícono de engranaje/escudo: muestra un modal rápido explicando la arquitectura para reclutadores: HybridCache, Polly, Docker `arch-net` vs Cloudflare).
- Abajo de todo: Link directo al Portafolio de Santiago (`s-portfolio.bbasolutions.ar`) y al repositorio de GitHub.

### B. Barra Superior (Top Header)
Distribuida con Flexbox (`justify-content: space-between`, `align-items: center`):
1. **Izquierda (Título):**
   - `<span class="brand">PULSO API</span> <span class="slash">/</span> <span class="subtitle">Status Dashboard</span>`
   - `PULSO API` en azul `#38bdf8` negrita; `/ Status Dashboard` en blanco `#f1f5f9` peso medio.
2. **Centro (Píldora Global de Estado):**
   - Caja redondeada verde (`background: var(--status-green-bg)`, borde `1px solid var(--status-green-border)`).
   - Texto: **`Sistemas Operativos`** acompañado de un ícono de check circular verde brillante (con animación sutil de pulso). Si hay una simulación de caída activa, cambia dinámicamente a ámbar: **`Degradación Parcial (Lab Activo)`**.
3. **Derecha (3 Tarjetas KPI Compactas):**
   - Tres cajitas individuales con borde sutil (`background: var(--bg-card)`, `border-radius: 10px`, `padding: 8px 16px`):
     - Caja 1: Label arriba `"Uptime 24h:"` | Valor abajo grande en blanco **`99.98%`**
     - Caja 2: Label arriba `"Uptime 7d:"` | Valor abajo grande en blanco **`99.95%`**
     - Caja 3: Label arriba `"Caché Hit Ratio:"` | Valor abajo grande en blanco **`94.2%`**

---

### C. Bloque Principal Central: "Servicios Publicados"
Una tarjeta grande (`background: var(--bg-card)`, `border: 1px solid var(--border-subtle)`, `border-radius: 12px`, `padding: 20px`).

- **Encabezado del panel:**
  - **Servicios Publicados** `(nombres amigables — sin contenedores ni puertos)`
- **Tabla / Grid de 4 Columnas:**
  - Cabeceras en gris `--text-secondary`:
    1. `Nombre amigable` (30% ancho)
    2. `Red Interna (Docker)` (20% ancho)
    3. `URL Pública (Cloudflare)` (25% ancho)
    4. `Uptime 24h (Barras)` (25% ancho)
- **Filas (Las 5 filas de la imagen):**
  1. **Nutrition App** -> `[● ONLINE]` (verde) | `[● UP]` (verde) | 24 barritas verticales verdes.
  2. **n8n Automation** -> `[● ONLINE]` (verde) | `[● UP]` (verde) | 24 barritas verticales verdes.
  3. **Santiago Portfolio** -> `[● ONLINE]` (verde) | `[● UP]` (verde) | 24 barritas verticales verdes.
  4. **API Gateway** -> `[● ONLINE]` (verde) | `[● UP]` (verde, o cambia a `[● FALLO DE TÚNEL]` en naranja durante simulación).
  5. **Demo Service** -> `[● UP]` (verde) | `[● UP]` (verde, o cambia a `[● DESCONECTADO]` en rojo durante simulación).

- **Diseño de las Píldoras de Estado (Badges):**
  - `border-radius: 9999px`, `padding: 4px 12px`, `font-size: 0.78rem`, `font-weight: 600`, letra mayúscula con un puntito (`●`) del mismo color adentro.
- **Diseño del Gráfico de Barras de 24h (Derecha de cada fila):**
  - Un contenedor flex con **24 barritas verticales** (`width: 5px`, `height: 22px`, `border-radius: 2px`, `gap: 3px`).
  - Debajo de las 24 barritas, una línea finita con 3 etiquetas en `--text-muted` (`font-size: 0.7rem`): `0h` a la izquierda, `12h` al medio, `24h` a la derecha.
  - En `Demo Service`, cuando está desconectado, las barritas se ven verde oscuro apagado y la última barrita (24h) se pinta de rojo/marrón, con un tooltip al pasar el mouse que muestra el estado de esa hora.

---

### D. Fila Inferior (Grid de 2 Columnas: 68% Izquierda / 32% Derecha)

#### 1. Panel Izquierdo: "Panel de Laboratorio (Simulaciones y Métricas)"
Dividido internamente en 2 mitades separadas por una línea vertical sutil (`border-right: 1px solid var(--border-subtle)`):

- **Mitad Izquierda: Gráficos de latencia en tiempo real**
  - Subtítulo: `Gráficos de latencia en tiempo real`
  - Leyenda interactiva en vivo: `— p50 (12ms)` (en azul cian) y `— p95 (45ms)` (en violeta).
  - **Gráfico SVG dinámico suave:** Un `<svg>` con dos curvas Bezier suaves (`path`) y relleno con gradiente lineal (`linearGradient`) debajo de cada curva. Cada vez que llega un evento SSE (cada 3s), el gráfico desplaza un punto suavemente hacia la izquierda agregando la nueva lectura de latencia real de la API.

- **Mitad Derecha: Estadísticas de la caché híbrida de .NET (Memoria + Redis)**
  - Subtítulo: `Estadísticas de la caché híbrida de .NET (Memoria + Redis)`
  - **Medidor Semicircular (Gauge SVG) + Barra de Progreso:**
    - A la izquierda: Un arco semicircular SVG con gradiente cian/esmeralda que muestra `100%` (o el porcentaje de disponibilidad de caché) en el centro.
    - A la derecha del arco: Label `Hit Ratio` a la izquierda y `94.2%` a la derecha, con una barra horizontal (`height: 8px`, `border-radius: 999px`) rellena con un gradiente de azul `#38bdf8` a verde `#10b981`.
    - Debajo de la barra: Píldora técnica gris oscura con texto `Stale-While-Revalidate activo` (se ilumina en verde o ámbar cuando actúa).
  - **EL BOTÓN ESTRELLA (Call To Action para el Reclutador):**
    - Una caja-botón ancha y prominente en la parte inferior derecha del laboratorio:
    - Borde `1.5px solid var(--status-amber)`, fondo oscuro cálido `rgba(245, 158, 11, 0.08)`, `border-radius: 10px`, `padding: 12px 16px`, cursor pointer, transición suave con efecto hover luminoso.
    - Ícono de triángulo de advertencia naranja (`⚠`) a la izquierda.
    - Título en naranja/ámbar negrita: **`SIMULAR CAÍDA (Servicio de Prueba)`**
    - Subtítulo en gris claro: `Prueba el Circuit Breaker y la respuesta de la caché con datos antiguos`
    - **Interacción al hacer clic:**
      - Llama a `POST /api/v1/lab/simulate-failure`.
      - Muestra un contador regresivo en el propio botón (`Simulación activa: 45s restantes... — Circuit Breaker: OPEN`).
      - La fila `Demo Service` de arriba cambia instantáneamente a `DESCONECTADO` (Rojo), `API Gateway` muestra `FALLO DE TÚNEL` (Naranja), el gráfico de latencia muestra un pico visual y luego cae a `1ms` demostrando que la **Caché Híbrida está respondiendo con datos Stale sin bloquear al usuario**.

#### 2. Panel Derecho: "Endpoints de la API (v1)"
Tarjeta a la derecha (`background: var(--bg-card)`, `border-radius: 12px`, `padding: 20px`):
- Título: **`Endpoints de la API (v1)`**
- Lista vertical de 4 filas interactivas (cajas con fondo `--bg-card-inner`, `border-radius: 8px`, `padding: 8px 12px`). **Hacer que al hacer clic en cualquiera de los `GET`, se abra un visor rápido o copie el comando `curl` / abra el JSON real en una pestaña nueva**:
  1. Badge azul `[GET]` + texto monospace `/summary`
  2. Badge azul `[GET]` + texto monospace `/services`
  3. Badge azul `[GET]` + texto monospace `/stream (SSE)`
  4. Badge verde `[POST]` + texto monospace `/lab/simulate-failure` (al hacer clic ejecuta la misma simulación del botón naranja).
- **Pie del panel (Seguridad CORS):**
  - Ícono de candado verde (`🔒`) + texto: `CORS: s-portfolio.bbasolutions.ar`