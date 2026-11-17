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
| `src/Nstn.Core` | Nachtmodell, `WindowForecaster`, `PlanSimulator`, `FilterCatalog`, `FilterAssignments` (net8.0, keine NINA-/WPF-Abhängigkeit) |
| `tests/Nstn.Core.Tests` | Goldtests gegen `night_model.json`, Forecaster-, Simulator- und Katalog-Tests (xUnit, net10.0) |
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
  Schleife – Loop Until Time: Morgendämmerung
    Wait for NSTN window
    Block OIII – Bedingungen: NSTN O III 7 nm + Loop For Iterations 1 → Smart Exposure 5 × 300 s OIII
    Block Hα   – Bedingungen: NSTN H-alpha 7 nm + Loop For Iterations 1 → Smart Exposure 5 × 300 s Ha
    Block SII  – Bedingungen: NSTN S II 7 nm + Loop For Iterations 1 → Smart Exposure 5 × 300 s SII
  Trigger: Autofokus, Meridian-Flip, Dithern
```

**Bedingungen an einem Block sind Schleifenbedingungen.** Hat ein Block nur die
NSTN-Bedingung, wiederholt N.I.N.A. ihn, solange sein Fenster offen ist: Er behält das
Fenster für sich (strikter Vorrang). Erst „Loop For Iterations 1" am Block macht daraus
einen Rundlauf, bei dem jeder Block pro Schleifendurchlauf einmal drankommt.

Ein Block, dessen Fenster zu ist, wird übersprungen. **„Wait for NSTN window" gehört an
den Anfang der Schleife:** Sind alle Fenster zu, würde N.I.N.A. 3.2 die Schleife sonst
ungebremst leer durchlaufen (eine Bremse von 250 ms je Durchlauf gibt es erst ab 3.3).
Der Befehl liest die Bänder aus den NSTN-Bedingungen der Blöcke darunter und wartet, bis
eines öffnet. Öffnet in dieser Nacht keines mehr, wartet er weiter, bis die
Schleifenbedingung (z. B. Morgendämmerung) die Schleife beendet.

![Sequencer mit NSTN-Bedingung und Warte-Befehl](docs/sequencer-0.1.1.jpg)

## Filter-Voreinstellungen (ab 0.3.0)

Im Feld **Filter** der Bedingung stehen die Duo-Filter und Schmalband-Sätze aus den
NSTN-Tabellen (`src/Nstn.Core/Filters/*.csv`, unverändert aus `astro-public`). Mit einem
Filter zeigt **Band** nur dessen Linien, die Bandbreite kommt aus der Tabelle und ist
gesperrt; „Custom" erlaubt beides von Hand.

- Ein **Duo-Filter** startet auf seiner schwächeren Linie – der mit dem meisten
  Streulicht im Band (Bandbreite × Rayleigh-Gewicht), meist O III. Wählt man die
  robustere Linie, sagt ein Hinweis, dass der andere Kanal dann schon überstrahlt ist.
  Bei Farbkameras ist das eine legitime Wahl: zwei Blöcke, „solange O III offen" und
  „solange Hα offen", sammeln Hα länger (`samples/M31-NSTN-Duo.json`).
- Gespeichert werden Linie, Bandbreite **und** Filtername; Linie und Bandbreite bleiben
  maßgeblich. Beim Laden wird die gespeicherte Linie nie durch die Voreinstellung ersetzt,
  unabhängig von der Reihenfolge der Felder in der Datei.
- Die Tabellen sind ein Vorschlag: Askar verkauft einen D1 mit 6/6 nm und einen
  „Super D1" mit 8,5/6,5 nm. Nahe der Schwelle verschiebt 1 nm das Fenster deutlich
  (M 31 am 17.11.: O III 6 nm ab 22:18, 7 nm ab 23:56).

## Filter des Profils (ab 0.4.0)

Unter *Options → Plugins → NSTN → Filters of this profile* steht jeder Filter der
Filterradliste des Profils mit dem, was er durchlässt: „not assigned", „Broadband",
„Custom" (Linie + Bandbreite von Hand) oder ein Filter aus den NSTN-Tabellen. Gespeichert
wird je Profil.

- **Vorschläge** erscheinen neben jeder Zeile und werden nur per Knopf übernommen.
  Erkannt werden bekannte Produkte (Askar D1/D2/C1/C2, Optolong L-eNhance/L-eXtreme/
  L-Ultimate/L-Para/L-Synergy, SV220 mit Bandbreite), Breitbandnamen (L, R, G, B, Lum,
  kein_Filter, NoFilter, UV/IR) und einzelne Linien **mit** Bandbreite im Namen
  („Ha 3nm"). Keine Bandbreite wird erfunden: „Ha" allein, „Super D1" oder „ALP-T" bleiben
  ohne Vorschlag.
- In der Bedingung macht **Filter → „From this block's filter"** daraus die Quelle: Die
  Bedingung liest den Filter aus dem Switch Filter bzw. der Smart Exposure ihres Blocks
  und nimmt Band und Bandbreite aus der Zuordnung. Ändert man den Filter in der Smart
  Exposure, folgt die NSTN-Bedingung. Fehlt der Filter, nutzt der Block mehrere Filter
  oder ist der Filter nicht zugeordnet, meldet die Bedingung das als Problem.
- Für die Farbkamera mit Duo-Filter und zwei Blöcken (O III und Hα mit demselben Filter)
  passt diese Quelle nicht – dort den Filter direkt in der Bedingung wählen.

`samples/M31-NSTN-Block.json` braucht die Filter „Askar D1", „Ha" und „L" im Profil.

## Nachtplan (ab 0.2.0)

„Wait for NSTN window" zeigt unter sich, was die gebaute Struktur heute Nacht ab jetzt
voraussichtlich bringt: je Block das Fenster (blass), die Zeiten, in denen er belichten
würde (kräftig), Integrationszeit und Anzahl, dazu eine Zusammenfassung je Band und wann
und warum der Plan endet. Hinweise erscheinen darunter, etwa wenn ein Block ohne
Iterationsgrenze das Fenster für sich behält oder die äußere Schleife keine Bedingung hat
und deshalb nur einmal läuft.

![Nachtplan unter „Wait for NSTN window"](docs/night-plan-0.2.0.jpg)

Gerechnet wird nach den Regeln des Sequencers (Prüfung vor jeder Aufnahme, begonnene
Aufnahmen laufen zu Ende, Blockgrenze aus „Loop For Iterations", Ende aus „Loop For
Iterations" bzw. „Loop Until Time" der äußeren Schleife). Verschachtelte Reihen wie Smart
Exposure werden aufgelöst. Je Aufnahme kommt ein Zuschlag dazu (*Options → Plugins → NSTN*,
Standard 10 s); Autofokus, Meridian-Flip, Filterwechsel und Schwenks sind nicht enthalten.
Der Plan aktualisiert sich alle 20 s und per „Recalculate".

## Beispiele

`samples/M31-NSTN-Test.json`: kurzer Testlauf für M 31 (3 Durchläufe, je Block höchstens
2 × 2 s). `samples/M31-NSTN-Plan.json`: eine Nacht im Rundlauf (O III und Hα je 5 × 300 s
pro Durchlauf, L/RGB absichtlich ohne Grenze) für den Nachtplan.
`samples/M31-NSTN-Duo.json`: Askar D1 an einer Farbkamera, je ein Block für O III und Hα. In N.I.N.A. über
*Advanced Sequencer → Load sequence* laden; N.I.N.A. packt einen einzelnen Ziel-Container
selbst in eine neue Sequenz. Erzeugt werden die Dateien mit
`python samples/make_sample.py [full|plan|duo|block]` – wichtig für eigene Generatoren: jedes Element
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
| Nachtplan 0.2.0 (M31-NSTN-Plan.json ab 23:41) | O III 10 × 300 s, Hα 14 × 300 s, L/RGB 57 × 120 s, Ende 03:48; Vorrang-Hinweis bei L/RGB |
| Nachtplan nach Änderung der äußeren Schleife (1000 → 2) | per „Recalculate" und automatisch nach ≤ 20 s: Ende 03:02, „loop iterations used up" |
| Voreinstellungen 0.3.0 laden (M31-NSTN-Duo.json, beide Feldreihenfolgen) | Askar D1, O III bzw. Hα wie gespeichert, 6 nm gesperrt; Hinweis beim Hα-Block; Fenster = Python-Port (O III 6 nm 22:18–04:30, Hα 6 nm 18:36–04:30) |
| Filterwechsel in der Oberfläche (→ L-eXtreme 7 nm) | Band springt auf O III, 7 nm, Hinweis weg, Fenster 23:56–03:44 |
| Filter des Profils 0.4.0 (Askar D1, Ha, L angelegt) | Zeilen erscheinen, Vorschläge D1 → O III 6 nm und L → Breitband, „Ha" ohne Vorschlag; per Knopf übernommen, Ha von Hand Custom 7 nm; im Profil gespeichert |
| M31-NSTN-Block.json per `--sequencefile` | lädt ohne Fehler; **Anzeige im Block-Modus nicht angesehen** – ab hier kamen in N.I.N.A. keine Klicks der Computersteuerung mehr an |

0.1.1 behebt einen Befund aus diesem Test: 0.1.0 schrieb bei Leerdurchläufen je
Bedingung und Prüfung eine Protokollzeile (2000 Zeilen je 1000 Durchläufe); jetzt nur bei
einem echten Zustandswechsel. N.I.N.A. selbst schreibt weiter 4 Zeilen je übersprungenem
Block – ein Grund mehr für den Warte-Befehl.

## Bekannte Grenzen (Stand 0.4.0)

- „From this block's filter" ist in N.I.N.A. noch nicht in der Anzeige geprüft (Laden ja).
- Umbenennen eines Filters im Profil wird erst nach „Reload filter list" oder erneutem
  Öffnen sichtbar; neue/gelöschte Filter sofort.
- Nachtplan: nur die Blöcke direkt unter dem Warte-Befehl; andere Schleifenbedingungen als
  „Loop For Iterations"/„Loop Until Time" werden genannt, aber nicht gerechnet; nur die
  laufende Nacht.
- Uhrzeiten in der lokalen Zeit des Aufnahme-PCs.
- Getestet nur mit der Simulator-Kamera, ohne Montierung, Filterrad, Guiding und
  Meridian-Flip.
