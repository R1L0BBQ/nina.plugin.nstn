# NSTN für N.I.N.A. – Anleitung

NSTN („Narrowband Saves The Night“) belichtet jeden Filter dann, wenn der Himmel für ihn
dunkel genug ist. Das Plugin rechnet für dein Ziel die ganze Nacht durch und entscheidet
damit zur Laufzeit, welcher Filter gerade lohnt. Eingerechnet werden:

- Lichtverschmutzung des Standorts (SQM),
- Dämmerung,
- Mondlicht, getrennt je Emissionslinie und Bandbreite.

Statt fester Uhrzeiten, die nur für eine Nacht stimmen, gilt eine NSTN-Sequenz in jeder Nacht.

Diese Anleitung beschreibt Version 0.7.0 mit dem **NSTN-Filterplan**. Alle anderen Teile
gibt es seit 0.6.0.

## Inhalt

1. [Voraussetzungen](#voraussetzungen)
2. [Installieren](#installieren)
3. [Einrichten](#einrichten)
4. [Der schnelle Weg: NSTN-Filterplan](#der-schnelle-weg-nstn-filterplan)
5. [Was in der Nacht passiert](#was-in-der-nacht-passiert)
6. [NSTN-Nachtinfo](#nstn-nachtinfo)
7. [Der Weg von Hand: Blöcke mit NSTN-Bedingung](#der-weg-von-hand-blöcke-mit-nstn-bedingung)
8. [Häufige Fragen und Probleme](#häufige-fragen-und-probleme)
9. [Grenzen](#grenzen)

---

## Voraussetzungen

- **N.I.N.A. 3.2.0.9001**, die Version, für die das Plugin gebaut ist. N.I.N.A. 3.3 Nightly
  (3.3.0.1069) lädt dasselbe Plugin ebenfalls, wurde aber nur im Test geprüft.
- **Standort im Profil:** Breite und Länge unter *Optionen > Allgemein > Astrometrie*.
- **Ein gemessener SQM-Wert** des Standorts, also die Himmelshelligkeit im Zenit in
  mag/arcsec². Eine Bortle-Schätzung reicht nicht, der Wert geht direkt in die Rechnung ein.
- **Filterrad** mit gepflegter Filterliste im Profil (*Optionen > Ausrüstung > Filterrad*),
  wenn du den Filterplan nutzt.
- Die Uhr des Aufnahme-PCs muss stimmen. Alle Zeiten gelten in seiner lokalen Zeit.

## Installieren

1. N.I.N.A. beenden.
2. Aus dem ZIP `NINA.Plugin.Nstn-<Version>.zip` den Ordner `NSTN` nach
   `%LOCALAPPDATA%\NINA\Plugins\3.0.0\` entpacken. Danach liegen die drei Dateien
   `NINA.Plugin.Nstn.dll`, `Nstn.Core.dll` und `astronomy.dll` in
   `…\Plugins\3.0.0\NSTN\`.
3. N.I.N.A. starten. Unter *Plugins* steht jetzt „NSTN – Narrowband Saves The Night“.

> **Hinweis zu N.I.N.A. 3.3:** 3.2 und 3.3 nutzen denselben Plugin-Ordner (`3.0.0`), dieselben
> Profile und dieselbe Datenbank. Das Setup der 3.3-Nightly ersetzt eine installierte 3.2.
> Vor einem Umstieg die Profile in `%LOCALAPPDATA%\NINA\Profiles` sichern.

## Einrichten

### 1. Standort und Himmel

*Optionen > Plugins > NSTN*:

| Feld | Bedeutung | Standard |
|---|---|---|
| Himmelsqualität im Zenit (SQM) | Pflicht. 0 heißt „nicht gesetzt“, dann rechnet NSTN nicht. | – |
| Schwelle Breitband (L/RGB) | Ein Breitbandfenster öffnet, wenn der Himmel Richtung Ziel dunkler ist als dieser Wert | 20,6 |
| Schwelle Schmalband (im Band) | dasselbe für Schmalband, gemessen im Durchlassbereich des Filters | 23,1 |
| Mindesthöhe des Ziels | darunter ist jedes Fenster zu | 20° |
| Nachtplan: Zuschlag je Aufnahme | Download, Speichern, Dithern im Mittel; nur für die Schätzung | 10 s |

Die Schwellen sind kalibriert. Ändere sie nur an sehr hellen Standorten, und dann in kleinen Schritten.

### 2. Filter dieses Profils

Auf derselben Seite steht unter **„Filter dieses Profils“** jeder Filter aus der
Filterradliste. Lege für jeden fest, was er durchlässt:

- **Breitband:** L, R, G, B oder kein Filter.
- **ein Filter aus der Liste:** Duo-Filter und Schmalband-Sätze mit Linien und Bandbreiten aus
  den NSTN-Tabellen, zum Beispiel Askar D1, Optolong L-eXtreme oder Antlia-Sätze.
- **Eigene Angabe:** Linie und Bandbreite von Hand, etwa Hα 3 nm.

Neben jeder Zeile steht, wenn möglich, ein **Vorschlag**. Ein Vorschlag wird nur per
Knopf übernommen, entweder einzeln oder mit „Vorschläge für nicht zugeordnete Filter
übernehmen“. Eine Bandbreite erfindet NSTN nie: „Ha“ ohne Zahl bleibt ohne Vorschlag.

> **Duo-Filter:** Bei einem Duo-Filter (O III + Hα in einem Filter) rechnet NSTN mit der
> **schwächeren Linie**, meist O III. Sie ist am empfindlichsten gegen Mondlicht. Ist
> sie offen, lohnt der Filter. Wie du die Hα-Linie länger nutzt, steht
> unter [Duo-Filter an der Farbkamera](#duo-filter-an-der-farbkamera).

Hast du einen Filter umbenannt, klicke auf „Filterliste neu laden“.

### 3. Sprache

Das Plugin spricht die Sprache von N.I.N.A. (*Optionen > Allgemein > Sprache*): Deutsch oder
Englisch, andere Sprachen fallen auf Englisch zurück. Ein Wechsel wirkt sofort.

---

## Der schnelle Weg: NSTN-Filterplan

Der **NSTN-Filterplan** ist ein einzelner Befehl, so wie „Smart Belichtung“ (*Smart Exposure*). Du trägst nur ein,
welche Filter du willst, wie viele Aufnahmen je Runde und wie lange. Den Rest baut der
Plan selbst: warten, Filter wechseln, Fenster prüfen, Runde für Runde bis zum Ende der Nacht.

### Sequenz anlegen

1. Im *Sequenzer* einen **Deep Sky Object Instruction Set** (Ziel-Container) anlegen und das
   Ziel eintragen. NSTN braucht dessen Koordinaten.
2. In den Ziel-Container, von oben nach unten:
   - **NSTN-Nachtinfo** (Kategorie NSTN). Sie ist freiwillig, zeigt aber die Nacht und prüft den Aufbau.
   - „Schwenken und Zentrieren“, danach „Guiding starten“, wie gewohnt.
   - **NSTN-Filterplan** (Kategorie NSTN).
3. An den Ziel-Container als **Auslöser**:
   - **Meridianflip**,
   - **Dither nach Belichtungen**,
   - **AF nach Filterwechsel**, oder Fokus-Offsets im Filterrad (siehe unten).

So sieht das aus:

```
Deep Sky Object Instruction Set „NGC 7000“      Auslöser: Meridianflip, Dither nach Belichtungen, AF nach Filterwechsel
├─ NSTN-Nachtinfo
├─ Schwenken und Zentrieren, Guiding starten
└─ NSTN-Filterplan
     Filter Askar D1   #  5 × 300 s
     Filter Ha         #  5 × 300 s
     Filter L          # 10 × 120 s
     Gain -1  Offset -1  Binning 1x1  Runden je Filter 1  Abbruch nach Fehlschlägen in Folge 3
```

Die Auslöser am Ziel greifen auch zwischen den Aufnahmen **innerhalb** des Filterplans,
genau wie bei Smart Belichtung. Das ist in N.I.N.A. 3.2 und 3.3 mit Simulatoren geprüft:
Meridianflip, AF und Dither wurden ausgeführt.

### Die Felder

**Je Zeile**

| Feld | Bedeutung |
|---|---|
| Filter | Name des Filters **wie in der Filterliste des Profils**, Groß-/Kleinschreibung egal. Das Band nimmt der Plan aus „Filter dieses Profils“. |
| # | Aufnahmen je Runde für diesen Filter. Darüber gewichtest du die Filter. |
| × … s | Belichtungszeit |
| Entfernen | Zeile löschen |

„Filter hinzufügen“ hängt eine Zeile mit dem ersten noch nicht benutzten Filter an. Ein neuer
Plan startet mit den ersten drei Filtern des Profils, je 5 × 300 s.

**Für den ganzen Plan**

| Feld | Bedeutung |
|---|---|
| Gain, Offset | für jede Aufnahme; −1 = Kamerastandard |
| Binning | für jede Aufnahme |
| Runden je Filter | Wie oft ein Filter dran ist, bevor der nächste kommt. **1 = Rundlauf**: jede Runde jeder Filter einmal. Mit **3** belichtet der Plan 3 × hintereinander denselben Filter und wechselt erst dann. Das spart Filterwechsel und vor allem Autofokus-Läufe. |
| Abbruch nach Fehlschlägen in Folge | Der Plan endet, wenn so viele Aufnahmen hintereinander scheitern. Standard 3, **0 = nie**. Siehe [Was in der Nacht passiert](#was-in-der-nacht-passiert). |

Unter den Feldern zeigt der Plan:

- **Gliederung:** was aus den Zeilen gebaut wird, zum Beispiel „2. Askar D1: 5 × 300 s,
  solange sein NSTN-Fenster offen ist, einmal je Runde“.
- **Endezeile:** wie lange der Plan läuft, etwa „Läuft, bis das letzte Fenster schließt (05:52)“.
- **je Zeile das Fenster heute Nacht und den Zustand jetzt:**
  „Ha: 5 × 300 s – Heute Nacht (18. Nov.): 18:42–21:02, 01:08–06:04 · 7 h 16 min /
  Zu, öffnet 18:42 · Himmel im Band 5,19, nötig 23,10“.
- **Nachtplan:** eine Zeitleiste je Zeile. Blass ist das Fenster, kräftig die geschätzten
  Belichtungszeiten. Dazu kommen die erwartete Integrationszeit je Filter und das voraussichtliche Ende.
  „Neu berechnen“ aktualisiert sofort, sonst geschieht das alle 20 Sekunden.

> **Autofokus und Rundlauf:** „AF nach Filterwechsel“ fokussiert bei **jedem** Filterwechsel.
> Im Rundlauf mit drei Filtern sind das sehr viele AF-Läufe. Abhilfe:
> - „Runden je Filter“ erhöhen (3 statt 1 drittelt die AF-Läufe), oder
> - in der Filterliste des Profils einen „Fokus Offset“ je Filter hinterlegen und statt
>   „AF nach Filterwechsel“ zum Beispiel „AF nach Zeit“ oder „AF nach Temperaturänderung“ nutzen.

> **Während der Plan läuft**, sind Zeilen und Planwerte gesperrt. Änderungen erst nach
> dem Lauf oder nach einem Stopp.

### Beispiele für die Gewichtung

- **Gleich viel O III wie Hα:** Askar D1 5 × 300 s, Ha 5 × 300 s.
  O III bekommt trotzdem weniger, weil sein Fenster bei Mond kürzer ist. Genau das ist gewollt.
- **Schmalband-Satz SHO:** Ha 3 × 300 s, OIII 6 × 300 s, SII 6 × 300 s.
  Die schwachen Linien bekommen mehr je Runde.
- **Breitband nur in der mondfreien Zeit:** L 10 × 120 s als eigene Zeile. Ihr Fenster
  öffnet nur, wenn der Himmel dunkel genug ist; sonst wird sie übersprungen.

---

## Was in der Nacht passiert

1. **Warten:** Der Plan wartet, bis das erste Fenster eines seiner Filter öffnet. Er wartet
   ohne Last; N.I.N.A. bleibt bedienbar, Abbrechen geht jederzeit.
2. **Runde für Runde:**
   - Jede Zeile kommt einmal dran, bei „Runden je Filter“ > 1 entsprechend oft hintereinander.
   - Ein Filter, dessen Fenster gerade zu ist, wird übersprungen.
   - Geprüft wird **vor jeder Aufnahme**. Eine laufende Aufnahme wird nie abgebrochen.
3. **Auslöser:** Meridianflip, Dither und AF vom Ziel-Container greifen zwischen den
   Aufnahmen.
4. **Ende ohne Uhrzeit:** Öffnet heute Nacht kein Fenster mehr, endet der Plan von selbst.
   Im Protokoll steht dann „no window left tonight, ending“. Danach macht N.I.N.A. mit dem
   weiter, was im Ziel-Container nach dem Plan steht, oder mit dem Endbereich der Sequenz
   (Parken, Kamera aufwärmen …).
5. **Ende bei Fehlschlägen:** Scheitern Aufnahmen hintereinander so oft wie unter
   „Abbruch nach Fehlschlägen in Folge“ eingestellt, endet der Plan. Ursachen sind etwa eine
   getrennte Kamera oder ein fehlender Bildordner. Du bekommst eine Meldung in N.I.N.A. und
   eine Warnung im Protokoll. Ohne diese Grenze würde der Plan die ganze Nacht Filter
   wechseln und fokussieren, ohne ein einziges Bild zu speichern. Eine gelungene Aufnahme
   setzt den Zähler zurück.

Im Protokoll nennt jede Zeile den Block, also die Zeile des Plans, etwa
`NstnWindowCondition O III in 'Askar D1: 5 × 300 s' open: …`.

---

## NSTN-Nachtinfo

„NSTN-Nachtinfo“ gehört an den Anfang des Ziel-Containers. Beim Ablauf kostet sie keine Zeit,
sie schreibt nur eine Zusammenfassung ins Protokoll. Im Sequenzer zeigt sie:

- **Die Nacht:**
  - Sonne: Untergang und Aufgang, die drei Dämmerungen, Dunkelzeit.
  - Mond: Phase, Auf- und Untergang, mondfreie Dunkelzeit.
  - Ziel: Kulmination, nutzbare Zeit, Mondabstand, dunkelster Himmel Richtung Ziel.
  - Die Fenster je Band.
- **Ein Diagramm:** Höhe von Ziel und Mond, Mondabstand, Himmelshelligkeit mit SQM-Linie und
  die Fenster. Unter der Maus siehst du die Werte je 10 Minuten.
- **Die Sequenzprüfung:** Warnungen (rot) und Hinweise zum Aufbau des Ziels, zum Beispiel ein
  fehlender Meridianflip-Auslöser, obwohl das Ziel in der Nacht kulminiert. Ist alles in
  Ordnung, meldet sie „Der Aufbau dieses Ziels folgt dem NSTN-Muster“. Zum Filterplan gibt es
  zwei eigene Meldungen:
  - einen **Hinweis**, wenn der Plan im Rundlauf läuft und „AF nach Filterwechsel“ am Ziel hängt;
  - eine **Warnung**, wenn im selben Container hinter dem Plan noch NSTN-Arbeit steht, etwa ein
    zweiter Filterplan. Sie bekommt in dieser Nacht keine Zeit mehr, weil der Plan bis zum Ende
    der Nacht läuft.
- **„So wird eine NSTN-Sequenz aufgebaut“:** der empfohlene Aufbau zum Aufklappen.

---

## Der Weg von Hand: Blöcke mit NSTN-Bedingung

Der Filterplan deckt den Normalfall ab. Wer mehr Kontrolle braucht, baut das Muster aus
einzelnen Befehlen selbst. Das Plugin bringt dafür mit:

- **„Schleife solange NSTN-Fenster offen“:** die Bedingung an einem Block.
- **„Auf NSTN-Fenster warten“:** der Befehl am Anfang der Schleife.

```
Deep Sky Object Instruction Set „NGC 7000“      Auslöser: Meridianflip, Dither, AF
├─ NSTN-Nachtinfo
├─ Schwenken und Zentrieren, Guiding starten
└─ Sequentielles Anweisungsset  – Bedingung: Bis Zeitpunkt wiederholen (Morgendämmerung)
     ├─ Auf NSTN-Fenster warten
     ├─ Block O III – Bedingungen: Schleife solange NSTN-Fenster offen (O III) + Schleife für Iterationen 1
     │     └─ Smart Belichtung 5 × 300 s, Filter OIII
     └─ Block Hα    – Bedingungen: Schleife solange NSTN-Fenster offen (Hα) + Schleife für Iterationen 1
           └─ Smart Belichtung 5 × 300 s, Filter Ha
```

Die Regeln, auf die es dabei ankommt:

1. **Bedingungen an einem Block sind Schleifenbedingungen.** Ein Block nur mit der
   NSTN-Bedingung wiederholt sich, solange sein Fenster offen ist, und behält es für sich.
   Erst **„Schleife für Iterationen 1“** dazu macht daraus einen Rundlauf.
2. **„Auf NSTN-Fenster warten“ gehört an den Anfang der Schleife.** Sind alle Fenster zu,
   dreht N.I.N.A. 3.2 die Schleife sonst ungebremst leer durch und füllt das Protokoll.
3. **Die äußere Schleife braucht ein eigenes Ende**, zum Beispiel „Bis Zeitpunkt wiederholen“
   (Morgendämmerung). Ohne Bedingung läuft sie genau einmal.
4. In der Bedingung wählst du unter **Filter** entweder einen Filter aus den NSTN-Tabellen,
   eine eigene Angabe (Band und Bandbreite von Hand) oder **„Aus dem Filter dieses Blocks“**.
   Dann liest die Bedingung den Filter aus „Filter wechseln“ bzw. der Smart Belichtung des Blocks
   und nimmt das Band aus „Filter dieses Profils“.

„Auf NSTN-Fenster warten“ zeigt denselben Nachtplan wie der Filterplan.

### Duo-Filter an der Farbkamera

Mit einem Duo-Filter an der Farbkamera kann man beide Linien getrennt ausnutzen. Das geht
nur von Hand:

- zwei Blöcke mit demselben Filter;
- im einen Block die Bedingung „solange O III offen“, im anderen „solange Hα offen“;
- beide Male den Filter direkt in der Bedingung wählen, nicht „Aus dem Filter dieses Blocks“.

So sammelt die Kamera Hα weiter, wenn O III wegen des Mondes schon zu ist. Der Filterplan
rechnet je Zeile mit einer Linie und kann das deshalb nicht.

---

## Häufige Fragen und Probleme

| Symptom | Ursache und Abhilfe |
|---|---|
| Rotes Ausrufezeichen, „Himmelsqualität des Standorts (SQM) … eintragen“ | SQM-Wert unter *Optionen > Plugins > NSTN* eintragen. |
| „Filter „…“ steht nicht in der Filterliste dieses Profils“ | Der Name in der Zeile weicht von der Filterliste ab. In der Auswahl der Zeile einen Filter aus der Liste wählen. |
| „Filter „…“ hat keine NSTN-Zuordnung“ | Unter „Filter dieses Profils“ fehlt die Zuordnung für diesen Filter. |
| „Diese Bedingung gehört in einen Ziel-Container“ | Plan oder Bedingung liegen nicht in einem Deep Sky Object Instruction Set. |
| Ein Filter wird nie belichtet | Sein Fenster öffnet heute Nacht nicht, etwa Breitband bei Mond oder ein Ziel zu tief. Das zeigt die Fensterzeile im Plan („nie offen“) und die Nachtinfo. |
| Der Plan wartet und wartet | Es ist noch kein Fenster offen. Die Zeile über dem Nachtplan sagt, wann das erste öffnet. |
| Der Plan endet mitten in der Nacht mit einer Meldung | Mehrere Aufnahmen hintereinander sind gescheitert. Im Protokoll nach „exposures in a row failed“ und den Fehlern davor suchen. Meist ist die Kamera getrennt oder der Bildordner fehlt. |
| Nachtinfo warnt „… steht in … hinter …“ | Hinter dem Filterplan steht noch ein NSTN-Block oder ein zweiter Plan. Seine Filter als Zeilen in den einen Plan aufnehmen. |
| Sehr viele Autofokus-Läufe | Siehe [Autofokus und Rundlauf](#die-felder): „Runden je Filter“ erhöhen oder Fokus-Offsets nutzen. |
| Im Protokoll viele Zeilen „skipped“ | N.I.N.A. schreibt je übersprungenem Block einige Zeilen. Das ist normal, wenn Fenster zu sind. |
| Eine Sequenz mit NSTN wird ohne Plugin geöffnet | N.I.N.A. lädt sie zwar, die NSTN-Teile laufen aber nicht; NSTN-Blöcke werden still übersprungen. Das Plugin muss auf dem Aufnahme-PC installiert sein. |

---

## Grenzen

- Gerechnet wird nur **die laufende Nacht**. Die Anzeige für eine andere Nacht gibt es noch nicht.
- **Uhrzeiten** gelten in der lokalen Zeit des Aufnahme-PCs.
- Der **Nachtplan** ist eine Schätzung. Autofokus, Meridianflip, Filterwechsel und Schwenks
  stecken nicht darin, nur der Zuschlag je Aufnahme.
- Der Filterplan rechnet **je Zeile mit einer Linie**, siehe
  [Duo-Filter an der Farbkamera](#duo-filter-an-der-farbkamera).
- Der Name eines gespeicherten Plans bleibt so, wie er gespeichert wurde. Ein in englischer
  Oberfläche angelegter Plan heißt auch in deutscher Oberfläche „NSTN filter plan“.
- Das Himmelsmodell nutzt den **eingetragenen SQM-Wert**, keine Live-Messung von einem Wettergerät.
- Geprüft:
  - mit Simulatoren in N.I.N.A. 3.2 und 3.3 (Kamera, Montierung, Filterrad, Fokussierer, Mount Dither);
  - noch nicht in einer echten Nacht mit echten Geräten.
