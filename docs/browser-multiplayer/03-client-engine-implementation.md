# Multijugador en navegador — Cliente y motor (implementación)

Especificación para cambios en el fork OpenRA (WASM) y en el host web. Complementa [01-architecture-and-scope.md](./01-architecture-and-scope.md) y [02-proxy-and-infrastructure.md](./02-proxy-and-infrastructure.md).

## 1. Objetivo técnico

Proveer una implementación de `OpenRA.Network.IConnection` que:

- Se comporte como `NetworkConnection` desde la perspectiva de `OrderManager` y del lobby.
- Use **WebSocket** (o WebTransport vía interop) hacia el URL del proxy en lugar de `TcpClient`.

Referencia: `OpenRA.Game/Network/Connection.cs` — métodos `IConnection`: `LocalClientId`, `StartGame`, `Send`, `SendImmediate`, `SendSync`, `Receive`, `Dispose`.

## 2. Contrato con el protocolo existente

`NetworkConnection` hace:

1. **Conexión**: hilos que llaman `TcpClient.Connect`; luego `NetworkConnectionReceive` lee del stream:
   - `handshakeProtocol` (int32) debe ser `ProtocolVersion.Handshake`.
   - `clientId` (int32).
   - Bucle: `len` (int32), `client` (int32), `buf` (len bytes) → encola en `receivedPackets`.

2. **Envío**: `Send(byte[])` escribe al stream: longitud + payload, posiblemente seguido de sync serializados (ver mismo archivo).

La nueva clase (nombre sugerido: `BrowserTunnelConnection` o `WebSocketNetworkConnection`) debe producir **exactamente la misma secuencia de bytes** en el “stream lógico” que el servidor espera, tras el túnel.

## 3. Buffer de lectura

WASM recibirá datos en **fragmentos** (frames WebSocket). Implementar un **buffer acumulativo**:

- Mientras haya datos, intentar parsear int32s y blobs según el mismo orden que `NetworkConnectionReceive`.
- Si faltan bytes para el siguiente campo, esperar el siguiente frame (callback/async) sin perder orden.

Esto es equivalente a un `NetworkStream` pero alimentado por eventos WebSocket.

## 4. Hilos y runtime browser

`NetworkConnection` usa varios `Thread` (connect, receive). En .NET browser/WASM:

- Revisar restricciones del runtime: muchas operaciones deben volver al **hilo principal** o usar `Task` + `SynchronizationContext`.
- Opciones de diseño:
  - **JS interop**: `WebSocket` en JavaScript; callbacks marshalling a .NET en el thread correcto para encolar paquetes.
  - **API .NET**: `ClientWebSocket` si está soportada en el target `browser` del proyecto y respeta el modelo de threading del host.

**Requisito**: `IConnection.Receive` sigue siendo invocado desde el loop del juego (como hoy); las llegadas de red solo **encolan**; no ejecutar lógica de mundo en callbacks de red crudos sin serializar al tick.

## 5. Estados de conexión

Reutilizar `ConnectionState` (`PreConnecting`, `Connecting`, `Connected`, `NotConnected`) y el patrón de `Game.ConnectionStateChanged` + `ConnectionLogic` para mostrar paneles de error/reintento igual que escritorio.

- Al fallar el WebSocket: `NotConnected`, mensaje localizado si aplica.
- **No** reutilizar hilos de `NetworkConnectionConnect` sin adaptar: sustituir por async o polling en tick hasta `Connected`.

## 6. Integración con `Game.JoinServer`

Hoy:

- `Game.JoinServer(ConnectionTarget endpoint, string password)` crea `new NetworkConnection(endpoint)`.

Añadir sobrecarga o rama:

- `Game.JoinServer(BrowserTunnelEndpoint spec, string password)` donde `spec` contiene al menos la **URL `wss://...`** (y opcional path/query para token).

O extender `ConnectionTarget` con un flag `UseWebTunnel` + URL — decidir según convenciones del fork para no romper escritorio.

**Importante**: builds `OPENRA_BROWSER` deben usar el túnel; builds de escritorio siguen con TCP.

## 7. UI mínima (join-only)

Sin panel de crear servidor:

- **Opción A**: argumento de arranque o query parseada por `OpenRA.BrowserHost` antes de `Game.Run` / al cargar mod — llama a join automático al túnel configurado.
- **Opción B**: botón en chrome YAML condicionado a browser que abre un único destino hardcodeado o leído de `Game.Settings` / manifest browser.

Flujo sugerido:

1. Mostrar `CONNECTING_PANEL` o equivalente.
2. `JoinServer(tunnel, password)`.
3. En `Connected`, abrir `SERVER_LOBBY` como hace `ConnectionLogic` tras join exitoso.

Referencia: `OpenRA.Mods.Common/Widgets/Logic/ConnectionLogic.cs`, `MultiplayerLogic.cs` (solo la parte de join si se reutiliza).

## 8. Contraseña y mods

- Si el servidor exige contraseña, el mismo mecanismo que escritorio (`CurrentServerSettings.Password`, diálogo de retry).
- **Versión de mod/engine** debe coincidir con el dedicado; el handshake fallará si no — documentar para operadores.

## 9. Archivos probables a tocar

| Área | Archivos (indicativos) |
|------|-------------------------|
| Red | `OpenRA.Game/Network/Connection.cs` (nueva clase o partial), posible `BrowserTunnelConnection.cs` |
| Game | `OpenRA.Game/Game.cs` — sobrecarga `JoinServer`, `ConnectionStateChanged` |
| Plataforma | `OpenRA.Platforms.Browser` o JS en `OpenRA.BrowserHost/wwwroot` — WebSocket create |
| Host | `OpenRA.BrowserHost` — pasar URL desde configuración Blazor/appsettings |
| UI | `MainMenuLogic.cs` o chrome YAML — entrada join-only |
| Condicional | `#if OPENRA_BROWSER` donde haga falta evitar `TcpClient` en browser |

## 10. Pruebas

1. **Unitarias** (si el proyecto las tiene): buffer de reensamblado de frames → parse igual que `NetworkConnectionReceive`.
2. **Integración**: dedicado local + proxy local + WASM en dev — unirse, pasar lobby, iniciar partida corta, verificar sync.
3. **Regresión**: skirmish loopback browser sigue funcionando (`CreateBrowserLoopbackLocalServer`).

## 11. WebTransport (fase opcional)

- Exponer en JS `WebTransport` y un stream bidireccional; marshalling de `Uint8Array` ↔ .NET `byte[]` o `Memory<byte>`.
- Misma capa de buffer que WebSocket.
- Feature-detect en runtime: caer a WebSocket si no hay soporte.

## 12. Checklist de aceptación v1

- [ ] Jugador web puede conectar al dedicado a través del proxy y ver el lobby.
- [ ] Puede iniciar la partida con otros clientes (web o escritorio) sin desync atribuible al túnel.
- [ ] Desconexión y error de red degradan con UI clara.
- [ ] Skirmish local en browser no regresa.
- [ ] Documentación de operación del proxy enlazada desde README o wiki interna del fork.
