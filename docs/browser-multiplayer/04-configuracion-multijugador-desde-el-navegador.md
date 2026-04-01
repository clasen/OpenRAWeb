# Configuración del multijugador desde el navegador

Guía operativa para **unirse a una partida** con el cliente OpenRA en **WebAssembly** (navegador), usando el **túnel WebSocket → TCP** hacia un **servidor dedicado**. Complementa la arquitectura en [01-architecture-and-scope.md](./01-architecture-and-scope.md), el proxy en [02-proxy-and-infrastructure.md](./02-proxy-and-infrastructure.md) y el motor en [03-client-engine-implementation.md](./03-client-engine-implementation.md).

## Scripts del flujo local (resumen)

Todos viven en la **raíz del repositorio**. Ejecútalos desde ahí (`cd` al clon).

| Paso | Script | Qué hace |
|------|--------|----------|
| 0 — todo en uno | [`run-browser-multiplayer.sh`](../../run-browser-multiplayer.sh) | Build incremental + dedicado + túnel + host web en una sola terminal. |
| 0b — todo en uno + ngrok | [`run-browser-multiplayer-ngrok.sh`](../../run-browser-multiplayer-ngrok.sh) | Igual + túneles ngrok e impresión de URL pública (ver [05-ngrok-public-server.md](./05-ngrok-public-server.md)). |
| 1 — compilar | [`build-browser-multiplayer-local.sh`](../../build-browser-multiplayer-local.sh) | `OpenRA.sln` en modo escritorio, host Blazor WASM y proxy Release. |
| 2 — dedicado | [`run-dedicated-server.sh`](../../run-dedicated-server.sh) | Servidor TCP del juego (`Game.Mod=ra` por defecto; `Game.Mod=cnc` para TD). |
| 3 — túnel | [`run-browser-tunnel-proxy.sh`](../../run-browser-tunnel-proxy.sh) | WebSocket → TCP hacia el dedicado (`LISTEN` / `TARGET_*` por variables de entorno). |
| 4 — página web | [`run-browser-host.sh`](../../run-browser-host.sh) | Sirve el cliente WASM e imprime la URL HTTP base. |
| 5 — URL de join | [`print-browser-multiplayer-join-url.sh`](../../print-browser-multiplayer-join-url.sh) | Imprime la URL con `?tunnel=ws://…` para pegar en el navegador. |
| (opc.) contenido RA web | [`copy-ra-content-to-browser.sh`](../../copy-ra-content-to-browser.sh) | Copia assets de Red Alert al `wwwroot` del host (otro mod puede tener flujo distinto). |

---

## Inicio rápido (ejemplo local, mod CnC)

Valores por defecto: dedicado **TCP 1234**, proxy WebSocket **8787**, túnel **`ws://127.0.0.1:8787/`**.

### Modo recomendado (una sola terminal)

```bash
./run-browser-multiplayer.sh --mod ra
```

Para abrir el navegador automáticamente en macOS:

```bash
./run-browser-multiplayer.sh --open
```

Ese script:

- compila en modo incremental (solo recompila si hubo cambios),
- levanta dedicado + túnel + browser host,
- **espera** a que el host esté escuchando en el puerto HTTP antes de mostrar la URL (si el build del host falla, sale con error en lugar de fingir que todo va bien),
- imprime la URL final de join,
- y al hacer `Ctrl+C` cierra todo junto.

Por defecto el host se arranca en **Debug** (`BROWSER_HOST_CONFIGURATION`); en **Release** el proyecto WASM puede fallar a compilar (p. ej. `CS8795` en interop JS) según el grafo de build.

> Si ya compilaste antes y quieres arranque rápido: `./run-browser-multiplayer.sh --no-build`.

### Modo manual (3 terminales)

1. **Compilar (una vez o tras cambios grandes)**  
   `./build-browser-multiplayer-local.sh`

2. **Terminal 1 — servidor de juego**  
   `./run-dedicated-server.sh`  
   Déjalo abierto. Si el dedicado no usa `1234`, anota el puerto y en la terminal 3 exporta `TARGET_PORT=…` antes del script del proxy.

3. **Terminal 2 — túnel Web → TCP**  
   `./run-browser-tunnel-proxy.sh`  
   (Equivalente a fijar `LISTEN`, `TARGET_HOST`, `TARGET_PORT`; ver §4.)

4. **Terminal 3 — host Blazor**  
   `./run-browser-host.sh`  
   Copia la URL base que imprime (p. ej. `http://127.0.0.1:5284/`).

5. **URL completa para el navegador**  
   `./print-browser-multiplayer-join-url.sh 'http://127.0.0.1:5284/'`  
   Sustituye por la URL real del paso 4. Abre en el navegador la línea que salga.

Listo: el cliente web intenta unirse al lobby del dedicado. Por defecto todo usa **cnc**; necesitas **contenido del mod** para WASM según tu fork.

**Otro jugador en escritorio:** TCP directo a `127.0.0.1:1234` (sin proxy).

Lo demás de este documento es detalle (TLS, tokens, producción, fallos).

---

## 1. Flujo en una frase

El navegador no abre TCP al dedicado: abre **WebSocket** (`ws://` / `wss://`) al **proxy**; el proxy mantiene un **TCP** hacia `host:puerto` del servidor OpenRA. El protocolo de juego es el mismo que en escritorio; solo cambia el transporte hasta el proxy.

## 2. Requisitos

| Qué | Por qué |
|-----|---------|
| **Mismo mod y versión** de engine que el dedicado | El handshake fallará si no coinciden reglas y binarios. |
| **Contenido del mod** disponible para WASM | Copia o montaje HTTP según tu flujo (p. ej. script de contenido para el host web). |
| **Runtime .NET** alineado con el repo | Tras compilar, el dedicado y herramientas usan el TFM del proyecto (p. ej. `net9.0`). |
| **Proxy accesible** desde el navegador | En local: `ws://127.0.0.1:…`; en internet: `wss://…` detrás de TLS. |

**Alcance v1 (join-only):** desde el navegador no se crea servidor público ni se lista el master server dentro del juego; la URL del túnel se configura en el sitio o en la query (ver §5).

## 3. Servidor dedicado OpenRA

**Script:** [`run-dedicated-server.sh`](../../run-dedicated-server.sh)

El game server escucha **TCP** (habitualmente **1234**).

- Ajusta `Engine.EngineDir=..` y **`Game.Mod=ra`** (o `cnc` para TD) si no pasas `Game.Mod=…`.
- Antes de arrancar ejecuta `dotnet build OpenRA.sln -p:OpenRaBrowserBuild=false` para que `./bin/` sea **escritorio** (evita DLLs Wasm mezcladas y mensajes falsos `[browser]`).

Para otro mod: `./run-dedicated-server.sh Game.Mod=ra` y en el navegador `?mod=ra`.

Los avisos de *master server* en local son normales.

## 4. Proxy de túnel (WebSocket → TCP)

**Script:** [`run-browser-tunnel-proxy.sh`](../../run-browser-tunnel-proxy.sh)  
Implementación: [tools/OpenRA.BrowserTunnelProxy](../../tools/OpenRA.BrowserTunnelProxy/README.md).

Variables de entorno (por defecto, local):

| Variable | Por defecto | Significado |
|----------|-------------|-------------|
| `LISTEN` | `http://127.0.0.1:8787` | Donde el proxy acepta el upgrade WebSocket. |
| `TARGET_HOST` | `127.0.0.1` | Host del dedicado visto desde el proxy. |
| `TARGET_PORT` | `1234` | Puerto TCP del dedicado (sin `TOKEN_ROUTES`). |

Ejemplo con puerto distinto:

```bash
TARGET_PORT=5678 ./run-browser-tunnel-proxy.sh
```

Para configuración avanzada (`TLS_CERT`, `EDGE_TOKEN`, etc.) sigue [02-proxy-and-infrastructure.md](./02-proxy-and-infrastructure.md) y el README del proxy; puedes invocar `dotnet run` a mano o ampliar el script.

**URL WebSocket del cliente** (sin token ni rutas): `ws://127.0.0.1:8787/` (ajusta host/puerto a `LISTEN`).

## 5. Host web (Blazor) y parámetros de auto-join

**Script:** [`run-browser-host.sh`](../../run-browser-host.sh)

Elige un **puerto HTTP libre** (desde 5284) e imprime la URL base.

**Script de URL de join:** [`print-browser-multiplayer-join-url.sh`](../../print-browser-multiplayer-join-url.sh)

```bash
./print-browser-multiplayer-join-url.sh 'http://127.0.0.1:5284/'
```

Variables opcionales: `TUNNEL_WS` (p. ej. otra ruta de WebSocket), o pasa la base HTTP como primer argumento.

### Query string

| Parámetro | Efecto |
|-----------|--------|
| `tunnel` | URL WebSocket del proxy → `Browser.TunnelUrl=` y auto-join. |
| `tunnelToken` | `Browser.TunnelEdgeToken=` si el proxy usa `EDGE_TOKEN`. |
| `mod` | `Game.Mod=`; por defecto **cnc**, igual que el dedicado. |

## 6. Orden recomendado para una prueba local

1. **`./build-browser-multiplayer-local.sh`** (si hace falta compilar).
2. **`./run-dedicated-server.sh`**
3. **`./run-browser-tunnel-proxy.sh`**
4. **`./run-browser-host.sh`**
5. **`./print-browser-multiplayer-join-url.sh '…'`** con la URL del paso 4 → abrir en el navegador.

## 7. TLS y producción

En internet el tramo navegador → proxy debe usar **`wss://`**. Suele usarse un reverse proxy delante; detalle en [02-proxy-and-infrastructure.md](./02-proxy-and-infrastructure.md) §4–§5.

## 8. Fallos frecuentes

| Síntoma | Comprobación |
|---------|----------------|
| `curl http://127.0.0.1:8787/` devuelve 200 pero el juego dice *WebSocket connection failed* | Proxy antiguo que interceptaba `GET /` y el handshake. Actualiza, `dotnet build tools/OpenRA.BrowserTunnelProxy/...`, reinicia `./run-browser-tunnel-proxy.sh`. |
| El proxy responde **502** | Dedicado parado o `TARGET_PORT` mal; arranca `./run-dedicated-server.sh` antes. |
| *Server is running an incompatible mod* | Alinea mod (`cnc` por defecto) y versión `mod.yaml`; `?mod=…` en el navegador. |
| **101** y luego *aborted* en Kestrel | Suele ser cierre normal del túnel; revisa logs **Tunnel: session ended** del proxy y el dedicado. |
| El dedicado muestra *is experiencing connection problems* al rato | Evita dejar la pestaña en segundo plano durante la prueba; algunos navegadores limitan el loop de juego en background. Si ocurre incluso con pestaña activa, actualiza al build con keepalive del túnel desacoplado del tick del mundo. |
| Mensajes `[browser]` en consola del dedicado | Ejecuta `./build-browser-multiplayer-local.sh` o `./run-dedicated-server.sh` (fuerza build escritorio). |
| No conecta al túnel | Proxy en marcha; firewall; URL `ws`/`wss` correcta. |
| 401/403 en upgrade WS | `EDGE_TOKEN` o path `/play/…` según README del proxy. |
| Puerto HTTP del host ocupado | `run-browser-host.sh` elige otro puerto; usa esa URL en `print-browser-multiplayer-join-url.sh`. |

## 9. Referencias rápidas

- Acceso público con **ngrok** (invitado escritorio o navegador): [05-ngrok-public-server.md](./05-ngrok-public-server.md).
- Scripts: `build-browser-multiplayer-local.sh`, `run-dedicated-server.sh`, `run-browser-tunnel-proxy.sh`, `run-browser-host.sh`, `print-browser-multiplayer-join-url.sh`, `copy-ra-content-to-browser.sh`.
- README raíz: [README.md](../../README.md) (*Browser multiplayer*).
