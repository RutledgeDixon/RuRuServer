# RuRuServer

Simple TCP + WebSocket relay server for RuRu:
  1. Allows up to two clients at a time (across both TCP and WebSocket connections combined)
  2. If only one client is logged on, buffers messages and sends them to the next unique client
  3. Encodes messages in the same format as RuRuComms
  4. Accepts the original desktop client over raw TCP (port `50512`) and the ruffleskerman.com web client over WebSocket (port `50513`, path `/ruru/`) — both transports share the same client list, message buffer, and auth/relay logic. See [PROTOCOL.md](PROTOCOL.md) for details.

# *Installation Instructions:*
  1. Download the release RRS.zip, extract, and store the folder in somewhere where you will remember
  2. Allow port-forwarding on your router for both the TCP port (`50512`) and the WebSocket port (`50513`) for the IP address that the server is run off of
  3. Open Task Scheduler and create a new task that runs RuRuCommsServer.exe from the folder you downloaded every time your computer starts up
     1. search for 'Task Scheduler' and run it
     2. click 'Create Task...'
     3. name the task
     4. go to the triggers tab and add an 'at startup' trigger
     5. go to the actions tab, add an action, choose 'Start a program' from the dropdown, hit the browse button, and find RuRuCommsServer.exe
     6. go to the settings tab, and uncheck the box that would stop the task after a certain number of days
  4. For the WebSocket endpoint to work over `wss://` (required by browsers since ruffleskerman.com is HTTPS), a real TLS certificate for the `ruru.ruffleskerman.com` hostname must be bound to the WebSocket port on the host. On Windows, this is done via `netsh`:
     ```
     netsh http add urlacl url=https://+:50513/ruru/ user=<account running the service>
     netsh http add sslcert ipport=0.0.0.0:50513 certhash=<cert thumbprint> appid={<any GUID>}
     ```
     A certificate can be obtained for the `ruru.ruffleskerman.com` subdomain using an ACME client such as win-acme, renewed automatically, and re-bound with the commands above after renewal.

# Linux host setup for ruffleskerman.com/ruru

If you want to serve the web client at `wss://ruffleskerman.com/ruru`, run RuRuServer on localhost and terminate TLS at Nginx.

## 1. Build and publish

```bash
dotnet restore
dotnet publish -c Release -r linux-x64 --self-contained false -o ./publish
```

Copy `valid_ids.txt` into `./publish` (same folder as the executable).

## 2. Run server with env vars

On Linux, RuRuServer defaults to an internal `http://` WebSocket listener, which is intended to sit behind a reverse proxy.

```bash
export RURU_TCP_PORT=50512
export RURU_WS_PORT=50513
export RURU_WS_PATH=/ruru/
export RURU_WS_SCHEME=http
export RURU_WS_HOST=127.0.0.1
./publish/RuRuCommsServer
```

## 3. Nginx reverse proxy (TLS + WebSocket upgrade)

Use an HTTPS server block for `ruffleskerman.com` and add:

```nginx
location /ruru {
    proxy_pass http://127.0.0.1:50513/ruru/;
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "upgrade";
    proxy_set_header Host $host;
    proxy_read_timeout 3600;
}
```

Then reload Nginx:

```bash
sudo nginx -t && sudo systemctl reload nginx
```

## 4. DNS and firewall

1. Point `ruffleskerman.com` (or `ruru.ruffleskerman.com`) DNS A/AAAA record to your server.
2. Open inbound `443` for HTTPS (for web client/WebSocket) and `50512` for raw TCP desktop clients.
3. Keep `50513` private (localhost only) when using Nginx.

## 5. Optional systemd service

Create `/etc/systemd/system/ruru.service`:

```ini
[Unit]
Description=RuRuCommsServer
After=network.target

[Service]
WorkingDirectory=/opt/ruru
ExecStart=/opt/ruru/RuRuCommsServer
Environment=RURU_TCP_PORT=50512
Environment=RURU_WS_PORT=50513
Environment=RURU_WS_PATH=/ruru/
Environment=RURU_WS_SCHEME=http
Environment=RURU_WS_HOST=127.0.0.1
Restart=always
RestartSec=3
User=www-data
Group=www-data

[Install]
WantedBy=multi-user.target
```

Enable and start:

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now ruru
sudo systemctl status ruru
```
