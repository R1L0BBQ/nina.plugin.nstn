# NSTN für N.I.N.A.

Plugin für den Advanced Sequencer von N.I.N.A. 3.2: Die Bedingung
**„Loop while NSTN window open"** lässt einen Befehlsblock nur laufen, solange das
NSTN-Himmelsmodell das gewählte Band (L/RGB, Hα, O III, S II, Hβ) für das Ziel als
lohnend bewertet. Im Sequencer zeigt die Bedingung das Fenster der Nacht und den
aktuellen Zustand an.

Das Nachtmodell ist ein wörtlicher Port von FITSORAs `fitsora/domain/night.py`
(selbst ein Port des TypeScript-Belichtungsplaners) und wird gegen dieselben
Goldfixtures geprüft (`tests/Nstn.Core.Tests/golden/`).

## Aufbau

| Pfad | Inhalt |
|---|---|
| `src/Nstn.Core` | Nachtmodell + `WindowForecaster` (net8.0, keine NINA-/WPF-Abhängigkeit) |
| `tests/Nstn.Core.Tests` | Goldtests gegen `night_model.json`, Forecaster-Tests (xUnit, net10.0) |
| `src/NINA.Plugin.Nstn` | NINA-Plugin (net8.0-windows, WPF, `NINA.Plugin` 3.2.0.9001) |
| `build.ps1` | Tests + Build im Docker-SDK-Container, legt `dist\NINA.Plugin.Nstn-<Version>.zip` an |

## Bauen

Voraussetzung: Docker mit `mcr.microsoft.com/dotnet/sdk:10.0`. Kein lokales .NET-SDK nötig.

```powershell
.\build.ps1
```

Ergebnis: `dist\NSTN\` mit genau drei DLLs (`NINA.Plugin.Nstn.dll`, `Nstn.Core.dll`,
`astronomy.dll`) und das ZIP darum.

## Installieren (manuell)

1. N.I.N.A. beenden.
2. Den Ordner `NSTN` aus dem ZIP nach `%LOCALAPPDATA%\NINA\Plugins\3.0.0\` entpacken
   (ergibt `...\Plugins\3.0.0\NSTN\*.dll`).
3. N.I.N.A. starten, unter *Options → Plugins → NSTN* den **SQM-Wert** des Standorts
   eintragen. Breite/Länge kommen aus dem Profil (*Options → General → Astrometry*).

## Benutzen

Die Bedingung gehört an einen Befehlsblock **innerhalb eines Ziel-Containers** (sie
braucht dessen Koordinaten). Geprüft wird vor jedem Befehl, auch in verschachtelten
Blöcken; eine laufende Belichtung wird zu Ende geführt, nie abgebrochen.

Muster für mehrere Filter (Rundlauf, Gewichtung über die Anzahl je Block):

```
Ziel NGC 7000
  Anfahren & Zentrieren
  Schleife – bis Morgendämmerung
    Wait for NSTN window
    Block OIII – Loop while NSTN window open: O III 7 nm → 5 × 300 s OIII
    Block Hα   – Loop while NSTN window open: H-alpha 7 nm → 5 × 300 s Ha
    Block SII  – Loop while NSTN window open: S II 7 nm → 5 × 300 s SII
  Trigger: Autofokus, Meridian-Flip, Dithern
```

Ein Block, dessen Fenster zu ist, wird übersprungen. **„Wait for NSTN window" gehört an
den Anfang der Schleife:** Sind alle Fenster zu, würde N.I.N.A. 3.2 die Schleife sonst
ungebremst leer durchlaufen (eine Bremse von 250 ms je Durchlauf gibt es erst ab 3.3).
Der Befehl liest die Bänder aus den NSTN-Bedingungen der Blöcke darunter und wartet, bis
eines öffnet. Öffnet in dieser Nacht keines mehr, wartet er weiter, bis die
Schleifenbedingung (z. B. Morgendämmerung) die Schleife beendet.

Duo-Filter: Bedingung auf die **schwächere** Linie setzen (meist O III), sonst fällt
das Fenster zu großzügig aus.

![Sequencer mit NSTN-Bedingung und Warte-Befehl](docs/sequencer-0.1.1.jpg)

## Beispiel

`samples/M31-NSTN-Test.json` ist das Muster oben als Ziel-Container für M 31 (3 Durchläufe,
je Block höchstens 2 × 2 s). In N.I.N.A. über *Advanced Sequencer → Load sequence* laden;
N.I.N.A. packt einen einzelnen Ziel-Container selbst in eine neue Sequenz. Erzeugt wird die
Datei mit `python samples/make_sample.py` – wichtig für eigene Generatoren: jedes Element
braucht `"Parent": {"$ref": …}` auf seinen Container, sonst hat es nach dem Laden keinen
Elternteil und findet sein Ziel nicht.

## Getestet (17.11.2026, N.I.N.A. 3.2.0.9001, Simulator-Kamera)

| Prüfung | Ergebnis |
|---|---|
| Laden im Plugin-Manager, Optionsseite, Speichern im Profil | ok |
| Fensteranzeige gegen den Python-Port (M 31, 49,8° N, SQM 20,9) | identisch: Hα 18:48–03:44, O III 23:56–03:44, L/RGB ab 00:06 |
| Lauf mit Hα offen, O III und L/RGB zu | O III/L/RGB übersprungen, 6 Hα-Aufnahmen gespeichert |
| Alle Fenster zu, **ohne** Warte-Befehl | 1000 Leerdurchläufe in 0,56 s (N.I.N.A. 3.2 bremst nicht) |
| Alle Fenster zu, **mit** Warte-Befehl | wartet („O III opens 23:56"), 8 % eines Kerns (Leerlauf 2 %), Abbrechen ok |
| Anzeige-Refresh markiert die Sequenz nicht als geändert | ok |

0.1.1 behebt einen Befund aus diesem Test: 0.1.0 schrieb bei Leerdurchläufen je
Bedingung und Prüfung eine Protokollzeile (2000 Zeilen je 1000 Durchläufe); jetzt nur bei
einem echten Zustandswechsel. N.I.N.A. selbst schreibt weiter 4 Zeilen je übersprungenem
Block – ein Grund mehr für den Warte-Befehl.

## Bekannte Grenzen (Stand 0.1.1)

- Kein Nacht-Panel mit Zeitvorschau je Filter, keine Filter-Voreinstellungen aus den NSTN-CSVs.
- Uhrzeiten in der lokalen Zeit des Aufnahme-PCs.
- Getestet nur mit der Simulator-Kamera, ohne Montierung, Filterrad, Guiding und
  Meridian-Flip.
