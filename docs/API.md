# API-Kurzreferenz

Maßgeblich ist die maschinenlesbare Beschreibung [`openapi.yaml`](openapi.yaml) (OpenAPI 3.0).
Ansehen: Inhalt auf <https://editor.swagger.io> einfügen, oder in VS Code die Erweiterung „OpenAPI (Swagger) Editor“ nutzen.

- Basis-URL: `http://127.0.0.1:5001` (der Service hört nur lokal auf dem Raspberry)
- Format: JSON (camelCase), Enums als Zahl, Zeiten als `"hh:mm:ss"`
- Fehler: HTTP 400/409 mit einer Fehlermeldung als JSON-String
- Keine Authentifizierung

| Methode | Pfad | Zweck | Erfolg |
|---|---|---|---|
| GET | `/api/alarms` | Alle 3 Wecker | 200 `AlarmDto[]` |
| PUT | `/api/alarms/{id}` | Wecker speichern und einplanen | 200 `SaveResultDto`, 404 |
| POST | `/api/alarms/{id}/test` | Datei probehören | 204, 400 |
| GET | `/api/music` | MP3-Liste | 200 `string[]` |
| POST | `/api/music` | MP3 hochladen (Feld `file`) | 200 Dateiname, 400 |
| DELETE | `/api/music/{file}` | MP3 löschen | 204, 409 |
| GET | `/api/status` | Klingelt etwas? Nächste Zeiten | 200 `StatusDto` |
| POST | `/api/stop` | Klingeln beenden | 204 |
| GET | `/api/shutdown` | Shutdown-Zeitplan lesen | 200 `ShutdownDto` |
| PUT | `/api/shutdown` | Zeitplan speichern | 200 `SaveShutdownResultDto` |
| POST | `/api/shutdown/now` | Jetzt herunterfahren | 204 |

`AlarmMode`: `0` Einmalig (Datum nötig), `1` Täglich, `2` Werktags (Mo–Fr).

## Beispiele (curl, direkt auf dem Raspberry)

```bash
# Wecker lesen
curl -s http://127.0.0.1:5001/api/alarms

# Wecker 1: werktags 06:45 mit morgen.mp3
curl -s -X PUT http://127.0.0.1:5001/api/alarms/1 \
  -H "Content-Type: application/json" \
  -d '{"id":1,"enabled":true,"time":"06:45:00","mode":2,"date":null,"file":"morgen.mp3"}'

# Wecker 3: einmalig am 25.10.2026 um 09:30
curl -s -X PUT http://127.0.0.1:5001/api/alarms/3 \
  -H "Content-Type: application/json" \
  -d '{"id":3,"enabled":true,"time":"09:30:00","mode":0,"date":"2026-10-25T00:00:00","file":"sonntag.mp3"}'

# Wecker 1 ausschalten
curl -s -X PUT http://127.0.0.1:5001/api/alarms/1 \
  -H "Content-Type: application/json" \
  -d '{"id":1,"enabled":false,"time":"06:45:00","mode":2,"date":null,"file":"morgen.mp3"}'

# MP3 hochladen / auflisten / löschen
curl -s -F "file=@/home/pi/morgen.mp3" http://127.0.0.1:5001/api/music
curl -s http://127.0.0.1:5001/api/music
curl -s -X DELETE "http://127.0.0.1:5001/api/music/morgen.mp3"

# Probehören, Status, Stopp
curl -s -X POST http://127.0.0.1:5001/api/alarms/1/test
curl -s http://127.0.0.1:5001/api/status
curl -s -X POST http://127.0.0.1:5001/api/stop

# Täglich um 22:00 herunterfahren
curl -s -X PUT http://127.0.0.1:5001/api/shutdown \
  -H "Content-Type: application/json" \
  -d '{"enabled":true,"time":"22:00:00","mode":1,"date":null}'
```

Beispielantwort von `PUT /api/alarms/1` bei fehlender Datei (gespeichert, aber deaktiviert):

```json
{
  "alarm": { "id": 1, "enabled": false, "time": "06:45:00", "mode": 2, "date": null, "file": null },
  "error": "Bitte eine vorhandene Musikdatei wählen."
}
```

## Live-Updates (SignalR)

OpenAPI kann Server-Push nicht beschreiben, deshalb hier separat.

- Hub-URL: `/hub/status`
- Server sendet an alle verbundenen Clients:

| Event | Argument | Wann |
|---|---|---|
| `StatusChanged` | `StatusDto` | Wecker gespeichert, Klingeln beginnt/endet, Shutdown-Zeitplan geändert |
| `MusicChanged` | – | MP3 hochgeladen oder gelöscht |

- Clients senden nichts an den Hub. Alle Änderungen laufen über die REST-Endpunkte oben.
- Namen der Events stehen als Konstanten in `Wecker.Contracts.ApiRoutes`.
