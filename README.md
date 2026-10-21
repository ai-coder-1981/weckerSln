# Wecker

- src/Wecker.Contracts – gemeinsame DTOs
- src/Wecker.Service   – Quartz, Persistenz, Audio (mpg123), REST + SignalR, läuft als systemd-Dienst
- src/Wecker.Web       – Blazor + MudBlazor, nur UI, spricht per HTTP/SignalR mit dem Service
- deploy/              – systemd-Units, publish.sh (PC), setup-pi.sh (Raspberry)

Lokal testen (zwei Terminals):
    dotnet run --project src/Wecker.Service
    dotnet run --project src/Wecker.Web
MP3s lokal nach src/Wecker.Service/data/music legen (mpg123 muss installiert sein, sonst zeigt der Service einen Fehler).

Herunterfahren: setup-pi.sh legt /etc/sudoers.d/wecker an (nur 'systemctl poweroff' für den User wecker).
Unter Windows/macOS wird das Herunterfahren nur geloggt, nie ausgeführt.

Projekt bauen und auf dem raspberry deployen:

```bash
./deploy/publish.sh
scp -r publish pi@raspberrypi:~/wecker-publish
ssh pi@raspberrypi
sudo bash ~/wecker-publish/deploy/setup-pi.sh ~/wecker-publish
```

Befehle zum debuggen auf dem Ziel:

```bash
sudo systemctl restart wecker-service
systemctl status wecker-service
systemctl show wecker-service -p Environment
```

Die richtigen Audio-Ausgabe für den Service einstellen:

```bash
sudo mkdir -p /etc/systemd/system/wecker-service.service.d
printf '[Service]\nEnvironment=Player__Device=plughw:CARD=Headphones,DEV=0\n' | sudo tee /etc/systemd/system/wecker-service.service.d/override.conf
```