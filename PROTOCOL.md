# RuRuServer Protocol (Minimal Spec)

This document describes the wire protocol used by this server.

## Transport

Two transports are supported, sharing the same client list, message buffer, and auth/relay rules:

1. **Raw TCP** (used by the original RuRuComms desktop client): TCP over IPv4, listen port `50512`.
2. **WebSocket** (used by the ruffleskerman.com web client): `wss://` on port `50513`, path `/ruru/`. Requires a real TLS certificate (browsers block mixed-content `ws://` from an HTTPS page), so this is exposed as `wss://ruru.ruffleskerman.com:50513/ruru/` (or an equivalent standard-port reverse-proxy mapping).
3. Max concurrent clients: `2`, counted across both transports combined.

Each logical message must be sendable/receivable as a single frame/read on either transport: one WebSocket text frame corresponds to one TCP `Read()` call's worth of bytes (up to 1024 bytes). Clients should send each logical message in a single write, per the framing notes below.

## Connection and Authentication

1. Client opens a TCP connection to `server:50512`, or a WebSocket connection to `wss://server:50513/ruru/`.
2. Client must immediately send its identity frame (server waits up to 5 seconds), as a single TCP write or a single WebSocket text frame.
3. Identity frame format:

```text
BxF_ID_<client_id>
```

4. Server validates:
   - Frame starts with `BxF_ID_`.
   - `<client_id>` (after prefix removal) exists as an exact line in `valid_ids.txt`.
5. If validation fails, server closes the connection.
6. If the server already has 2 clients, server sends:

```text
Server full. Try again later.
```

Then closes the connection.

## Message Flow

1. After authentication, client sends plain text payloads on the same connection (TCP stream or WebSocket).
2. Server relays each received payload to all other connected clients, regardless of which transport they're on (a TCP client and a WebSocket client can freely relay to each other).
3. Relay frames sent by server are newline-terminated on the TCP transport. On the WebSocket transport, the same newline-terminated string is sent as the WebSocket text frame payload (clients should trim it).

There are no HTTP endpoints, methods, or RPC names beyond the WebSocket upgrade handshake at `/ruru/`. This is raw message forwarding.

## Server Control Frames

The server may send control frames to peers:

```text
BxF_SERVER_New connection: <client_id>
BxF_SERVER_Client disconnected: <client_id>
```

## Buffering Behavior

If only one client is connected when a message arrives, server buffers it.

Internal buffer format:

```text
<sender_id>::<payload>
```

When another client connects, buffered frames are replayed only if they were not originally sent by that connecting client.

## Framing Notes

1. Server reads incoming client data in fixed chunks (1024 bytes).
2. Server does not parse client messages by newline; read boundaries are transport-driven.
3. To avoid boundary issues, clients should send each logical message in a single write and keep payloads modest in size.

## Minimal Client Sequence

1. Connect to `host:50512`.
2. Send auth frame, example:

```text
BxF_ID_alice
```

3. Start reading from socket for relays/control frames.
4. Send plain text payloads on same socket.
5. Reconnect on socket close.
