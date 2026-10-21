#!/usr/bin/env bash
# Auf dem Entwicklungsrechner ausführen (im Solution-Ordner).
# Raspberry Pi OS 64 Bit: linux-arm64 (Standard), 32 Bit: ./deploy/publish.sh linux-arm
set -euo pipefail
RID="${1:-linux-arm64}"
OUT="$(pwd)/publish"
rm -rf "$OUT"
dotnet publish src/Wecker.Service -c Release -r "$RID" --self-contained false -o "$OUT/service"
dotnet publish src/Wecker.Web     -c Release -r "$RID" --self-contained false -o "$OUT/web"
cp -r deploy "$OUT/deploy"
echo "Fertig: $OUT"
