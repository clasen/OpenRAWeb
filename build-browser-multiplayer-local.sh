#!/usr/bin/env bash
# One-shot build for local browser multiplayer dev: desktop bin (dedicado), Wasm host, tunnel proxy.
# From repo root. Next: run-dedicated-server.sh | run-browser-tunnel-proxy.sh | run-browser-host.sh
set -euo pipefail
cd "$(dirname "$0")"

echo "== OpenRA.sln (escritorio, OpenRaBrowserBuild=false) =="
dotnet build OpenRA.sln -c Debug -p:OpenRaBrowserBuild=false -v minimal

echo "== OpenRA.BrowserHost (WASM) =="
dotnet restore OpenRA.BrowserHost/OpenRA.BrowserHost.csproj -p:OpenRaBrowserBuild=true
dotnet build OpenRA.BrowserHost/OpenRA.BrowserHost.csproj -c Debug -p:OpenRaBrowserBuild=true --no-restore -v minimal

echo "== OpenRA.BrowserTunnelProxy (Release) =="
dotnet build tools/OpenRA.BrowserTunnelProxy/OpenRA.BrowserTunnelProxy.csproj -c Release -v minimal

echo "Listo. Terminales: ./run-dedicated-server.sh | ./run-browser-tunnel-proxy.sh | ./run-browser-host.sh"
echo "URL de join: ./print-browser-multiplayer-join-url.sh 'http://127.0.0.1:PUERTO/'"
