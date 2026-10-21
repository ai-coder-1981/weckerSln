# Wecker.Contracts – Sinn und Verwendung

## Kurz gesagt

`Wecker.Contracts` ist die **gemeinsame Sprache** von Service und Web-UI.
Es ist eine winzige Klassenbibliothek ohne Logik und ohne NuGet-Pakete.
Sie enthält nur Datenformen, die zwischen den beiden Prozessen hin- und hergeschickt werden.

```
Wecker.Web ──────►  Wecker.Contracts  ◄────── Wecker.Service
(UI, kennt den        (Datenformen)         (Logik, kennt die
 Service nicht)                              UI nicht)
```

## Warum gibt es das?

Web und Service sind zwei getrennte Programme. Sie reden nur über JSON miteinander.
Ohne gemeinsames Projekt müsste man dieselbe Klasse zweimal schreiben:

| Ohne Contracts | Mit Contracts |
|---|---|
| `AlarmDto` in Service **und** in Web, getrennt gepflegt | Eine Klasse, beide Seiten nutzen sie |
| Feld im Service umbenannt, Web merkt es erst zur Laufzeit (Wecker speichert nichts mehr) | Der Compiler meldet den Fehler sofort in beiden Projekten |
| Event-Namen und Pfade als Strings in zwei Projekten | Konstanten in `ApiRoutes` |

Der Gewinn ist also **Typsicherheit über die Prozessgrenze hinweg**.

## Was steht drin

Alles in `src/Wecker.Contracts/Contracts.cs`:

| Typ | Zweck |
|---|---|
| `AlarmMode` | Enum: `Once` (0), `Daily` (1), `Weekdays` (2) |
| `AlarmDto` | Ein Wecker: `Id`, `Enabled`, `Time`, `Mode`, `Date`, `File` |
| `SaveResultDto` | Antwort auf „Wecker speichern“: der gespeicherte Wecker plus `Error` |
| `StatusDto` | Klingelstatus, nächster Wecker, nächstes Herunterfahren |
| `ShutdownDto` | Zeitplan für das Herunterfahren |
| `SaveShutdownResultDto` | Antwort auf „Zeitplan speichern“ |
| `ApiRoutes` | Hub-Pfad `/hub/status` und Event-Namen `StatusChanged`, `MusicChanged` |

**Das Muster `…ResultDto`:** Speichern liefert immer 200 zurück, auch bei ungültigen Eingaben.
Der Grund steht in `Error`, und der Wecker ist dann deaktiviert. Die UI zeigt die Meldung an und
übernimmt den tatsächlichen Zustand, ohne sich auf HTTP-Statuscodes zu verlassen.

## Regeln für das Projekt

1. **Keine Logik.** Nur Properties, Records, Enums und Konstanten. Keine Berechnungen, kein Zugriff auf Dateien.
2. **Keine Abhängigkeiten.** Keine NuGet-Pakete, keine Projektreferenzen. Nur das, was .NET von Haus aus mitbringt.
3. **Nur Daten, die über die Leitung gehen.** Interne Klassen des Service (`AudioPlayer`, `DataPaths`, `PlayerOptions`) gehören nicht hierher.
4. **Web und Service referenzieren Contracts, aber nie einander.** So kann jede Seite ausgetauscht werden, solange der Vertrag gleich bleibt.

## Wie es benutzt wird

**Service** nimmt die Typen entgegen und gibt sie zurück:

```csharp
api.MapGet("/alarms", (AlarmService s) => s.GetAlarms());          // liefert AlarmDto[]
api.MapPut("/alarms/{id:int}", async (int id, AlarmDto dto, AlarmService s) => ...);
```

**Web** liest dieselben Typen über den `AlarmApiClient`:

```csharp
var alarms = await http.GetFromJsonAsync<List<AlarmDto>>("api/alarms");
var result = await http.PutAsJsonAsync($"api/alarms/{alarm.Id}", alarm);
```

Beide Seiten serialisieren mit dem System.Text.Json-Standard. Deshalb ist das JSON automatisch identisch.

## Beispiel: neues Feld „Lautstärke“ ergänzen

1. **Contracts:** in `AlarmDto` ergänzen:
   ```csharp
   public int Volume { get; set; } = 80;
   ```
   Alte Dateien (`alarms.json`) ohne dieses Feld laden weiter, es gilt dann der Standardwert 80.
2. **Service:** in `AlarmService.SaveAsync` übernehmen (`alarm.Volume = dto.Volume;`) und im `AudioPlayer` verwenden.
3. **Web:** in `AlarmCard.razor` einen `MudSlider` an `Alarm.Volume` binden und beim Ändern `Persist()` aufrufen.
4. **Doku:** `docs/openapi.yaml` unter `AlarmDto` ergänzen.
5. **Beide Projekte neu bauen und veröffentlichen.** Siehe nächster Abschnitt.

## Kompatibilität (wichtig beim Update)

Die Web-UI und der Service werden getrennt installiert, es können also kurz verschiedene Versionen laufen.

- **Unkritisch:** neue Felder mit Standardwert hinzufügen. Die ältere Seite ignoriert sie.
- **Kritisch:** Felder umbenennen oder löschen, Typen ändern, Enum-Werte umnummerieren. Dann immer beide Seiten gemeinsam aktualisieren (`publish.sh` und `setup-pi.sh` tun das ohnehin).
- **Enums stehen als Zahl im JSON.** Neue Werte deshalb nur am Ende anhängen (`Once=0`, `Daily=1`, `Weekdays=2`). Die Reihenfolge nie ändern.

## Vereinfachung, die du kennen solltest

`AlarmDto` und `ShutdownDto` dienen gleichzeitig als **Übertragungsformat** (API) und als **Speicherformat** (`alarms.json`, `shutdown.json`).
Das hält die Anwendung klein. Wächst sie, könnte man ein eigenes internes Modell für die Speicherung einführen.
Dann bliebe die API in Contracts stabil, auch wenn sich das Dateiformat ändert.

## Contracts und OpenAPI

| | `Wecker.Contracts` | `openapi.yaml` |
|---|---|---|
| Für wen | .NET-Projekte (Service, Web) | alle anderen: curl, Python, Home Assistant, Tests |
| Wird geprüft von | dem Compiler | niemandem automatisch, von Hand pflegen |
| Wahrheit bei Konflikt | der Code | muss an den Code angepasst werden |

Ändert sich ein Typ in Contracts, muss `openapi.yaml` von Hand nachgezogen werden.
