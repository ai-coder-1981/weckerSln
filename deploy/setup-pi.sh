#!/usr/bin/env bash
# Auf dem Raspberry Pi: sudo bash setup-pi.sh <Pfad-zum-publish-Ordner>
# Beim ersten Mal installiert das Skript alles, bei Updates tauscht es nur die Programme aus.
set -euo pipefail
[ "$EUID" -eq 0 ] || { echo "Bitte mit sudo starten."; exit 1; }
SRC="$(realpath "${1:?Pfad zum publish-Ordner angeben}")"

apt-get update
apt-get install -y mpg123 alsa-utils curl ca-certificates libicu-dev

if ! command -v dotnet >/dev/null; then
  curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --channel 8.0 --runtime aspnetcore --install-dir /usr/share/dotnet
  ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet
fi

id wecker >/dev/null 2>&1 || useradd --system --home-dir /var/lib/wecker --shell /usr/sbin/nologin wecker
usermod -aG audio wecker

# Der Dienst-User darf genau einen Befehl als root ausführen: den Rechner herunterfahren
echo 'wecker ALL=(root) NOPASSWD: /usr/bin/systemctl poweroff' > /etc/sudoers.d/wecker
chmod 440 /etc/sudoers.d/wecker
visudo -cf /etc/sudoers.d/wecker

systemctl stop wecker-web wecker-service 2>/dev/null || true
rm -rf /opt/wecker/service /opt/wecker/web
mkdir -p /opt/wecker
cp -r "$SRC/service" /opt/wecker/service
cp -r "$SRC/web" /opt/wecker/web

cp "$SRC/deploy/wecker-service.service" /etc/systemd/system/
cp "$SRC/deploy/wecker-web.service" /etc/systemd/system/
systemctl daemon-reload
systemctl enable --now wecker-service wecker-web

echo
echo "Fertig. MP3-Dateien nach /var/lib/wecker/music kopieren."
echo "Web-UI: http://$(hostname -I | awk '{print $1}'):5000"
