# Multijugador en navegador — Proxy e infraestructura

Especificación del **servicio de túnel** entre el navegador y el servidor dedicado OpenRA. Complementa [01-architecture-and-scope.md](./01-architecture-and-scope.md).

## 1. Función

Por cada sesión de jugador web:

1. Aceptar una conexión entrante **WebSocket seguro** (`wss://`) o **WebTransport** (según despliegue).
2. Abrir **una** conexión TCP al proceso del servidor dedicado (`host:port` configurable, típicamente red privada o `127.0.0.1` si el proxy coexiste con el dedicado).
3. Reenviar bytes **en orden** en ambas direcciones hasta que cualquier extremo cierre.

El dedicado **no** debe modificarse para v1 si el túnel es transparente.

## 2. Semántica de bytes (WebSocket)

TCP es un **stream** continuo; WebSocket expone **mensajes** binarios.

**Requisito**: preservar orden y contenido del stream.

- **Envío navegador → TCP**: cada frame binario recibido del cliente se **concatena en orden** al stream TCP (o se escribe cada payload completo al `NetworkStream` en secuencia). No reordenar ni omitir frames.
- **TCP → navegador**: los bytes leídos del TCP se pueden empaquetar en frames binarios de tamaño razonable (p. ej. hasta 16–64 KiB); el cliente WASM debe **reensamblar** en un único stream lógico antes de pasarlo al parser que replica `NetworkConnectionReceive` (lectura secuencial de `int32`, etc.).

**Alternativa equivalente**: un solo mensaje WS por “chunk” leído del socket con `ReadAsync`; el cliente acumula en un buffer hasta completar el siguiente marco lógico del protocolo OpenRA. Lo importante es que la secuencia de bytes vista por la capa de protocolo sea **idéntica** a la de un `TcpClient` directo.

## 3. Semántica de bytes (WebTransport)

Usar un **bidirectional stream** por jugador:

- Un extremo del stream ↔ `NetworkStream` del TCP al dedicado.
- Mismo criterio de orden y reensamblado si el stack parte el stream en chunks.

No usar **datagramas** WT para el flujo principal del protocolo OpenRA (lockstep ordenado).

## 4. Configuración del proxy

Parámetros mínimos (entorno o fichero):

| Parámetro | Descripción |
|-----------|-------------|
| `LISTEN` | Interfaz/puerto para `wss` o HTTP/3 (p. ej. `:443` detrás de reverse proxy). |
| `TARGET_HOST` | Hostname/IP del servidor OpenRA (vista desde el proxy). |
| `TARGET_PORT` | Puerto TCP del dedicado (p. ej. 1234 según mod). |
| `TLS_CERT` / `TLS_KEY` | Certificado para `wss` o QUIC. |

Opcional v1+:

- `MAX_CONCURRENT` — límite de túneles simultáneos.
- `IDLE_TIMEOUT` — cierre por inactividad.
- Cabecera o subpath por **token** de sala si un mismo proxy enruta a varios puertos internos.

## 5. Despliegue típico

```text
Internet --TLS--> [Reverse proxy nginx/caddy] --> [Proceso proxy túnel] --TCP--> OpenRA dedicated
```

O el proxy escucha directamente en 443 con certificado propio.

**Red**: el dedicado puede quedar solo en red privada; solo el proxy necesita exposición pública. Firewall: permitir UDP/443 si se usa QUIC/WebTransport.

## 6. Seguridad

- **TLS obligatorio** en el tramo browser → proxy.
- El tramo proxy → dedicado suele ser **red de confianza** (misma VPC); si cruza internet, considerar TLS o VPN — evaluación aparte.
- **Rate limiting** en el proxy contra abuso de handshakes.
- El servidor OpenRA en modo multijugador puede exigir **autenticación de jugador** (huella/firma); el proxy no debe alterar el cuerpo del handshake — ver `OpenRA.Game/Server/Server.cs` (`IsMultiplayer`).

## 7. Autenticación en el borde (opcional, fase 2)

Si se desea restringir quién abre el túnel:

- Validar JWT o cookie en el upgrade WebSocket **antes** de conectar al TCP; o
- Path secreto `/play/<token>` mapeado a un backend que verifica token y solo entonces abre TCP.

Esto es **adicional** al protocolo OpenRA; no sustituye el handshake interno del juego.

## 8. Implementación de referencia (lenguaje)

Cualquier runtime con:

- Servidor WebSocket seguro (p. ej. ASP.NET Core, Go, Node con `ws` detrás de TLS, etc.).
- `TcpClient`/`net.Dial` hacia el dedicado.

No hay dependencia del lenguaje del motor del juego en el proxy.

## 9. Pruebas del proxy

1. Conectar con un cliente WebSocket de prueba que envíe los primeros bytes que un cliente OpenRA enviaría tras conectar (o usar test de integración con el motor cuando exista `IConnection` browser).
2. Verificar que el dedicado acepta la conexión y completa handshake como con escritorio.
3. Prueba de carga: N túneles concurrentes, verificar file descriptors y CPU.

## 10. Coexistencia con master server

Los jugadores de **escritorio** siguen conectando por IP:puerto o lista pública. El anuncio (`MasterServerPinger`) no cambia.

Los jugadores **web** ignoran la dirección pública TCP del anuncio y usan la **URL del proxy** configurada en el sitio.
