# OpenRA.BrowserTunnelProxy

**WebSocket → TCP** tunnel between the browser client and a dedicated OpenRA server. Preserves byte order: each binary WebSocket message (including fragments) is written to TCP in order; TCP reads are sent as binary frames (≤ 64 KiB per frame). Matches [docs/browser-multiplayer/02-proxy-and-infrastructure.md](../../docs/browser-multiplayer/02-proxy-and-infrastructure.md).

The same URL path `/` accepts the WebSocket upgrade (client `ws://…/`) and a plain **GET /** for a short status text — a separate `MapGet("/")` would break the handshake.

WebTransport / QUIC is not implemented here; terminate TLS in front of this process or use Kestrel HTTPS below.

## Run

```bash
dotnet run --project OpenRA.BrowserTunnelProxy.csproj
```

Override with environment variables or `appsettings.{Environment}.json` (same keys).

## Configuration

| Parameter | Description |
|-----------|-------------|
| `LISTEN` | Semicolon-separated listen URLs (default `http://127.0.0.1:8787`). Shorthand `:8787` → `http://0.0.0.0:8787`. Falls back to `ASPNETCORE_URLS` if `LISTEN` is unset. |
| `TARGET_HOST` | Hostname/IP of the OpenRA dedicated server (from the proxy’s network view). |
| `TARGET_PORT` | Default TCP port when not using `TOKEN_ROUTES`. |
| `TLS_CERT` | Path to PEM certificate **or** `.pfx` / `.p12` (see `TLS_KEY`). |
| `TLS_KEY` | Path to PEM private key (required with PEM `TLS_CERT`). |
| `MAX_CONCURRENT` | Maximum simultaneous tunnels; `0` = unlimited. Over limit → HTTP 503 before WebSocket accept. |
| `IDLE_TIMEOUT` | Seconds without traffic on either leg; `0` = disabled. Closes the session when exceeded. |
| `RATE_LIMIT_PER_MINUTE` | WebSocket upgrade attempts per client IP per rolling minute; `0` = disabled. Over limit → HTTP 429. |
| `EDGE_TOKEN` | If set, clients must send this value as header `X-OpenRA-Tunnel-Token` **or** query `token=`. |
| `TOKEN_ROUTES` | JSON object mapping room id → TCP port. When non-empty, WebSocket path must be `/play/{room}` (e.g. `ws://host/play/alpha`). |

### TLS (browser → proxy)

For `https://` / `wss://` entries in `LISTEN`, provide `TLS_CERT` + `TLS_KEY` (PEM) or a passwordless `.pfx` in `TLS_CERT` alone.

Production often uses **Internet → TLS → reverse proxy → this proxy (HTTP) → OpenRA TCP** instead.

### Edge token (optional, doc §7)

`EDGE_TOKEN` is a simple shared secret; it does not replace OpenRA’s in-game handshake.

## Logs

After a successful WebSocket upgrade (HTTP **101**), Kestrel may still print *“the application aborted the connection”* when the tunnel session ends. That usually means **one side closed** (browser, game client, or dedicated TCP), not a failed handshake. This proxy also logs **Tunnel: session ended** with `WebSocket` state and TCP status for clarity.

## Try with the WASM client

Default (no `TOKEN_ROUTES`, no `EDGE_TOKEN`):

`?tunnel=ws://127.0.0.1:8787/`

With room routing (`TOKEN_ROUTES` has `alpha` → port):

`?tunnel=ws://127.0.0.1:8787/play/alpha`

With `EDGE_TOKEN=secret`:

`?tunnel=ws://127.0.0.1:8787/?token=secret`  
(or configure the client to send header `X-OpenRA-Tunnel-Token` — not wired in the current Blazor host; extend `BrowserTunnelConnection` / host if needed.)

## Tests (doc §9)

1. Use any WebSocket client to open the tunnel URL, send raw bytes; confirm the dedicated server sees a normal TCP connection.
2. Load test: raise `MAX_CONCURRENT` and open *N* tunnels; watch process fds / CPU.
