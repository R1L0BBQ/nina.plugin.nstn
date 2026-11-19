# NSTN für N.I.N.A. – Anleitung

NSTN („Narrowband Saves The Night“) belichtet jeden Filter dann, wenn der Himmel für ihn
dunkel genug ist. Das Plugin rechnet für dein Ziel die ganze Nacht durch und entscheidet
damit zur Laufzeit, welcher Filter gerade lohnt. Eingerechnet werden:

- Lichtverschmutzung des Standorts (SQM),
- Dämmerung,
- Mondlicht, getrennt je Emissionslinie und Bandbreite.

Statt fester Uhrzeiten, die nur für eine Nacht stimmen, gilt eine NSTN-Sequenz in jeder Nacht.

Diese Anleitung beschreibt Version 0.11.0. Den **NSTN-Filterplan** gibt es seit 0.7.0,
alle anderen Teile seit 0.6.0.

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

**Mit dem Installer** (`NSTN-Installer-<Version>.zip`, ab 0.11.1):

1. N.I.N.A. beenden.
2. Das ZIP vollständig entpacken und im entpackten Ordner `Install-NSTN.cmd` doppelklicken.
   Keine Administratorrechte nötig; installiert wird für den angemeldeten Windows-Benutzer.
3. N.I.N.A. starten. Unter *Plugins* steht jetzt „NSTN – Narrowband Saves The Night“.

Der Installer prüft die Prüfsummen des Pakets, bricht bei laufender N.I.N.A. ab und sichert
eine vorhandene NSTN-Version nach `%LOCALAPPDATA%\NINA\NSTN-Sicherung\<Zeitpunkt>-<Version>`.
Die NSTN-Einstellungen im Profil bleiben. `Uninstall-NSTN.cmd` entfernt das Plugin, ebenfalls
mit Sicherung. Zurück zu einer älteren Version: bei geschlossener N.I.N.A. den Inhalt von
`…\Plugins\3.0.0\NSTN\` durch den gesicherten Ordner ersetzen.

**Von Hand** (`NINA.Plugin.Nstn-<Version>.zip`):

1. N.I.N.A. beenden.
2. Den Ordner `NSTN` aus dem ZIP nach `%LOCALAPPDATA%\NINA\Plugins\3.0.0\` entpacken.
   Danach liegen die drei Dateien `NINA.Plugin.Nstn.dll`, `Nstn.Core.dll` und
   `astronomy.dll` in `…\Plugins\3.0.0\NSTN\`.
3. N.I.N.A. starten.

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
| Schwelle Schmalband (Himmel im Band) | dasselbe für Schmalband, gemessen im Durchlassbereich des Filters | 23,1 |
| Mindesthöhe des Ziels | darunter ist jedes Fenster zu | 20° |
| Nachtplan: Zuschlag je Aufnahme | Download, Speichern, Dithern im Mittel; nur für die Schätzung | 10 s |

Die Schwellen sind kalibriert. Ändere sie nur an sehr hellen Standorten, und dann in kleinen Schritten.
Beide sind Himmelshelligkeiten in mag/arcsec²: höher heißt, der Himmel muss dunkler sein.
Unter 18 warnen die Optionen. So niedrig ist der Himmel praktisch immer dunkel genug, und
das Fenster folgt nur noch der Höhe des Ziels.

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

> **Filtersätze:** Wählst du einen Satz mit mehreren Linien, etwa „Astronomik (6 nm)“, nimmt
> NSTN die Linie aus dem Filternamen: „S2“ wird S II, „O3“ wird O III. Steht im Namen keine
> Linie, wähle sie selbst. Passt eine gespeicherte Linie nicht zum Namen, steht hinter der
> Zuordnung „prüfen: der Filtername nennt S II, eingestellt ist Hα“. Ein so falsch
> zugeordneter Filter bekommt die Fenster der falschen Linie.

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
     Gain -1  Offset -1  Binning 1x1  Sätze am Stück 1  Abbruch nach Fehlschlägen in Folge 3
```

Die Auslöser am Ziel greifen auch zwischen den Aufnahmen **innerhalb** des Filterplans,
genau wie bei Smart Belichtung. Das ist in N.I.N.A. 3.2 und 3.3 mit Simulatoren geprüft:
Meridianflip, AF und Dither wurden ausgeführt.

### Die Felder

**Je Zeile**

| Feld | Bedeutung |
|---|---|
| Filter | Name des Filters **wie in der Filterliste des Profils**, Groß-/Kleinschreibung egal. Das Band nimmt der Plan aus „Filter dieses Profils“. Ganz oben in der Liste steht **L/RGB** (siehe unten). |
| # | Aufnahmen je Runde für diesen Filter. Darüber gewichtest du die Filter. |
| × … s | Belichtungszeit |
| Entfernen | Zeile löschen |

**Die Zeile L/RGB (ab 0.10.0)** fasst L, R, G und B zu einer Zeile zusammen. Die vier teilen sich
das Breitband-Fenster, und für den Vorrang zählen sie als eine Zeile.

| Feld | Bedeutung |
|---|---|
| Verhältnis | L:R:G:B je Runde, z. B. **3:1:1:1** (drei L, dann je ein R, G, B) oder **0:1:1:1** (nur RGB). Erlaubt sind 0 bis 99. |
| L … s | Belichtungszeit für L |
| RGB … s | Belichtungszeit für R, G und B |

Welche Filter des Profils L, R, G und B sind, erkennt NSTN am Namen: L, Lum, Luminance, Clear;
R, Red, Rot; G, Green, Grün; B, Blue, Blau, auch als Wort in einem längeren Namen („Baader R“).
Ein Filter, der in „Filter dieses Profils“ einer Schmalbandlinie zugeordnet ist, zählt nie
(„L-eXtreme“ ist kein L). Fehlt einer, meldet der Plan das rot.

„Filter hinzufügen“ hängt eine Zeile mit dem ersten noch nicht benutzten Filter an. Ein neuer
Plan startet mit den ersten drei Filtern des Profils, je 5 × 300 s.

**Für den ganzen Plan**

| Feld | Bedeutung |
|---|---|
| Gain, Offset | für jede Aufnahme; −1 = Kamerastandard. Eine Zeile kann eigene Werte haben (siehe unten). |
| Binning | für jede Aufnahme, ebenso je Zeile änderbar |
| Sätze am Stück | Wie oft die Aufnahmen einer Zeile hintereinander laufen, bevor der nächste Filter kommt. **1 = Rundlauf**: jede Runde jeder Filter einmal. Mit **3** belichtet der Plan 3 × hintereinander denselben Filter (bei 5 × 300 s also 15 Aufnahmen) und wechselt erst dann. Das spart Filterwechsel und vor allem Autofokus-Läufe. Bis 0.8 hieß das Feld „Runden je Filter“. |
| Abbruch nach Fehlschlägen in Folge | Der Plan endet, wenn so viele Aufnahmen hintereinander scheitern. Standard 3, **0 = nie**. Siehe [Was in der Nacht passiert](#was-in-der-nacht-passiert). |

Der Plan ist in vier Abschnitte gegliedert: **Filter je Runde** (die Zeilen), **Für alle
Aufnahmen** (die Planwerte), **Ablauf** und die **Nacht**. Sie zeigen:

- **Ablauf:** Neben dem Knopf steht immer die Endezeile, also wie lange der Plan läuft, etwa „Läuft, bis das
  letzte Fenster schließt (05:52)“. Der Knopf klappt die Gliederung auf: was aus den Zeilen gebaut
  wird, zum Beispiel „2. Askar D1: 5 × 300 s je Runde, nur bei offenem Fenster“.
- **Nacht 18./19. Nov.:** Die Überschrift nennt die Nacht mit beiden Daten. Je Zeile steht das
  Fenster, darunter blasser der Zustand jetzt: „Ha: 5 × 300 s – offen 18:42–21:02, 01:08–06:04
  (7 h 16 min) / Zu, öffnet 18:42 · Himmel im Band 25,2 mag/arcsec², Schwelle 23,1“. Steht die
  Sonne über dem Horizont, steht dort statt des Himmelswerts „Sonne über dem Horizont“.
- **Welche Nacht:** bis Sonnenaufgang die laufende, danach schon die kommende. Wer morgens in die
  Sequenz schaut, sieht den Plan für heute Abend, nicht die vergangene Nacht. Ein laufender Plan
  behält seine Nacht: Ist er bei Sonnenaufgang noch nicht fertig, endet er mit „Die Nacht
  18./19. Nov. ist vorbei.“ und wartet nicht bis zum Abend.
- **Nachtplan:** eine Zeitleiste je Zeile. Blass ist das Fenster, kräftig die geschätzten
  Belichtungszeiten. Darüber stehen die erwartete Integrationszeit je Filter und das voraussichtliche
  Ende, etwa „Geplant ab 18:42: H-Alpha 7 nm 3 h 10 min, … · Ende 06:04: das letzte Fenster
  schließt“. Ist nichts mehr übrig: „Keine Aufnahmen mehr in dieser Nacht.“
  „Neu berechnen“ aktualisiert sofort, sonst geschieht das alle 20 Sekunden.

> **Autofokus und Rundlauf:** „AF nach Filterwechsel“ fokussiert bei **jedem** Filterwechsel.
> Im Rundlauf mit drei Filtern sind das sehr viele AF-Läufe. Abhilfe:
> - „Sätze am Stück“ erhöhen (3 statt 1 drittelt die AF-Läufe), oder
> - in der Filterliste des Profils einen „Fokus Offset“ je Filter hinterlegen und statt
>   „AF nach Filterwechsel“ zum Beispiel „AF nach Zeit“ oder „AF nach Temperaturänderung“ nutzen.

> **Während der Plan läuft**, sind Zeilen und Planwerte gesperrt. Änderungen erst nach
> dem Lauf oder nach einem Stopp.

### Eigene Werte je Zeile (Fenster ab 0.8.0, Kamera ab 0.11.0)

Normalerweise gilt für alle Zeilen dasselbe: die Schwellen und die Mindesthöhe aus den
Plugin-Optionen, der Rundlauf und die Kamerawerte aus „Für alle Aufnahmen“. Über
**„Eigene Werte“** neben einer Zeile lässt sich das für diese eine Zeile ändern. Aufgeklappt
stehen zwei Zeilen: **Fenster** und **Kamera**.

**Fenster.** Die Einstellungen gelten zusätzlich: Ob der Himmel gut genug ist, rechnet NSTN weiterhin.

| Feld | Wirkung | Beispiel |
|---|---|---|
| **Vorrang** | Öffnet das Fenster der Zeile, übernimmt sie **nach der laufenden Aufnahme**, auch mitten in einer Runde. Solange es offen ist, läuft nichts anderes. Sind zwei Zeilen mit Vorrang offen, läuft die obere. | L/RGB mit Vorrang: Das kurze Breitband-Fenster geht ganz an L/RGB, die Schmalbandzeilen bekommen den Rest der Nacht. |
| **Schwelle** | Wird zur Schwelle der Plugin-Optionen addiert, in mag/arcsec². Plus heißt strenger, das Fenster wird kürzer und liegt im dunkelsten Teil der Nacht. Minus heißt lockerer, das Fenster wird länger. | O III +0,3: nur bei sehr dunklem Himmel. Hα −0,3: auch noch bei mehr Mond. |
| **Mindesthöhe** | Mindesthöhe des Ziels für diesen Filter; leer nimmt die Plugin-Optionen. | L 40°: Breitband erst, wenn das Ziel hoch steht. |
| **nicht vor / nicht nach** | Ortszeit als HH:mm. Zeiten vor Mittag gehören zum Morgen danach. Leer heißt keine Grenze. | B nicht nach 01:00. |

**Kamera.** Gain, Offset und Binning nur für diese Zeile. Leer übernimmt den Wert aus „Für alle
Aufnahmen“; Gain oder Offset −1 heißt Wert der Kamera.

| Zeile | Felder | Beispiel |
|---|---|---|
| ein Filter | Gain, Offset, Binning | Ha mit Gain 100 und Offset 30, die anderen mit dem Planwert. |
| L/RGB | einmal für L, einmal für R, G und B | L 1x1, RGB 2x2: Farbe gebinnt, Luminanz in voller Auflösung. |

Neben der Zeile steht dann eine Kurzfassung, etwa „Vorrang · 0,3 mag strenger · ab 22:00 · bis 02:00“
oder „Gain 100, Offset 30, 2x2“, bei L/RGB „L: Gain 0 · RGB: 2x2“. Fensterzeile, Nachtplan und
Gliederung rechnen mit diesen Werten. Ändert sich ein Wert in „Für alle Aufnahmen“, behalten
Zeilen mit eigenem Wert ihren. Ohne eigene Werte speichert der Plan genau wie bisher.

> **Vorrang mit Augenmaß:** Eine Zeile mit Vorrang nimmt sich ihr ganzes Fenster. Ohne
> strengere Schwelle oder Zeitspanne bekommen die anderen Filter dann oft nichts mehr. Am
> besten kombinierst du Vorrang mit einer engen Schwelle oder Zeitspanne, oder du gibst ihn
> einer Zeile mit kurzem Fenster, etwa L/RGB in der mondfreien Zeit.
>
> **Warum Vorrang für kurze Fenster?** Ohne Vorrang prüft der Plan eine Zeile nur, wenn die
> Runde bei ihr ankommt. Mit drei Schmalbandzeilen à 2 × 5 × 300 s dauert eine Runde gut
> 2,5 Stunden. Ein Breitband-Fenster von 76 Minuten, das mitten in der Runde öffnet und
> schließt, geht dann leer aus. Mit Vorrang übernimmt L/RGB nach der laufenden Aufnahme.

### Beispiele für die Gewichtung

- **Gleich viel O III wie Hα:** Askar D1 5 × 300 s, Ha 5 × 300 s.
  O III bekommt trotzdem weniger, weil sein Fenster bei Mond kürzer ist. Genau das ist gewollt.
- **Schmalband-Satz SHO:** Ha 3 × 300 s, OIII 6 × 300 s, SII 6 × 300 s.
  Die schwachen Linien bekommen mehr je Runde.
- **Breitband nur in der mondfreien Zeit:** L 10 × 120 s als eigene Zeile. Ihr Fenster
  öffnet nur, wenn der Himmel dunkel genug ist; sonst wird sie übersprungen.
- **LRGB plus Schmalband:** L/RGB 3:1:1:1 (L 60 s, RGB 120 s) **mit Vorrang**, dazu Ha, S II und
  O III je 5 × 300 s. Im Breitband-Fenster läuft nur L/RGB, sonst die Schmalbandzeilen im Rundlauf.

### Fertige Beispielsequenz

`NSTN-LRGB-Ha.de.json` (deutsch) und `NSTN-LRGB-Ha.json` (englisch) sind eine komplette Sequenz
zum Laden und Weitergeben. Sie liegen im Repository unter `samples/`. Die beiden Fassungen
unterscheiden sich nur in den Anmerkungen und den Namen.

- **Laden:** im *Sequenzer* unter *Advanced Sequencer* das Ordnersymbol „Sequenz laden“.
- **Inhalt:**
  - Start: Kamera auf −10 °C kühlen, entparken.
  - Ziel M 33 mit den Auslösern Meridianflip, Dither nach 3 Aufnahmen und AF nach Zeit (60 min).
  - Im Ziel: NSTN-Nachtinfo, warten bis zur nautischen Dämmerung und bis das Ziel 20° hoch
    steht, dann schwenken und zentrieren, Autofokus, Guiding starten und der Filterplan:
    L/RGB 3:1:1:1 (L 60 s, RGB 120 s) **mit Vorrang** und Ha 5 × 300 s, Sätze am Stück 2.
    Im kurzen Breitband-Fenster läuft also nur L/RGB, den Rest der Nacht Ha.
  - Ende: Guiding stoppen, Kamera aufwärmen, parken.
- **Autofokus auf L:** einmal vor dem Guiding, danach jede Stunde. Damit N.I.N.A. dafür auf L
  wechselt, braucht das Profil zwei Einstellungen:
  1. *Optionen > Ausrüstung > Filterrad:* beim L-Filter „Autofokus Filter“ anhaken.
  2. *Optionen > Ausrüstung > Okularauszug:* „Filterrad Offsets nutzen“ einschalten.

  N.I.N.A. wechselt dann für jeden Autofokus auf L und danach zurück zum Filter, der gerade
  belichtet wurde. Der Filterplan läuft also einfach weiter. Damit der Fokus auch für R, G, B
  und Ha stimmt, trägst du in der Filterliste je Filter den **Fokus-Offset relativ zu L** ein.
  Ohne die beiden Einstellungen fokussiert N.I.N.A. mit dem Filter, der gerade drin ist.
- **Danach anpassen:**
  - Ziel (Namenssuche, Rahmung oder Sternenatlas);
  - Kühltemperatur;
  - die Filter in den Zeilen, falls sie im Profil anders heißen. Der Plan meldet das rot.

> **Warum kein „Filter wechseln“ vor dem Autofokus?** Der Filterplan wechselt den Filter am
> Anfang eines Blocks, nicht vor jeder Aufnahme. Ein Filterwechsel im Auslöser würde die
> restlichen Aufnahmen dieses Blocks auf L belichten. N.I.N.A.s eigener Autofokus-Filter
> wechselt dagegen nach dem Autofokus zurück.

Die Wartebefehle vor dem Zentrieren haben einen Grund. Der Plan wartet zwar selbst auf sein
erstes Fenster, Zentrieren und Guiding laufen aber vorher. Ohne die Wartebefehle würde
N.I.N.A. in der hellen Dämmerung oder unter den Horizont schwenken.

> **Breitband an hellen Standorten:** L/RGB braucht einen Himmel, der Richtung Ziel dunkler
> ist als die Breitband-Schwelle (Standard 20,6). Liegt der SQM-Wert des Standorts darunter,
> öffnen die L-, R-, G- und B-Zeilen nie. Die Nachtinfo warnt dann „öffnet nie“, und
> belichtet wird nur Ha.

Eine komplette Sequenz kann keine Vorlage im Vorlagenordner sein, denn N.I.N.A. lässt dort
nur Teile einer Sequenz zu. Wer nur den Ziel-Container als Vorlage will, hat nach dem Laden zwei Wege: den Knopf
„Als Vorlage speichern“ in der Kopfzeile des Containers, oder den Container in die
Vorlagenliste ziehen.

---

## Was in der Nacht passiert

1. **Warten:** Der Plan wartet, bis das erste Fenster eines seiner Filter öffnet. Er wartet
   ohne Last; N.I.N.A. bleibt bedienbar, Abbrechen geht jederzeit.
2. **Runde für Runde:**
   - Jede Zeile kommt einmal dran, bei „Sätze am Stück“ > 1 entsprechend oft hintereinander.
   - Ein Filter, dessen Fenster gerade zu ist, wird übersprungen.
   - Öffnet das Fenster einer Zeile mit Vorrang, endet die laufende Zeile nach ihrer Aufnahme,
     und die Vorrang-Zeile läuft, bis ihr Fenster schließt.
   - Geprüft wird **vor jeder Aufnahme**. Eine laufende Aufnahme wird nie abgebrochen.
3. **Auslöser:** Meridianflip, Dither und AF vom Ziel-Container greifen zwischen den
   Aufnahmen.
4. **Ende ohne Uhrzeit:** Öffnet in dieser Nacht kein Fenster mehr, endet der Plan von selbst.
   Im Protokoll steht dann „no window left tonight, ending“. Spätestens bei Sonnenaufgang ist
   seine Nacht vorbei, dann endet er auch bei einer sehr lockeren Schwelle. Danach macht N.I.N.A. mit dem
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
  - Ziel: Kulmination, nutzbare Zeit, Mondabstand.
  - Himmel: der dunkelste Wert Richtung Ziel in dieser Nacht.
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
| Sehr viele Autofokus-Läufe | Siehe [Autofokus und Rundlauf](#die-felder): „Sätze am Stück“ erhöhen oder Fokus-Offsets nutzen. |
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
