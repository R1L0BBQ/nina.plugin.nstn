# Sequenz-Generator für N.I.N.A.: Recherche und Bewertung

Stand 18.11.2026 · Bezug NSTN 0.6.0, N.I.N.A. 3.2.0.9001 · reine Recherche, nichts gebaut.

**Vorgehen:** Websuche, Abruf der Seiten, AstroBin und Cloudy Nights im eingebauten Browser gelesen,
ohne Anmeldung. AstroBin zeigt Beiträge maschinell ins Deutsche übersetzt, Zitate von dort sind
deshalb sinngemäß. Reddit war nicht erreichbar, weil Suchdienst und Browser die Domain sperren.
Discord wurde nicht durchsucht. NINA-Verhalten ist am Quelltext belegt: Klon
`C:\Claude\_scratch\nina-src-3.2`, Tag `Version-3.2`, Pfade unten relativ dazu.
Häufigkeit in Abschnitt 2 = Zahl unabhängiger Fundstellen in dieser Recherche. Das ist keine
Statistik.

## 1. Gefundene Werkzeuge

### 1a. Erzeugen eine ladbare Sequenz oder Vorlage

| Name | URL | Art | Erzeugt | Mono | NINA-Version | Letzte Aktivität | Lizenz | Lücken |
|---|---|---|---|---|---|---|---|---|
| NINA „Legacy"-Sequencer + „Build advanced sequence" | in NINA; [Doku](https://nighttime-imaging.eu/docs/master/site/sequencer/advanced/simpletoadvanced) | eingebaut, Formular | **ganze Sequenz** (Start, je Ziel ein DSO-Container, Ende), Umwandlung nur in eine Richtung (`NINA.Core/Locale/Locale.resx:5048`) | ja: je Zeile Filter, Zeit, Anzahl, Dither; Modus *Rotate* (eine Schleife über alle Zeilen, `NINA/ViewModel/Sequencer/SimpleSequence/SimpleDSOContainer.cs:302-327`); AF nach Filterwechsel als Trigger (`…:123`) | 3.2 (Quelltext) | Teil von NINA | MPL-2.0 (`LICENSE.txt`) | Ende nur über Iterationszahl (einzige Bedingung `LoopCondition`, `…:704-707`); die Doku ergänzt „Loop Until Time" und Dither je Runde von Hand; keine Flats, keine Mond-/Dämmerungslogik je Filter |
| El Nina Wizard | [el-nina.com](https://el-nina.com/) | Web-App, läuft nur im Browser | **ganze Sequenz** als JSON (Standard: 105 Anweisungen, 87 KB); 6 Schritte (Ansatz, Geräte, Sicherheit, Aufnahme, Automatik, Prüfung) | nur indirekt: Filterrad-Option, „AF after filter change"; Belichtungsplan kommt aus Target Scheduler | Auswahl 3.2.0.9001 oder 3.3.0.1049 nightly #49 | aktuell (3.3-Nightly in der Auswahl), kein Datum | keine Angabe, kein Autor, kein Impressum im Seitenquelltext | Modus ohne Plugin ist „Coming soon"; heute Pflicht: **Target Scheduler + Sequencer+**; keine eigene Filter-/Schmalbandlogik (gelesen am 18.11.2026) |
| PatriotAstro-Vorlagen | [patriotastro.com](https://patriotastro.com/nina-advanced-sequence-downloads/) | Vorlagensammlung (ZIP „v1") | `*.template.json`: 8 Mono-Varianten (LRGB, SHO, je ± Fokus-Offsets ± Rotation), OSC, Start, Ende | ja | keine Angabe | Seite „updated", © 2026; ein Nutzer im Aug. 2025: „getting a bit older now" ([CN](https://www.cloudynights.com/forums/topic/973964-nina-advanced-sequencer-newbie-question/)) | keine Angabe | Autor nennt Hardware-Annahmen in den Vorlagen; Anpassung von Hand; Logik nur einzeilig beschrieben. In Foren **am häufigsten empfohlen** ([AstroBin](https://app.astrobin.com/forum/topic/179656/stefan-berg-nighttime-imaging-n-astronomy-nina-nina/sequences-for-lrgb), CN) |
| Brian Weber, Mono-Vorlagen | [Blog](https://blog.briangweber.com/nina-mono/) | Vorlagen (Google Drive) | Vorlagen: Start, Ziel LRGB/SHO, Flats, Ende | ja: Filter je Runde, Dither nach jeder Runde, Fokus-Offsets, AF nach HFR | keine Angabe | 17.06.2025 | „All rights reserved" | braucht Sequencer Powerups (Variablen), Three Point Polar Alignment, Filter Offset Calculator; Flat-Vorlage setzt bekannte Belichtungszeit je Filter voraus |
| ac4lt/autonomous-nina-sequence | [GitHub](https://github.com/ac4lt/autonomous-nina-sequence) | Sequenz-Sammlung | **ganze Sequenz** (mehrere JSON) für 24/7-Sternwarte | Filterrad ja, Ziele und Filter über Target Scheduler | 3.2 | Release 1.2.0 vom 22.02. (Jahr nicht angezeigt) | MPL-2.0 | setzt Target Scheduler 5.8.2, Powerups, Ground Station, Wetter-/Sicherheitsmonitor voraus; Zielgruppe Sternwarte |
| phtnnz/astro, `nina-create-sequence2` | [GitHub](https://github.com/phtnnz/astro) | Python-Kommandozeile | **ganze Sequenz** aus Basis- und Zielvorlage + CSV-Zielliste | nicht beschrieben, steckt in den Vorlagen | Vorlagen mit 3.2 getestet | Release „v2,1" vom 30.06. (Jahr nicht angezeigt) | Apache-2.0 | für NEO-Beobachtung an der IAS-Fernsternwarte; kein Einsteigerwerkzeug. Technisch das nächste Gegenstück zu `make_sample.py` |
| KI auf Sequenz-JSON | [AstroBin, 24.03.2026](https://app.astrobin.com/forum/topic/226417/dont-underestimate-what-claude-can-do-with-your-nina-advanced-sequencer-json) | Arbeitsweise | ganze Mehrziel-Sequenz neu geschrieben | ja (SHO, LRGB) | – | 62 Antworten | – | keine Prüfung, nicht reproduzierbar; selbst der Autor schreibt, man müsse die Ausgabe prüfen |

### 1b. Plugins, die zur Laufzeit planen

| Name | URL | Art | Erzeugt | Mono | NINA-Version | Letzte Aktivität | Lizenz | Lücken |
|---|---|---|---|---|---|---|---|---|
| Target Scheduler | [Doku](https://tcpalmer.github.io/nina-scheduler/) | Plugin + Datenbank | keine Datei: **ein Container** wählt Ziel und Filter je Durchgang | ja: Belichtungsvorlage je Filter (Name muss zum Filterrad passen), Filterwechsel-Frequenz, Dither, Mond und Dämmerung je Filter, Flats-Befehle ([Vorlagen](https://tcpalmer.github.io/nina-scheduler/target-management/exposure-templates.html), [Projekte](https://tcpalmer.github.io/nina-scheduler/target-management/projects.html)) | 5.10+ nur NINA 3.3 ([Release Notes](https://tcpalmer.github.io/nina-scheduler/release.html)); für 3.2 ältere 5.x | 5.11.0.0 vom 05.11.2026 | MPL-2.0 | Start, Ende und Trigger baut der Nutzer selbst; Doku setzt voraus, dass man den Advanced Sequencer sicher bedient ([Getting Started](https://tcpalmer.github.io/nina-scheduler/getting-started.html)); Vorschau ≠ Lauf ([Preview](https://tcpalmer.github.io/nina-scheduler/scheduler-preview.html)). Laut Cloudy Nights die „Easy Button" für Fortgeschrittene ([CN](https://cloudynights.com/topic/974326-learning-ninas-advanced-sequencer)) |
| DynamicSequencer | [GitHub](https://github.com/DanielHeEGG/DynamicSequencer) | Plugin | keine Sequenz; Projekt-JSON von Hand | ja (Filtername muss passen) | 3.0.0.3005-rc | v3.5.1.0 vom 09.07. (Jahr nicht angezeigt) | MPL-2.0 | keine Oberfläche, keine Prüfung der Projekt-JSON, „early stages" |

### 1c. Planer mit Export: nur Ziele, keine ladbare Advanced-Sequenz

| Name | URL | Art | Erzeugt | Mono | NINA-Version | Letzte Aktivität | Lizenz | Lücken |
|---|---|---|---|---|---|---|---|---|
| Telescopius | [telescopius.com](https://telescopius.com/) | Web-Planer | CSV-Zielliste; NINA „Import target" ([Issue #1028](https://bitbucket.org/Isbeorn/nina/issues/1028/import-of-telescopius-csv-files-does-not)) und Target Scheduler ([Targets](https://tcpalmer.github.io/nina-scheduler/target-management/targets.html)) lesen sie | nein | – | – | proprietär | nur Name, Koordinaten; Rotation fehlte anfangs |
| Lightbucket Astro Planner | [GitHub](https://github.com/LightbucketAstro/lightbucket-astro-planner) | Desktop-App | `.ninaTargetSet` (Simple-Sequencer-Zielsatz): Koordinaten, Rotation, Filternamen | Filternamen ja; Belichtungszeit/Anzahl laut README nicht im Export (Annahme aus der Exportbeschreibung) | keine Angabe | v2.4.0 vom 04.11. (Jahr nicht angezeigt) | MIT | keine Advanced-Sequenz, keine Trigger |
| The Quiet Sky | [quietsky.co.uk](https://quietsky.co.uk/faq) | Web-App | Zielliste („in beta") | Filterzeiten im Planer, Export nicht beschrieben | – | – | kostenlos, Konto für Planer | nur Ziele |
| StellarSkyImager | [stellarskyimager.fr](https://www.stellarskyimager.fr/planner) | Web-Planer | „N.I.N.A / ASIAIR / APT exports", Format nicht dokumentiert | unklar | – | – | Konto vorhanden | Format unbekannt |
| Deep-Sky Planner | [Hilfe](https://www.knightware.biz/community/public/dsp9/WebHelp/topics/idh-exportapp.htm) | Desktop | Zielliste für „Import Target" im Simple Sequencer | nein | – | – | proprietär | nur Ziele (J2000) |
| Stellarium | – | Planetarium | kein Export; NINA holt Koordinaten einzelner Ziele über die Planetariumsanbindung (`NINA.Equipment/Equipment/MyPlanetarium/Stellarium.cs`) | nein | – | – | GPL | nur Koordinaten |

### 1d. Eigene Basis

| Name | URL | Art | Erzeugt | Mono | NINA-Version | Letzte Aktivität | Lizenz | Lücken |
|---|---|---|---|---|---|---|---|---|
| `samples/make_sample.py` | Repo `nina-nstn` | Python-Skript | **Ziel-Container** mit Rundlauf, Warte-Befehl, NSTN-Bedingungen, `Parent`-`$ref`; per `--sequencefile` in NINA geladen (`knowledge/tools/nina-nstn.md`) | `SwitchFilter` nach Namen, `@block` | 3.2.0.9001 | 18.11.2026 | offen | keine Oberfläche; keine Trigger, Smart Exposure, Flats, Start/Ende |

**Ideenquellen in anderer Software** (nur angelesen):
- **SGP** hat Ereignislisten je Ziel mit „rotate through events". Seine Foren kennen dieselben Fragen: AF bei jedem Filterwechsel oder Offsets ([SGP-Forum](https://forum.sequencegeneratorpro.com/t/how-to-rotate-through-events-while-minimizing-af-runs/18219)), AF auch bei gleichem Filter ([SGP-Forum](https://forum.sequencegeneratorpro.com/t/autofocus-kicks-in-for-the-same-filter-in-a-sequence/11397)).
- **ASIAIR Plan** arbeitet mit Belichtungsgruppen je Filter. Nutzer diskutieren AF bei Filterwechsel gegen EFW-Offsets und halten Dither nach jedem Filterwechsel für unnötig ([ZWO-Forum](https://bbs.zwoastro.com/d/13589/38)).
- **NINA.Polaris**, ein Fork von NINA, hat einen ASIAIR-artigen Mehrziel-„PLAN" ([GitHub](https://github.com/DanWBR/NINA.Polaris)).
- **Ekos-Scheduler, Voyager/RoboTarget:** keine verwertbaren Treffer, nicht vertieft.

Gemeinsames Muster: Die Einsteigerwerkzeuge zeigen **Gruppen je Filter mit Anzahl und Rundlauf ja/nein**, keine Bäume.

## 2. Stolperstellen, nach Häufigkeit

1. **Welcher Block wiederholt sich?** Schleifenumfang und Bedingungen, 6 Fundstellen.
   - Loop Until Time am Ziel-Container: Unpark, Home, AF und Slew laufen jede Runde mit. Die Antwort im Thread: Eine Schleifenbedingung wiederholt den ganzen Container, zu dem sie gehört ([AstroBin, ca. 11.11.2026](https://app.astrobin.com/forum/topic/264434/stefan-berg-nighttime-imaging-n-astronomy-nina-nina/looping-problem-in-nina)).
   - Ein Umsteiger auf Mono baut je Filter eine eigene Schleife mit 1 Aufnahme. Er glaubte, Trigger griffen erst nach dem Ende einer Smart Exposure ([AstroBin, 06/2025](https://app.astrobin.com/forum/topic/179656/stefan-berg-nighttime-imaging-n-astronomy-nina-nina/sequences-for-lrgb)).
   - Der Unterschied zwischen Start-, Ziel- und Endbereich ist unklar ([CN, 08/2025](https://www.cloudynights.com/forums/topic/973964-nina-advanced-sequencer-newbie-question/)).
   - Doku und Blog: Ohne Bedingung läuft ein Container genau einmal ([NINA-Doku](https://nighttime-imaging.eu/docs/master/site/sequencer/advanced/conditions), [Blog 07/2026](https://www.backyardastronomy.net/2026/07/19/advanced-nina-sequencer-conditions-loops-and-smart-logic/)).
   - Eigener Befund: Ohne „Loop For Iterations" behält ein NSTN-Block das Fenster für sich, statt reihum zu laufen (`knowledge/tools/nina-nstn.md`).
2. **Filterwechsel und Fokus:** Rundlauf nur mit Offsets, sonst AF bei jedem Wechsel. 5 Fundstellen.
   - Häufiger Filterwechsel ist ohne parfokale Filter problematisch. Abhilfe sind Offsets per Filter Offset Calculator ([Weber](https://blog.briangweber.com/nina-mono/)).
   - Target Scheduler empfiehlt Filterwechsel-Frequenz 0 ohne Offsets ([Projekte](https://tcpalmer.github.io/nina-scheduler/target-management/projects.html)).
   - NINA-Doku: Mit Offsets kann „AF After Filter Change" entfallen ([Doku](https://nighttime-imaging.eu/docs/master/site/sequencer/advanced/simpletoadvanced)).
   - Issues: Der Trigger feuerte zu oft (#1247, behoben); Offsets und AF lassen sich nicht je Filter mischen (#1316) ([#1247](https://bitbucket.org/Isbeorn/nina/issues/1247/af-after-filter-change-trigger-fires-too), [#1316](https://bitbucket.org/Isbeorn/nina/issues/1316/mix-focus-offets-with-normal-auto)).
   - Quelltext: Der Trigger feuert, sobald der aktuelle Filter vom Filter des letzten AF abweicht (`NINA.Sequencer/Trigger/Autofocus/AutofocusAfterFilterChange.cs:109-128`). Ein LRGB-Rundlauf ohne Offsets fokussiert also bei jedem Wechsel.
3. **Dithering und Nachführung beim Filterwechsel**, 4 Fundstellen.
   - Dither einmal pro Filterrunde, in der Smart Exposure Dither 0 ([AstroBin 179656](https://app.astrobin.com/forum/topic/179656/stefan-berg-nighttime-imaging-n-astronomy-nina-nina/sequences-for-lrgb), [Weber](https://blog.briangweber.com/nina-mono/), [NINA-Doku](https://nighttime-imaging.eu/docs/master/site/sequencer/advanced/simpletoadvanced)).
   - Mit OAG verschiebt der Offset-Fokusweg den Leitstern. Dann gilt: Guiding anhalten und neu starten, oder ein Settle-Trigger (PHD2 Tools) ([AstroBin 179656](https://app.astrobin.com/forum/topic/179656/stefan-berg-nighttime-imaging-n-astronomy-nina-nina/sequences-for-lrgb)).
   - Target Scheduler zählt den Dither je Filtername ([Vorlagen](https://tcpalmer.github.io/nina-scheduler/target-management/exposure-templates.html)).
4. **Trigger-Platzierung und Meridian-Flip**, 3 Fundstellen.
   - Smart Exposures lagen außerhalb des Anweisungsbereichs: keine AF-Trigger, kein Flip ([AstroBin, 12/2025](https://app.astrobin.com/forum/topic/207277/stefan-berg-nighttime-imaging-n-astronomy-nina-nina/nina-failing-to-run-triggers-etc)).
   - Trigger wirken auch in verschachtelten Schleifen ([AstroBin 179656](https://app.astrobin.com/forum/topic/179656/stefan-berg-nighttime-imaging-n-astronomy-nina-nina/sequences-for-lrgb)). Target Scheduler legt Flip, AF und Center after Drift **über** den Container ([Container](https://tcpalmer.github.io/nina-scheduler/sequencer/container.html)).
   - Grundsatz: Trigger laufen nur zwischen zwei Befehlen (belegter NINA-Fakt, `knowledge/tools/nina-nstn.md`).
5. **Ende der Nacht und Tageswechsel bei Zeitbedingungen**, 4 Fundstellen.
   - Doku: Feste Zeiten wechseln mittags auf den nächsten Tag, Dämmerungszeiten „auf der Gegenseite" ([Doku](https://nighttime-imaging.eu/docs/master/site/sequencer/advanced/conditions)).
   - Praxis 3.x: Der Wechsel liegt bei Sonnenaufgang ([#1299](https://bitbucket.org/Isbeorn/nina/issues/1299/regression-in-30-wait-until-time-sunrise)). Ein Warte-Befehl auf 7 Uhr wird seit Ende Februar übersprungen, Nutzer widersprechen sich ([AstroBin, 03/2026](https://app.astrobin.com/forum/topic/223678/nina-software-manual-time-rollover-noon-versus-sunrise-behavior)).
   - Ein Blog nennt −15° „astronomische Dämmerung". Fachlich sind es −18° ([Blog](https://www.backyardastronomy.net/2026/07/19/advanced-nina-sequencer-conditions-loops-and-smart-logic/)).
   - Zeit- und Sicherheitsbedingungen brechen laufende Belichtungen ab (`NINA.Sequencer/Conditions/TimeCondition.cs:65`), `LoopCondition` nicht.
6. **Flats am Ende**, 3 Fundstellen.
   - Umsteiger auf Mono sucht Beispiele mit automatischen Flats ([AstroBin, 01/2026](https://app.astrobin.com/forum/topic/211105/acquisition/nina-32-looking-for-lrgb-sho-w-flats-advanced-sequencer-examples)).
   - Flat-Vorlagen brauchen eine bekannte Belichtungszeit je Filter ([Weber](https://blog.briangweber.com/nina-mono/)).
   - Rotator-Wiederholbarkeit und Paneelverhalten ([TS Flats](https://tcpalmer.github.io/nina-scheduler/sequencer/flats.html), [#859](https://bitbucket.org/Isbeorn/nina/issues/859/flat-panel-is-turning-off-for-trained-flat)).
7. **Vorlagen-Mechanik und Weitergabe**, 3 Fundstellen.
   - Eine eingefügte Vorlage ist eine einmalige Kopie; „Template by Reference" (Powerups) hilft ([CN, 08/2025](https://cloudynights.com/topic/974326-learning-ninas-advanced-sequencer)).
   - Gespeicherte Sequenz ≠ Vorlage ([#1050](https://bitbucket.org/Isbeorn/nina/issues/1050/save-and-restore-of-complete-sequence-to)).
   - Fremde Vorlagen bringen Plugins und Hardware-Annahmen mit (Weber, PatriotAstro).

Grundstimmung: Mono macht Sequenzen „viel komplizierter" (AstroBin 179656). Der Advanced Sequencer mache vielen Leuten Angst (AstroBin 226417). Seine Stärke liege gerade bei Mono mit Filter- und Fokuswechseln (CN 974326).

## 3. Einschätzung

> Die Abschnitte 3.2–3.4 und 3.7 sind durch Abschnitt 4 überholt (Rückmeldung von Marc, 18.11.2026).
> Sie bleiben als Herleitung stehen.

### 3.1 Lage

- **Allgemeine Mono-Sequenzen sind abgedeckt:** durch NINAs eigenen Legacy-Sequencer mit Rotate-Modus und Umwandlung, durch die PatriotAstro-Vorlagen und durch Target Scheduler. El Nina Wizard besetzt die Rolle „Web-Generator für die ganze Nacht", hängt derzeit aber an zwei Plugins.
- **Nicht abgedeckt:** ein Einsteigerweg, der einen Filterplan mit **Nachtfenstern je Linie** verbindet, ohne Datenbank und Planungsmaschine. Mond und Dämmerung je Filter gibt es nur in Target Scheduler, also beim schwersten Werkzeug. Genau das ist die NSTN-Nische.
- **Die häufigsten Fehler sind strukturell:** Schleifenumfang, Rundlauf gegen Vorrang, Trigger-Ort, Dither je Runde. Ein Generator verhindert sie, bevor sie entstehen. Die Sequenzprüfung in Night Info meldet einen Teil davon heute erst danach (`knowledge/tools/nina-nstn.md`, Abschnitt Night Info).

### 3.2 Lohnt ein eigener Generator?

**Ja, aber nur eng geschnitten.** Ein allgemeiner NINA-Generator mit Start, Geräten, Sicherheit und Flats wäre ein weiteres Werkzeug neben El Nina und PatriotAstro. Er hätte hohen Support-Anteil und keinen Vorsprung. Lohnend ist ein **NSTN-Ziel-Assistent**: ein Ziel, Filter, Belichtungen, Fokusstrategie. Heraus kommt der Ziel-Container im erprobten NSTN-Muster. Die Basis dafür existiert (`make_sample.py`, in NINA geladen).

### 3.3 Wo?

| Option | Für | Gegen |
|---|---|---|
| **A. Export im NSTN-Rechner** | Ziel, Filterkatalog (dieselben CSVs wie im Plugin) und Fenster sind schon da; der Nutzer sieht die Fenster und erzeugt daraus die Sequenz; Single-File, kein Server | zweite Implementierung des NINA-Formats (JS neben C# und Python); kennt weder die Filternamen im Profil noch die NINA-Version; der Rechner wird auch ohne NINA genutzt |
| B. Eigenständige Seite | klarer Zweck | doppelte Pflege von Ziel und Filter; kein Vorteil gegenüber A |
| **C. Im Plugin** („NSTN-Ziel anlegen") | Filterliste und Offsets direkt aus dem Profil; kein JSON-Format, weil Objekte über NINA entstehen. `ISequenceMediator.AddAdvancedTarget` und `AddTargetToTargetList` gibt es in 3.2 (`NINA.Sequencer/Interfaces/Mediator/ISequenceMediator.cs:34,44`), Plugins erhalten den Mediator (`NINA.Plugin/PluginLoader.cs:527`); die Sequenzprüfung folgt sofort | WPF-Aufwand; die Objekte außerhalb von NINAs Factory zu bauen ist **ungeprüft (Annahme)**; nur mit installiertem Plugin, was für NSTN-Blöcke ohnehin Voraussetzung ist |

Website: **A, nicht B.** Technisch robuster ist **C**. Siehe Empfehlung.

### 3.4 Kleinster sinnvoller Umfang (für A wie für C)

- **Eingaben:**
  - Filterset aus dem NSTN-Katalog
  - **exakte Filternamen wie im NINA-Profil**, mit Standardwerten vorbelegt
  - je Filter Belichtung und Aufnahmen pro Runde
  - Fokusstrategie: *Offsets* → Rundlauf jede Runde, oder *ohne Offsets* → größere Blöcke + „AF After Filter Change"
  - Dither einmal pro Runde ja/nein
- **Ausgabe:** ein Ziel-Container für **NINA 3.2.0.9001**, Versionsangabe im Dateinamen. Inhalt:
  - NSTN Night Info
  - äußere Schleife „Loop Until Time: astronomische Morgendämmerung"
  - Trigger Meridian-Flip auf Zielebene
  - „Wait for NSTN window"
  - je Filter ein Block mit `SwitchFilter`, Belichtungsreihe, NSTN-Bedingung `@block` und „Loop For Iterations 1"
  - Dither am Rundenende
- **Nicht im ersten Schritt:** Start und Ende (Verbinden, Kühlen, Parken), Flats, mehrere Ziele, Rotator, Target Scheduler, NINA 3.3, Variante ohne Plugin.
- **Test:** Goldtest gegen eine Datei, die NINA geladen und wieder gespeichert hat (Hin- und Rückweg), plus Ladeprobe per `--sequencefile`. Beides ist schon erprobt.

### 3.5 Risiken

| Risiko | Befund | Gegenmaßnahme |
|---|---|---|
| Internes, undokumentiertes JSON | `$type`, `$id`/`$ref`, `Parent`-Referenz nötig (eigener Befund). NINA lädt fehlerhafte Elemente als Platzhalter statt abzubrechen (`NINA.Sequencer/Serialization/JsonCreationConverter.cs:66-80`), Fehler bleiben also leise | nur Ziel-Container und nur Typen, die bereits geladen wurden; Hin- und Rückweg je NINA-Release |
| Wechsel 3.2 → 3.3 | 3.3 ist Nightly. Laut Nightly-Notes laufen bestehende Sequenzen weiter ([RELEASE_NOTES](https://github.com/daleghent/nina/blob/develop/RELEASE_NOTES.md)). Target Scheduler 5.10+ läuft nur unter 3.3; El Nina bietet getrennte Formate für 3.2 und 3.3 an, also gibt es vermutlich Unterschiede (Annahme) | Version fest ausweisen; 3.3 erst mit 3.3-fähigem Plugin |
| Filternamen ≠ Profil | `SwitchFilter` ordnet nach Name, sonst nach **Position** zu, ein falscher Name ergibt also still einen anderen Filter; leere Liste = `null` (`knowledge/tools/nina-nstn.md`) | Namen abfragen; `@block` lässt das Band dem tatsächlich zugeordneten Filter folgen, Night Info zeigt „Block filter: … → Band". Option C löst das Problem ganz |
| Plugin-Befehle ohne installiertes Plugin | Die Sequenz **lädt**, aber NSTN-Elemente werden „Unknown". Eine unbekannte Bedingung liefert `false` (`NINA.Sequencer/Conditions/UnknownSequenceCondition.cs`), der Block wird übersprungen; unbekannte Befehle werden übersprungen (`NINA.Sequencer/SequenceItem/UnknownSequenceItem.cs`). Es wird also nichts belichtet, NINA zeigt nur Issues an | Generator nur für NSTN-Nutzer, Hinweis prominent; keine stille Variante ohne Plugin |
| Supportaufwand | Jede erzeugte Sequenz gilt als „Marcs Sequenz"; Geräte- und Profilprobleme landen beim Generator | nur Ziel-Container (Gerätelogik bleibt beim Nutzer); Night Info als eingebaute Selbstprüfung; kurze FAQ |
| Drei Implementierungen | Format in Python (`make_sample.py`), C# (Plugin) und ggf. JS (Rechner) | eine Golddatei, die alle drei Implementierungen testen; bei C entfällt JS |

### 3.6 Projekt-Check (CLAUDE.md)

| # | Frage | Antwort |
|---|---|---|
| 1 | Konkreter Nutzen? | Für NSTN-Nutzer ja: Die häufigsten Strukturfehler entstehen gar nicht erst. Für allgemeine Mono-Nutzer gering, Alternativen gibt es |
| 2 | Laufende Kosten? | keine (läuft im Browser bzw. im Plugin); Pflege je NINA-Release |
| 3 | Herstellerabhängigkeit? | an NINAs internes Format (Open Source, MPL-2.0, aber undokumentiert); bei C an die Plugin-API statt an das Dateiformat |
| 4 | Betrieb/Wartung? | gering bei Ziel-Container-Umfang; Hin- und Rückweg je Release |
| 5 | In 5 Jahren sinnvoll? | unsicher: 3.3 bringt Ausdrücke in den Kern, NINA kann eigene Assistenten bekommen; das NSTN-Konzept bleibt |
| 6 | Automatisierbar? | ja, Goldtest und Ladeprobe existieren |
| 7 | Passt zu kleinem Team? | nur im kleinsten Umfang; El-Nina-Umfang (Sicherheit, Kuppel, 105 Anweisungen) nicht |
| 8 | Guten Gewissens empfehlen? | ja, für NSTN-Nutzer mit klarer Versionsangabe; kein Ersatz dafür, die Sequenz zu verstehen |

### 3.7 Empfehlung

- **Bauen: ja, als NSTN-Ziel-Assistent**, kein allgemeiner NINA-Generator.
- **Ort:** Soll es eine Website sein, dann als **Export im NSTN-Rechner**. Technisch robuster ist ein Builder **im Plugin**: Profilfilter direkt, kein Dateiformat, keine Plugin-Falle. Vor einer Entscheidung für C einen kleinen Prototyp bauen, der einen Container über die NINA-API anlegt.
- **Umfang:** wie 3.4, ein Ziel, NINA 3.2, ohne Start, Ende und Flats.

## 4. Neubewertung nach Rückmeldung (18.11.2026)

**Festlegungen von Marc:**
- Nutzer scheitern an der Logik der Schleifen. Auch die Trigger sind für viele nicht nachvollziehbar, und das NSTN-Muster verkompliziert das zusätzlich.
- Viele übernehmen die PatriotAstro-Vorlagen, ohne sie zu verstehen, bauen sie um und bekommen dann Probleme. Befürchtung: NSTN wird mangels Logikverständnis nicht genutzt.
- Das Plugin wird veröffentlicht.
- Keine Variante ohne Plugin: NSTN funktioniert nur mit NSTN.
- NINA 3.3 wird unterstützt.

### 4.1 Folgerung

Ein Generator, der eine Sequenz ausgibt, löst das Problem nicht. Der Nutzer bekommt dieselbe verschachtelte Struktur, die er beim Umbauen zerlegt; es ist das PatriotAstro-Muster aus Abschnitt 2, Punkt 7.

Beim NSTN-Muster ist das besonders heikel. „Loop For Iterations 1“ wirkt überflüssig. Ohne diese Bedingung behält ein Block das Fenster für sich (Abschnitt 2, Punkt 1).

Ziel ist deshalb, die Schleifenlogik zu **verstecken**, nicht sie zu erzeugen.

### 4.2 Vorschlag: Container „NSTN-Filterplan“

**Bedienung:** Der Nutzer legt ein einziges Element in den Ziel-Container und füllt eine Tabelle aus: Filter wie im Profil, Anzahl und Belichtungszeit je Runde. Die Kinder baut das Plugin selbst:
- „Wait for NSTN window“
- je Zeile `SwitchFilter`, die Belichtungen, die NSTN-Bedingung `@block` und „Loop For Iterations 1“
- die äußere Runde

**Belege aus dem NINA-Quelltext:**
- **Vorbild in NINA:** `SmartExposure` ist ein `SequentialContainer` mit `IImmutableContainer` und festen Kindern, einer `LoopCondition` und dem Trigger `DitherAfterExposures` (`NINA.Sequencer/SequenceItem/Imaging/SmartExposure.cs:49,70-71`).
  - Genauso gebaut sind `TakeManyExposures`, `TrainedFlatExposure`, `SkyFlat`, `AutoExposureFlat` und `AutoBrightnessFlat`.
  - Die Oberfläche zeigt solche Container mit der Vorlage einer einzelnen Anweisung, nicht als Baum (`NINA.Sequencer/View/SequenceDataTemplateSelector.cs:40-41`).
- **Plugins dürfen Container liefern** (`NINA.Plugin/PluginLoader.cs:738`, `ImportMany(typeof(ISequenceContainer))`).
- **Trigger greifen weiter:**
  - `SequentialStrategy` führt vor jedem Element die Trigger des Containers und aller übergeordneten Container aus (`NINA.Sequencer/Container/ExecutionStrategy/SequentialStrategy.cs:53,133-142`), nach jedem Element die Nachher-Trigger (`:58,144-153`).
  - Meridian-Flip, AF nach Filterwechsel und Dither am Ziel-Container laufen also auch zwischen den inneren Belichtungen.
  - Voraussetzung: Die Kinder sind echte NINA-Elemente. Eine einzelne Anweisung, die selbst belichtet, ließe dazwischen keine Trigger laufen.
- **Eigene Prüfung ist Pflicht:** `Sequencer.Validate` überspringt Immutable-Container, die ihre Kinder „itself“ prüfen sollen (`NINA.Sequencer/Sequencer.cs:206-208`).

**Idee, ungeprüft:** Der Container endet, sobald in dieser Nacht keine Zeile mehr ein offenes Fenster hat. Damit entfällt die feste Endzeit und mit ihr die Falle beim Tageswechsel (Abschnitt 2, Punkt 5).

**Annahmen, die ein Prototyp klären muss:**
- Kopieren beim Ziehen und beim Einfügen als Vorlage (`Clone()`) übernimmt Tabelle und Kinder.
- Laden stellt beides wieder her.
- Nach einer Tabellenänderung baut der Container seine Kinder neu.
- Die Darstellung als WPF-Vorlage funktioniert.

### 4.3 Optionen neu bewertet

| Option | Wirkung gegen das Logikproblem | Aufwand | Hauptrisiko |
|---|---|---|---|
| A. Export im Rechner | schwach: Der Nutzer sieht die Struktur und ändert sie | mittel | JSON-Format, Filternamen |
| C. Builder im Plugin | schwach: gleiche Struktur, aber korrekte Namen | mittel | Objekte außerhalb von NINAs Factory gebaut |
| **D. Filterplan-Container** | **stark: keine Schleifen sichtbar** | mittel bis hoch | Kopieren und Laden der Kinder, eigene Prüfung |
| E. Ablaufvorschau in Night Info („21:10–23:40 O III, dann Ha …“) | Ergänzung; hilft auch bei umgebauten Vorlagen | mittel | Die Simulation weicht vom NINA-Lauf ab |
| F. Doku mit Schaubild | Ergänzung | gering | allein zu wenig |

### 4.4 Trigger

Der Container übernimmt einen Teil selbst: das Dithern je Runde, intern wie bei Smart Exposure. Für den Rest genügt eine Regel in der Doku: **Trigger gehören an den Ziel-Container und gelten für alles darin.** Diese Regel ist am Quelltext belegt (4.2).

### 4.5 Veröffentlichung und NINA 3.3

- **Lizenz:** Sie muss vor der Veröffentlichung festgelegt werden.
- **Support:** Der Aufwand fällt vor allem in Forum und Issues an. Der Container senkt ihn, weil weniger falsch gebaute Sequenzen entstehen.
- **NINA 3.3:** 3.3 ist eine Nightly (Abschnitt 3.5). Entwickelt wird gegen 3.2, die Nightly wird regelmäßig mitgetestet.
  - Annahme: 3.3 bringt Brüche an der Plugin-API. Darauf deutet hin, dass Target Scheduler ab 5.10 nur noch unter 3.3 läuft.
  - Bis zum Release von 3.3 heißt das zwei Builds und doppelter Testaufwand.

### 4.6 Projekt-Check, Änderungen gegenüber 3.6

| # | Frage | Antwort |
|---|---|---|
| 1 | Konkreter Nutzen? | höher als beim Generator: Das Verständnisproblem entfällt, statt nur verschoben zu werden |
| 3 | Herstellerabhängigkeit? | an NINA-Klassen (`SequentialContainer`, `IImmutableContainer`, `SequentialStrategy`) statt an das Dateiformat |
| 4 | Betrieb/Wartung? | Prüfung je NINA-Release; bis zum Release von 3.3 zwei Builds |
| 7 | Passt zu kleinem Team? | ja, solange der Umfang bei einer Tabelle und einem Ziel bleibt |
| 8 | Guten Gewissens empfehlen? | ja; Nutzer müssen nur noch die Trigger-Regel aus 4.4 kennen |

### 4.7 Empfehlung (ersetzt 3.7)

- **Kein Generator als Hauptweg.** Der Export im Rechner (A) entfällt vorerst.
- **Filterplan-Container im Plugin (D).** Der Prototyp liegt samt Bericht auf dem Branch `prototyp/filterplan-container` (`docs/research/filterplan-container-prototyp.md`). Umgesetzt ist der Container als Befehl „NSTN filter plan“ in 0.7.0 (main, 18.11.2026).
- **Danach die Ablaufvorschau in Night Info (E)** und die Doku mit Schaubild (F).
- **Die bestehende NSTN-Bedingung bleibt** für Fortgeschrittene, die ihre Sequenz selbst bauen.
