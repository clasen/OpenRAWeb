# Servidor OpenRA local con acceso público vía ngrok

Guía para exponer un **servidor dedicado** en tu PC usando [ngrok](https://ngrok.com/) y jugar con alguien por internet. Complementa [04-configuracion-multijugador-desde-el-navegador.md](./04-configuracion-multijugador-desde-el-navegador.md) y [02-proxy-and-infrastructure.md](./02-proxy-and-infrastructure.md).

**Requisitos comunes:** [ngrok](https://ngrok.com/download) instalado y cuenta configurada (`ngrok config add-authtoken …`). Mismo **mod** y **versión** de engine entre anfitrión e invitado.

### Script todo-en-uno (repositorio)

En la raíz del clon, [`run-browser-multiplayer-ngrok.sh`](../../run-browser-multiplayer-ngrok.sh) levanta la pila y ngrok en un solo comando:

```bash
./run-browser-multiplayer-ngrok.sh              # navegador: dedicado + proxy + host + 2× ngrok http → imprime URL pública
./run-browser-multiplayer-ngrok.sh --open       # igual y abre el navegador
./run-browser-multiplayer-ngrok.sh --tcp-only   # solo dedicado + ngrok tcp (amigo en escritorio)
```

Opciones alineadas con [`run-browser-multiplayer.sh`](../../run-browser-multiplayer.sh): `--mod`, `--no-build`, `--`, argumentos extra al dedicado. Variables: `LISTEN`, `TARGET_HOST`, `TARGET_PORT`, `BROWSER_HOST_PORT`, `NGROK_API_ADDR` (si el agente API no está en `127.0.0.1:4040`). `Ctrl+C` detiene dedicado, proxy, host y ngrok.

---

## Elegir flujo

| Invitado usa… | Túnel ngrok | Complejidad |
|----------------|-------------|-------------|
| Cliente OpenRA **escritorio** | `ngrok tcp` al puerto del dedicado | Baja (un túnel) |
| Cliente **navegador** (WASM) | Dos túneles `ngrok http` (host + proxy WebSocket) | Media |

---

## Escenario A — Invitado en escritorio

El dedicado escucha **TCP** (por defecto **1234**, ver `ListenPort` en [`OpenRA.Game/Settings.cs`](../../OpenRA.Game/Settings.cs) / `ServerSettings`).

Atajo: `./run-browser-multiplayer-ngrok.sh --tcp-only`.

1. **Arranca el dedicado** desde la raíz del repositorio:
   ```bash
   ./run-dedicated-server.sh
   ```
   Ejemplos de argumentos:
   ```bash
   ./run-dedicated-server.sh Game.Mod=cnc Server.Name="Partida pública"
   ./run-dedicated-server.sh Server.ListenPort=5678
   ```
   Si cambias el puerto, usa ese valor en el paso 2.

2. **Abre un túnel TCP** (otra terminal):
   ```bash
   ngrok tcp 1234
   ```
   Sustituye `1234` si usaste `Server.ListenPort=…`.

3. En la salida de ngrok, copia la dirección pública (p. ej. `tcp://0.tcp.eu.ngrok.io:#####` → en el cliente OpenRA suele introducirse **host** `0.tcp.eu.ngrok.io` y **puerto** `#####` en conexión directa, según cómo pida el UI el destino).

4. **Seguridad:** la URL TCP es pública; cualquiera puede intentar conectar. Opcional: `Server.Password=…` en el dedicado.

---

## Escenario B — Invitado en el navegador

Necesitas **tres procesos locales** (como en [04](./04-configuracion-multijugador-desde-el-navegador.md)): dedicado, proxy WebSocket→TCP, host Blazor. Puertos típicos: dedicado **1234**, proxy **8787**, host **5284** (el host puede variar si el puerto está ocupado).

Atajo: `./run-browser-multiplayer-ngrok.sh` (construye, arranca todo, crea los túneles y muestra `[public] …`).

1. Arranca el stack (una terminal):
   ```bash
   ./run-browser-multiplayer.sh --mod ra
   ```
   O manualmente: `run-dedicated-server.sh`, `run-browser-tunnel-proxy.sh`, `run-browser-host.sh` (ajusta `TARGET_PORT` del proxy si el dedicado no usa 1234).

2. **Dos túneles HTTP** hacia tus servicios locales (HTTPS en el extremo público; el navegador necesita `https://` y `wss://`):
   ```bash
   ngrok http 5284
   ngrok http 8787
   ```
   Usa el puerto real que imprima `run-browser-host.sh` para el primer túnel.

3. **URL para el invitado:** base del juego en HTTPS + query `tunnel` en WSS al segundo túnel.

   Ejemplo (sustituye por tus subdominios ngrok):
   ```bash
   HOST_HTTP='https://abc123.ngrok-free.app/' \
   TUNNEL_WS='wss://def456.ngrok-free.app/' \
   ./print-browser-multiplayer-join-url.sh
   ```

   O pasando la URL del host como primer argumento:
   ```bash
   TUNNEL_WS='wss://def456.ngrok-free.app/' \
   ./print-browser-multiplayer-join-url.sh 'https://abc123.ngrok-free.app/'
   ```

   El proxy acepta el handshake WebSocket en la raíz (`GET /` con upgrade); no hace falta path extra en `wss://…/` salvo que configures rutas con token en el proxy (ver README en `tools/OpenRA.BrowserTunnelProxy/`).

4. **Plan gratuito de ngrok:** la página intersticial “Visit Site” a veces molesta a cargas automáticas del WASM. Si falla, revisa la documentación de ngrok para desarrollo o un plan que permita omitirla.

5. **Tú en escritorio, invitado en navegador:** deja dedicado + proxy + host en marcha; tú conéctate por TCP a `127.0.0.1:1234` (o el `ListenPort` que uses). El invitado usa solo la URL HTTPS con `?tunnel=wss://…`.

---

## Ejemplo `ngrok.yml` (v3, dos túneles)

Guarda en un fichero (p. ej. `ngrok-openra.yml`) y ajusta puertos. Ejecuta: `ngrok start --all --config ngrok-openra.yml`.

```yaml
version: "3"
agent:
  authtoken: YOUR_TOKEN_HERE   # o usa ngrok config add-authtoken
tunnels:
  browser-host:
    proto: http
    addr: 5284                 # mismo puerto que run-browser-host.sh
  browser-tunnel:
    proto: http
    addr: 8787                 # LISTEN del BrowserTunnelProxy
```

Tras arrancar, ngrok mostrará dos URLs `https://…`. Asigna la que corresponda al host Blazor como `HOST_HTTP` y la del proxy como `TUNNEL_WS` (con esquema `wss://`).

Para **solo escritorio**, no hace falta este fichero: basta `ngrok tcp 1234`.

---

## Alineación de puertos

| Componente | Variable / argumento |
|------------|----------------------|
| Dedicado | `Server.ListenPort=PORT` |
| Proxy → dedicado | `TARGET_PORT` (entorno de `run-browser-tunnel-proxy.sh`) |
| ngrok tcp | Mismo `PORT` que el dedicado |

---

## Referencias

- Scripts: [`run-browser-multiplayer-ngrok.sh`](../../run-browser-multiplayer-ngrok.sh), [`run-dedicated-server.sh`](../../run-dedicated-server.sh), [`run-browser-multiplayer.sh`](../../run-browser-multiplayer.sh), [`run-browser-tunnel-proxy.sh`](../../run-browser-tunnel-proxy.sh), [`run-browser-host.sh`](../../run-browser-host.sh), [`print-browser-multiplayer-join-url.sh`](../../print-browser-multiplayer-join-url.sh).
- Configuración local detallada: [04-configuracion-multijugador-desde-el-navegador.md](./04-configuracion-multijugador-desde-el-navegador.md).
