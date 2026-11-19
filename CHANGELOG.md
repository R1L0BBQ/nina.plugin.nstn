# Änderungen

Neueste zuerst. Technik und Bauen: [docs/Entwicklung.md](docs/Entwicklung.md).

## 0.11.2 (19.11.2026)

**Für die Veröffentlichung auf GitHub und im Plugin-Manager.** Am Verhalten ändert sich nichts.
- Lizenz MPL-2.0. Repository, Lizenz, Changelog und Bilder stehen in den Assembly-Metadaten (N.I.N.A. zeigt sie auf der Plugin-Seite, das Manifest liest sie von dort). `THIRD-PARTY-NOTICES.txt` und `LIESMICH.txt` nennen Lizenz und Quelltext-Adresse.
- Der Name in N.I.N.A. ist jetzt „NSTN“ (vorher „NSTN – Narrowband Saves The Night“). Der Plugin-Manager installiert in einen Ordner mit diesem Namen, also in denselben Ordner `NSTN` wie Installer und Handinstallation; ein Wechsel lädt das Plugin nicht doppelt. Die Optionsseite hängt am Namen (`NSTN_Options`).
- Kurzbeschreibung gekürzt auf höchstens 256 Zeichen (Grenze des Manifest-Schemas).
- `NINA.Plugin.Nstn-<Version>.zip` enthält die Dateien jetzt ohne Ordner: Es ist das Archiv für den Plugin-Manager. Von Hand nach `Plugins\3.0.0\NSTN\` entpacken.
- Neu `dist\manifest.json` (`installer\New-Manifest.ps1`) und `ManifestTests` (Name = Installationsordner, Optionsseite, Repository-Adresse, Schema-Grenzen).
- Prüfprogramm: Die Sonnenaufgangs-Prüfung „geplant ab dem ersten Fenster“ las den Nachtplan, bevor die Hα-Vorhersage eingerechnet war (189/190 im ersten Endlauf, „Planned from 02:38“ statt 02:26). Sie wartet jetzt auf alle Vorhersagen und den daraus neu gerechneten Plan.

| Prüfung | Ergebnis |
|---|---|
| `build.ps1` | 267/267, neu 4 `ManifestTests` |
| `tests\Run-NinaChecks.ps1` gegen 3.2.0.9001 | 190/190 mit genau den DLLs aus `dist\NSTN` und den beiden ZIPs (Plugin `5def999c…`) |
| Installer mit Windows PowerShell 5.1, Wegwerf-`LOCALAPPDATA` | 16/16 |
| `dist\manifest.json` | Grenzen des Schemas eingehalten, Prüfsumme = `NINA.Plugin.Nstn-0.11.2.0.zip` (`3d217d77…`), LongDescription 5 389 Zeichen |
| Abgeschottete N.I.N.A. 3.2 | Plugins > Installiert: „NSTN“, Version 0.11.2.0, Homepage und Versionshistorie verlinkt, Optionsseite unter dem neuen Schlüssel vorhanden |

## Installer zu 0.11.1 (19.11.2026)

Plugin unverändert. Neu ist `dist\NSTN-Installer-0.11.1.0.zip` für Marcs weitere N.I.N.A.-Instanzen ([docs/Entwicklung.md](docs/Entwicklung.md#installer), Abschnitt „Installer“).
- `build.ps1` packt ihn über `installer\Pack-Installer.ps1`.
- `tests\Run-NinaChecks.ps1` prüft jetzt die DLLs aus `dist\NSTN` statt eines eigenen Builds. Bisher überschrieb der Build des Prüfprogramms `dist\NSTN` mit anderen Bytes derselben Quelle, sodass das ZIP aus `build.ps1` nicht die geprüften DLLs trug.
- Nachtrag: `THIRD-PARTY-NOTICES.txt` liegt neben den DLLs (in `dist\NSTN`, beiden ZIPs und im installierten Ordner). Das NuGet-Paket der Astronomy Engine nennt nur „MIT“ und das Copyright, einen Lizenztext liefert es nicht mit; die MIT-Lizenz verlangt aber, dass er jeder Kopie beiliegt.

| Prüfung | Ergebnis |
|---|---|
| `build.ps1` | 263/263 |
| `tests\Run-NinaChecks.ps1` gegen 3.2.0.9001 | 190/190. DLLs in `dist\NSTN`, im Prüfprogramm und im Installer-ZIP byte-gleich (Plugin `eeb590c0…`); `dist\NSTN` vor und nach dem Lauf gleich. Gegenprobe: `dist\NSTN` älter gestellt als die Quellen, dann bricht das Skript vor dem Prüflauf ab |
| Installer mit Windows PowerShell 5.1, Wegwerf-`LOCALAPPDATA` | 16/16: Erstinstallation, Aktualisierung mit Sicherung außerhalb von `Plugins\` (fremde Datei entfernt), beschädigtes Paket abgelehnt (Ziel unverändert), laufende N.I.N.A. abgelehnt, Deinstallation mit Sicherung und zweimal, Start aus PowerShell 7 ohne `.cmd`, `.cmd` ohne entpacktes Skript. Gegenprobe ohne BOM: Umlaute zerstört |

## 0.11.1 (19.11.2026)

**Plugin-Beschreibung in N.I.N.A. aktualisiert** (Plugins > Installiert > NSTN). Sie stand auf etwa 0.7.
- Lange Beschreibung: L/RGB-Zeile mit Verhältnis und Namenserkennung, „sets in a row“ statt „rounds per filter“, „Own settings“ mit Fenster und Kamera, Vorrang (übernimmt nach der laufenden Aufnahme, der obere gewinnt), Ende bei Sonnenaufgang und Nacht mit beiden Daten, Standardschwellen mit Warnung.
- Kurzbeschreibung nennt die L/RGB-Zeile; Schlagwort „LRGB“ ergänzt.
- Aufbau-Anleitung „The easy way“ (Plugin-Beschreibung und Nachtinfo, de/en) nennt L/RGB und Vorrang; `SequenceGuide.Easy` passend (TextTests vergleicht).
- Englische Grundbeschreibung des Filterplans (`ExportMetadata`) gleich `Desc_Plan`.

Geprüft: 263/263 Kerntests, 159/159 NINA-Prüfungen ohne Läufe (nur Texte geändert). Lange Beschreibung aus der gebauten DLL zurückgelesen. In der Testinstanz: Version 0.11.1.0, neue Kurzbeschreibung, Beginn der Beschreibung mit der Liste. Weiter unten ließ sich die Seite per UI Automation nicht scrollen.

## 0.11.0 (19.11.2026)

**Kamerawerte je Zeile** (Marcs Wunsch; Entscheidungen: Gain, Offset, Binning; bei L/RGB getrennt für L und RGB; im Aufklappbereich).
- Der Knopf heißt jetzt „Eigene Werte“. Aufgeklappt stehen die Zeile **Fenster** (Vorrang, Schwelle, Mindesthöhe, Zeitspanne) und die Zeile **Kamera** (Gain, Offset, Binning). Bei L/RGB gibt es einen Satz für L und einen für R, G und B.
- Leer übernimmt den Wert aus „Für alle Aufnahmen“; −1 heißt Wert der Kamera. Binning „2x2“, „2X2“ oder „2“; Unlesbares behält den alten Wert.
- Ändert sich ein Planwert, behalten Zeilen mit eigenem Wert ihren.
- Kurzfassung neben der Zeile und in der Gliederung, etwa „Gain 100, Offset 30, 2x2“ oder „L: Gain 0 · RGB: 2x2“.
- Gespeichert werden `Gain`, `Offset`, `Binning`, `RgbGain`, `RgbOffset`, `RgbBinning` nur, wenn gesetzt. Pläne ohne eigene Werte bleiben byte-gleich (Golddatei unverändert).
- Strukturtext: Eine Serie mit abweichenden Werten trägt „ @g100 o30 2x2“. Damit erkennt der Plan auch weiterhin von Hand geänderte Aufnahmen und baut sie neu.

| Prüfung | Ergebnis |
|---|---|
| `build.ps1` | 263/263 |
| `tests\Run-NinaChecks.ps1` gegen 3.2.0.9001 | 190/190. Neu: Aufbau mit eigenen Werten (Ha; L und RGB getrennt), Take Exposure trägt sie, Kurzfassung und Gliederung, Planwert ändert nur Zeilen ohne eigenen Wert, ungültige Eingaben, leeres Binning, Speichern/Laden, keine Felder ohne eigene Werte, Rückweg zur Einzelzeile. **Lauf H:** Die Kamera bekommt für Hα Gain 100, Offset 30, 2x2, für O III die Planwerte. Gegenprobe ohne die eigenen Werte: 4 Prüfungen fallen |
| Abgeschottete N.I.N.A. 3.2 | Aufgeklappt (Zwischenfassung mit aufgeklappten Zeilen, danach zurückgebaut): Fenster- und Kamerazeile, bei L/RGB „L Gain/Offset/Binning · RGB Gain/Offset/Binning“; Beschriftungen 7,4:1 |

**Zwischenfall beim Prüfen:** Die Gegenprobe ersetzte zwei Zeilen in derselben Datei. Das Prüfskript sicherte beim zweiten Mal den schon halb mutierten Text, und der Rückbau meldete fälschlich „byte-gleich“. Die Gain-Mutante blieb im Quelltext, ebenso in der Zwischenfassung für das Bild (das Layout betrifft sie nicht). Der Abschlusslauf auf genau diesem Stand fiel rot aus (5 Prüfungen); behoben und erneut geprüft: 190/190. Paket-DLL und geprüfte DLL sind identisch (SHA-256 `4874a817…`). Nichts davon war installiert oder committet.

## 0.10.1 (19.11.2026)

**Nachtinfo: Balken und Zeitachse lagen gegeneinander verschoben.** Marc sah das Hα-Fenster im Diagramm ab etwa 23:40, laut Text und Plan öffnete es um 00:02.
- Die Fenster selbst waren richtig berechnet; falsch war nur die Lage der Balken.
- Gemessen in der Testinstanz: Höhe, Mondabstand, Himmel und Stundenachse lagen bei x = 444–924, die Balkenzeilen bei 434–913. 10 px sind bei dieser Achse etwa 20 Minuten.
- Ursache, ohne N.I.N.A. nachgestellt (WPF-Raster 100 / 480 / Auto): Die Balkenzeilen überspannen alle drei Spalten. Ist ihre Zusammenfassung („74 % beleuchtet“, „10 h 02 min“) breiter als die rechte Spalte, verteilt WPF den Überschuss auf alle überspannten Spalten, auch die festen (106,8 / 486,8 statt 100 / 480). Die 480-px-Diagramme lagen dann mittig in der breiteren Spalte. Mit längeren Texten wären es bis zu 63 px gewesen.
- Korrektur: Die rechte Spalte von Diagrammraster und Balkenzeilen teilt sich eine Breite (`SharedSizeGroup`, `Grid.IsSharedSizeScope`). Nachgestellt: 100 / 480 in allen Fällen. In N.I.N.A. gemessen: alles bei 434–913.
- Neuer Test `XamlTests.ASpanOverFixedColumnsKeepsThemFixed`: Ein Element über feste Spalten und eine Auto-Spalte braucht eine gemeinsame Breitengruppe, auch in seinem Inneren, und einen Bereich, der beide umfasst. Gegenproben ohne Bereich und ohne die Gruppe in den Zeilen schlagen fehl.
- Der Nachtplan im Filterplan hat dieses Muster nicht: Achse und Zeilen sind getrennte Raster.

| Prüfung | Ergebnis |
|---|---|
| `build.ps1` | 263/263 |
| `tests\Run-NinaChecks.ps1 -NoRuns` | 145/145 (nur die Vorlage geändert; die Läufe sind unverändert seit 0.10.0, 174/174) |

## 0.10.0 (19.11.2026)

**Anlass:** In Marcs Plan (L, R, G, B je 2 × 120 s; Ha, S2, O3 je 5 × 300 s; Sätze am Stück 2) bekam L/RGB nichts, obwohl sein Fenster 02:56–04:12 offen war. Eine Zeile wird nur geprüft, wenn die Runde bei ihr ankommt; eine Runde mit drei offenen Schmalbandzeilen dauerte gut 2,5 h. Das LRGB-Fenster öffnete und schloss mitten in der Runde ab 02:05. Der Nachtplan zeigte das korrekt (0 s).

**Eine Zeile L/RGB** (Marcs Entwurf): In der Filterliste einer Zeile steht oben „L/RGB“. Die Zeile fasst L, R, G und B zusammen:
- Verhältnis L:R:G:B je Runde, z. B. 3:1:1:1 oder 0:1:1:1 (0 = Filter weglassen).
- Belichtungszeit getrennt für L und für RGB.
- Ein Block mit SwitchFilter + Serie je Filter, beurteilt vom Breitband-Fenster (explizit, nicht „Filter dieses Blocks“, der vier Filter nicht kennt).
- Welche Profilfilter L, R, G, B sind, erkennt NSTN am Namen (`FilterAssignments.LrgbChannel`: ganze Wörter L/Lum/Luminance/Clear, R/Red/Rot, G/Green/Grün, B/Blue/Blau). Namen mit Linie oder Produkt fallen heraus („L-eXtreme“), ebenso Filter, die in den Optionen einer Schmalbandlinie zugeordnet sind. Fehlt einer, meldet der Plan, welche Namen er sucht.
- Gespeichert werden `Ratio` und `RgbExposureSeconds` nur in L/RGB-Zeilen; bestehende Pläne bleiben byte-gleich.

**Vorrang übernimmt sofort** (Marcs Entscheidungen): Öffnet das Fenster einer Vorrang-Zeile, endet die laufende Zeile nach ihrer Aufnahme, auch mitten in der Runde. Solange es offen ist, läuft nichts anderes. Bei zwei offenen Vorrang-Zeilen läuft die obere.
- Umsetzung: Die NSTN-Bedingung eines Blocks prüft vor jeder Aufnahme zusätzlich `NstnFilterPlan.MustYield` (ein offener Vorrang-Block weiter oben). Vorrang-Blöcke stehen zuerst und haben keine Iterationsgrenze.
- Der Nachtplan rechnet genauso (`PlanBlock.Preempts`).

| Prüfung | Ergebnis |
|---|---|
| `build.ps1` | 262/262. Neu: Kanalerkennung am Namen (16 Fälle), Simulation „Vorrang übernimmt nach der laufenden Aufnahme“ (Marcs Fall nachgestellt) und „von zwei Vorrängen gewinnt der obere“. Gegenprobe ohne Vorrang in der Simulation: beide fallen |
| `tests\Run-NinaChecks.ps1` gegen 3.2.0.9001 | 174/174. Neu: Aufbau, Speichern/Laden und Bezeichnung der L/RGB-Zeile, 0:1:1:1, unlesbares Verhältnis, Rückweg zur Einzelzeile, kein schmalbandiger Filter als L. **Lauf F:** O III (60 × 1 s) läuft, Hα mit Vorrang ab 05:41 übernimmt um 05:41:00 nach der laufenden Aufnahme, danach nur Hα. **Lauf G:** L/RGB 3:1:1:1 mit Vorrang belichtet LLLRGB LLLRGB …, Hα wartet |
| `-WriteSequence` | Beispielsequenz jetzt mit L/RGB 3:1:1:1 (L 60 s, RGB 120 s) mit Vorrang und Ha; lädt byte-gleich, Sequenzprüfung ohne Meldung |

**Angesehen** in der abgeschotteten N.I.N.A. 3.2 mit einer Kopie von Marcs Arbeitsprofil (deutsch): Die Zeile zeigt „L/RGB | 3:1:1:1 | L 60 s | RGB 120 s | Vorrang“; Nachtplan „Geplant ab 15:54: L/RGB 9 h 00 min, H-Alpha 6 nm 2 h 35 min“. Hα läuft nur, solange L/RGB zu ist. Beschriftungen L und RGB 7,4:1.

**Grenze:** Wird ein L/RGB-Durchgang vom schließenden Fenster unterbrochen, beginnt der nächste wieder mit L. Bei häufigen Unterbrechungen bekommt L etwas mehr als sein Anteil.

## 0.9.0 (19.11.2026)

**Die kommende Nacht nach Sonnenaufgang.** Am 19.11. um 08:58 zeigte der Plan „Heute Nacht (18. Nov.)“ und plante 0 s je Filter. Bis 12 Uhr galt die vergangene Nacht, obwohl sie vorbei war.
- `WindowForecaster.NightFor`: bis Sonnenaufgang die laufende Nacht, danach die kommende. Ohne Sonnenaufgang (Polarnacht, Mitternachtssonne) bleibt die Mittagsregel.
- Sonnenaufgang auf dem Zwei-Minuten-Raster des Modells: Die Rasterhöhen enthalten schon die Refraktion, deshalb gilt −0,27° (Sonnenradius), nicht −0,833°. Abweichung zur Astronomy-Engine unter einem Rasterschritt.
- **Ein Lauf behält seine Nacht.** Die Endebedingung des Filterplans merkt sich die Nacht ihrer ersten Prüfung im Lauf; ist sie vorbei, endet der Plan („Die Nacht 18./19. Nov. ist vorbei.“). Der Warte-Befehl gibt nach Sonnenaufgang ebenso auf. Ohne das würde ein Plan, der bei Sonnenaufgang noch belichtet (etwa mit sehr lockerer Schwelle), bis zum Abend warten, während die Montierung nachführt.
- Steht die Sonne über dem Horizont, zeigt die Zeile „Sonne über dem Horizont“ statt eines negativen Himmelswerts.

**Texte**, auf Marcs Durchsicht hin:

| Stelle | bisher | jetzt |
|---|---|---|
| Feld | Runden je Filter | Sätze am Stück |
| Abschnitt | Heute Nacht · „Heute Nacht (18. Nov.): 20:26–05:28 · 9 h 02 min“ | Überschrift „Nacht 19./20. Nov.“, je Zeile „offen 20:26–05:28 (9 h 02 min)“ |
| Zustand | Zu für den Rest der Nacht · Himmel im Band -3,99, nötig 2,40 | Zu bis Nachtende · Himmel im Band 25,2 mag/arcsec², Schwelle 23,1 |
| Warten | Beobachtet H-Alpha, S II, O III · … | Wartet auf ein Fenster von H-Alpha, S II, O III · … |
| Nachtplan | Plan ab 08:58: H-Alpha 6 nm 0 s, … · Ende 08:58, kein Fenster öffnet mehr | Geplant ab 18:32 (erste Aufnahme, nicht „jetzt“): … · Ende 05:28: das letzte Fenster schließt; ohne Aufnahmen „Keine Aufnahmen mehr in dieser Nacht.“ |
| Ablauf | Ha: 5 × 300 s, solange sein NSTN-Fenster offen ist, einmal je Runde | Ha: 5 × 300 s je Runde, nur bei offenem Fenster |
| Ablauf | Jede Aufnahme: Gain Kamerastandard, Offset Kamerastandard, Binning 1x1. | Gain und Offset der Kamera, Binning 1x1. |
| Ende | Heute Nacht öffnet kein NSTN-Fenster mehr – fertig. | In dieser Nacht öffnet kein Fenster mehr. |
| Nachtinfo | Die Nacht für dieses Ziel – kostet beim Ablauf der Sequenz keine Zeit | Die Nacht für dieses Ziel |
| Nachtinfo | Mondabstand … · Mond davon 5 h 41 min über dem Horizont · dunkelster Himmel … | Mondabstand …, solange das Ziel nutzbar ist; der Mond steht davon 5 h 41 min über dem Horizont. Eigene Zeile „Himmel“ |
| Optionen | Schwelle Schmalband (im Band) · Standard 23,1 | Schwelle Schmalband (Himmel im Band) · Standard 23,1 · höher heißt: dunklerer Himmel nötig; Warnung unter 18 |

Die englischen Texte sind entsprechend nachgezogen.

**Befund aus Marcs N.I.N.A.:** Aktiv war das Profil „Standard-2026-11-18T13:07:34“ mit Breite 0 und Länge 0, nicht „Standard“ (49,8° N, 6,6° O). Es ist am 18.11. um 13:07 entstanden, als eine zweite N.I.N.A.-Instanz startete (Falle aus den Testläufen, siehe Wissensspeicher). Darin steht die Schmalband-Schwelle 2,4. Daher kamen Sonnenuntergang 18:48 und die gleichen Fenster für Ha, S2 und O3. In der Testinstanz (Kopie dieses Profils, Standort auf Kordel gesetzt) öffnet Hα mit 2,4 schon um 15:54, vor Sonnenuntergang; deshalb warnen die Optionen jetzt unter 18.

**Beispielsequenz** jetzt auch deutsch: `samples/NSTN-LRGB-Ha.de.json` (deutsche Anmerkungen und Namen). Die englische verlangt jetzt 0.9.0.

| Prüfung | Ergebnis |
|---|---|
| `build.ps1` | 244/244. Neu: Sonnenaufgang auf dem Raster gegen die Astronomy-Engine, Nachtwechsel davor/danach/mittags/abends, Polarnacht und Mitternachtssonne behalten die Mittagsregel, Nachtbeschriftung de/en. Gegenprobe mit der alten Mittagsregel: 2 Tests fallen |
| `tests\Run-NinaChecks.ps1` gegen 3.2.0.9001 | 154/154. Neu „Sunrise“: um 08:58 die kommende Nacht in Überschrift, Nachtplan („geplant ab“ der ersten Aufnahme) und Zeilen ohne Tageslicht-Himmelswert; um 14:00 „Sonne über dem Horizont“; Lauf E über Sonnenaufgang (Schwelle 2,4) endet mit „The night of 18/19 Nov is over.“; ein Warte-Befehl kurz vor Sonnenaufgang gibt nach 60 s auf. Gegenprobe ohne Festhalten der Nacht: Lauf E wartet in den Tag (Zeitlimit), der Warte-Befehl wird nach 100 s abgebrochen |
| `-WriteSequence` | beide Beispielsequenzen laden und speichern byte-gleich, keine unbekannten Typen |
| Abgeschottete N.I.N.A. 3.2, Kopie von Marcs Arbeitsprofil (Standort auf Kordel gesetzt), 09:30 | „Nacht 19./20. Nov.“, „offen 18:32–04:34 (10 h 02 min) / Zu, öffnet 18:32“, „Sätze am Stück“, „Geplant ab 15:54: H-Alpha 6 nm 12 h 20 min · Ende 04:38: das letzte Fenster schließt“; Nachtinfo mit „Nacht 19./20. Nov.“, Zeile „Himmel“, Diagramm „SQM 21,0“ (Komma: der Punkt aus Marcs Bild ließ sich nicht nachstellen). Überschrift 7,4:1, Zweitzeilen 5,3:1 |

## 0.8.1 (19.11.2026)

**Schrift der Abschnittsüberschriften und Zweitzeilen lesbar.** In 0.8.0 waren „Für alle Aufnahmen“, „Heute Nacht“, der Zustand jetzt und die Beobachtungszeile auf dunklem Theme schwarz.
- Ursache: Ein eigener TextBlock-Stil (`NstnPlanHeading`, `NstnPlanSecondary`) ersetzt N.I.N.A.s impliziten Stil `StandardTextBlock` und damit dessen `Foreground = PrimaryBrush`. Ohne eigene Farbe fällt die Schrift auf Schwarz zurück.
- Die Stile setzen jetzt `Foreground` = `{DynamicResource PrimaryBrush}`. `BasedOn` per `StaticResource` wäre die Alternative, kann aber beim Laden des Plugins scheitern, und das fiele in keinem Test auf.
- Dieselbe Lücke in `Options.xaml` (Hinweis „keine Filter im Profil“) ist mit behoben.
- Gemeldet von Marc auf seinem Theme „Persian Faint“ (Schrift #BDC3C7, Hintergrund #263238). Dessen `SecondaryColor` #1D2731 ist fast so dunkel wie der Hintergrund und taugt nicht als Schriftfarbe; Zweitzeilen bleiben `PrimaryBrush` mit 80 % Deckkraft.

| Prüfung | Ergebnis |
|---|---|
| `build.ps1` | 241/241. Neu `XamlTests`: Jeder TextBlock-Stil braucht `BasedOn` oder einen eigenen `Foreground`. Die Gegenprobe ohne die beiden Setter fällt. Ein zweiter Test belegt, dass die Prüfung die Plan-Stile überhaupt sieht |
| `tests\Run-NinaChecks.ps1` gegen 3.2.0.9001 | 144/144 mit Läufen |
| Abgeschottete N.I.N.A. 3.2 mit Marcs Profil (Persian Faint), Kontrast im Bild gemessen | Überschriften #BDC3C7 auf #263238, 7,4:1, wie normaler N.I.N.A.-Text; Zweitzeilen 5,3:1. Zum Vergleich in 0.8.0 (Marcs Bildschirmfoto): #000000, 1,6:1 |

Beim Ansehen von 0.8.0 war das durchgerutscht, weil nur nach Augenmaß geprüft wurde. Seitdem wird der Kontrast im Bildschirmfoto gemessen.

## 0.8.0 (18.11.2026)

**Eigenes Fenster je Filterzeile.** Unter „Eigenes Fenster“ hat jede Zeile vier Einstellungen:
- Vorrang: läuft zuerst, ohne Iterationsgrenze, und behält ihr Fenster.
- Schwelle: Abweichung zur Schwelle der Optionen in mag/arcsec².
- Mindesthöhe.
- Zeitspanne: „nicht vor“ und „nicht nach“ als HH:mm.

**Umsetzung:**
- Zeile und Fensterbedingung speichern die Werte nur, wenn sie gesetzt sind. Pläne ohne sie bleiben byte-gleich, die Golddatei ist unverändert.
- Der Kern wendet sie über `WindowLimits` an: Schwelle und Höhe gehen in die `WindowSettings`, die Zeitspanne schneidet die Segmente.
- Die Sequenzprüfung der Nachtinfo gibt bei Vorrang-Zeilen keinen Rat mehr, „Loop For Iterations“ zu ergänzen.
- Das Aufklappen von „Eigenes Fenster“ baut den Plan nicht neu auf.

| Prüfung | Ergebnis |
|---|---|
| `build.ps1` | 239/239 Kerntests, darunter `WindowLimitsTests` (Schwelle, Höhe, Zeitspanne über Mitternacht, Einlesen der Uhrzeit, Vorhersage nur enger) und eine Vorrang-Zeile ohne Prüfbefund |
| `tests\Run-NinaChecks.ps1` gegen 3.2.0.9001 | 144/144 mit Läufen. Neu: Reihenfolge und Struktur mit Vorrang, Grenzen an der Bedingung, Speichern und Laden, keine Felder ohne eigenes Fenster, Vorhersage innerhalb 22–02 Uhr, kein Vorrang-Rat im Nachtplan. Gegenproben: Ignoriert die Bedingung die Grenzen, fällt die Prüfung (204 min statt innerhalb 22–02 Uhr); mit dem alten Nachtplan-Rat fällt die Prüfung dazu |

**Ansicht:**
- Der Plan zeigt vier Abschnitte: Filter je Runde, Für alle Aufnahmen, Ablauf und Heute Nacht.
- Die Gliederung ist eingeklappt; die Endezeile steht immer daneben.
- „Heute Nacht“ ist eine ausgerichtete Tabelle.
- „Eigenes Fenster“ ist ein normaler N.I.N.A.-Knopf. Ein ToggleButton bleibt im N.I.N.A.-Theme unlesbar, weil N.I.N.A. ihn nicht gestaltet.
- „Vorrang“ ist eine Beschriftung mit N.I.N.A.-Schalter.
- Im Filterplan gibt der Nachtplan bei Vorrang-Zeilen keinen Rat mehr, „Loop For Iterations“ zu ergänzen.

**Gefunden beim Ansehen in der Testinstanz:** Der Schlüssel `Plan_Priority` stand doppelt in den Sprachtabellen, die Beschriftung zeigte deshalb den Satz des Nachtplans. Neuer Test `NoKeyIsWrittenTwice`; die Gegenprobe mit einem doppelten Schlüssel fällt.

**Angesehen** in der abgeschotteten N.I.N.A. 3.2 mit deinem Profil (deutsch): Zeilen aufgeklappt, alle vier Abschnitte. Das Aufklappen per Klick selbst ist nicht angesehen. Dafür lief eine Zwischenfassung, in der Zeilen und Ablauf schon aufgeklappt starteten, weil sich die Testinstanz nicht anklicken ließ, ohne dir den Fokus zu nehmen.

## 0.7.2 (18.11.2026)

- **Nachtplan und Nachtinfo ziehen nach.** Beim Einfügen eines Filterplans und nach jeder Zeilenänderung zeigen beide sofort alle Zeilen, nicht erst nach ihrer 20-s-Aktualisierung.
  - Erste Ursache: Eine neue Fensterbedingung rechnet zum ersten Mal, bevor ihr Block im Ziel hängt. Dieses Ergebnis ohne Vorhersage wurde 2 s lang wiederverwendet. Jetzt wird nur noch eine echte Vorhersage wiederverwendet.
  - Zweite Ursache: Der Warte-Befehl plante, bevor die Blöcke hinter ihm standen. Jetzt stößt der Plan nach dem Neuaufbau seinen Nachtplan und die Nachtinfo im Ziel an.
- **Filtersätze nehmen die Linie aus dem Namen.** Bei „Astronomik (6 nm)“ wird „S2“ zu S II und „O3“ zu O III; bisher blieb es bei Hα. Eine gespeicherte Zuordnung, die dem Namen widerspricht, zeigt „prüfen: der Filtername nennt …“.

| Prüfung | Ergebnis |
|---|---|
| `build.ps1` | 222/222 Kerntests, neu: Linie aus dem Filternamen |
| `tests\Run-NinaChecks.ps1` gegen 3.2.0.9001 | 130/130 mit Läufen. Neu: Nachtplan und Nachtinfo nach dem Hinzufügen von Zeilen, Linie aus dem Namen und Hinweis bei Widerspruch. Gegenproben: ohne jede der beiden Korrekturen fallen die zugehörigen Prüfungen |

## 0.7.1 (18.11.2026)

Neu sind die Beispielsequenz `samples/NSTN-LRGB-Ha.json` (siehe [Beispiele](docs/Entwicklung.md#beispiele)) und ein
eigener Text für Breitband, wenn ein Fenster an diesem Standort nie öffnet. Bisher nannte die
Meldung auch für L/RGB „7 nm“ und riet zur Schmalband-Schwelle.

| Prüfung | Ergebnis |
|---|---|
| `build.ps1` | 213/213 Kerntests |
| `tests\Run-NinaChecks.ps1` gegen 3.2.0.9001 | 119/119; neu die Meldung „öffnet nie“ für Breitband und Schmalband an einer echten Bedingung im Ziel-Container. Die Gegenprobe mit dem alten Text fällt. |
| `tests\Run-NinaChecks.ps1 -WriteSequence` | Beispielsequenz: Laden und erneutes Speichern ergeben dieselbe Datei, keine unbekannten Typen |
| Testinstanz 3.2.0.9001 mit 0.7.1 und einer Profilkopie (Filter L, R, G, B, S2, Ha, O3) | Beispielsequenz geladen, keine Fehler oder Warnungen im Protokoll, Filterplan passt zu seinen Zeilen. Angesehen wurde die Ansicht mit dem 0.7.0-Build: alle Teile von Start bis Ende vorhanden |

## Getestet (18.11.2026, 0.7.0, NINA-Testinstanzen mit Alpaca-Simulatoren)

| Prüfung | Ergebnis |
|---|---|
| `build.ps1` | 213/213 Kerntests |
| `tests\Run-NinaChecks.ps1` gegen 3.2.0.9001 | 115/115, darunter Golddatei 3.2 und vier Läufe mit SequentialStrategy |
| Abnahme 3.2.0.9001 (Testinstanz, NSTN-Uhr 05:40, Plan Rundlauf mit Night Info, Ziel 6 min vor dem Meridian) | Meridian-Flip echt ausgeführt (pierWest → pierEast), danach weiter belichtet; 30 Aufnahmen, 13 × AF nach Filterwechsel, 29 × Dither erfolgreich; Night Info im Lauf ausgeführt (Protokollzeile) |
| Abnahme 3.3.0.1069 (ausgepackt, dasselbe net8-Build 0.7.0, gleicher Lauf) | Meridian-Flip echt ausgeführt; 30 Aufnahmen, 13 × AF, 29 × Dither erfolgreich; Night Info ausgeführt |

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
| M31-NSTN-Block.json per `--sequencefile` | lädt, N.I.N.A. bleibt bedienbar; Block „Askar D1": „Block filter: Askar D1 → OIII 6 nm", Fenster 22:18–04:30; Nachtplan rechnet mit O III 6 nm / Hα 7 nm / L/RGB |
| Filter im Switch Filter umstellen (Askar D1 → Ha → L) | Band folgt in ≤ 30 s: H-alpha 656 nm, 7 nm, 18:48–03:44; dann L/RGB, 00:06–02:10 |

**Night Info 0.5.0 (18.11.2026, `--sequencefile`, M 31 bei 49,8° N, SQM 20,9):**

| Prüfung | Ergebnis |
|---|---|
| M31-NSTN-Plan.json | Eckdaten plausibel (Untergang 16:47, Dunkel 18:44–05:53, Mond 55 %, untergeht 00:02, Kulmination 21:30 bei 82°), Fenster = Bedingungen (Hα 18:48–03:44, O III 23:56–03:44, L/RGB 00:06–02:10); Prüfung: 2 Hinweise (L/RGB ohne Iterationsgrenze, kein Meridian-Flip-Trigger) |
| M31-NSTN-mistakes.json | 2 Warnungen (Block ohne Aufnahme, Schleife ohne Warte-Befehl), 4 Hinweise (2 × Vorrang, lose Aufnahme, Meridian-Flip) |
| Tooltip, Leitfaden aufklappen, Laden ohne Verklemmung | ok |
| Ausführen des Befehls im Sequenzlauf | **nicht geprüft** (nur Protokollzeile, kein Kamera-Lauf) |

**Übersetzung 0.6.0 (18.11.2026, Profil `de-DE` und `en-GB`):**

| Prüfung | Ergebnis |
|---|---|
| Start mit deutschem Profil (M31-NSTN-Plan.json) | Nachtinfo, Warte-Befehl, Nachtplan, Bedingung, Auswahllisten, Optionsseite auf Deutsch; „Schleife für Iterationen“, „Meridianflip“, „Optionen > Allgemein > Astrometrie“ aus N.I.N.A.; „17. Nov.“, „20,8 mag/arcsec²“ |
| Live-Wechsel Deutsch → Englisch → Deutsch | alle Plugin-Texte folgen ohne Neustart. Befund: Name der Bedingung und gewählter Eintrag der Filterauswahl blieben zunächst in der alten Sprache (N.I.N.A.s `SequenceCondition.Name` meldet keine Änderung, Auswahl-Einträge ohne Änderungsmeldung) – behoben |
| N.I.N.A.s eigene, schon geladene Befehle | behalten beim Live-Wechsel ihren alten Namen – Verhalten von N.I.N.A., nicht des Plugins |

**Zwei Fehler aus dem Test vom 17.11., in 0.4.0 behoben:**

- *Verklemmung beim Laden einer Sequenz im Block-Modus.* N.I.N.A. 3.2 schützt alle
  Container mit **einer statischen Sperre** und ruft `Validate()` der Elemente unter dieser
  Sperre auf; der Warte-Befehl fragt dabei die Bedingung. Die Bedingung las im
  Hintergrund-Refresh die Elemente ihres Blocks (`GetItemsSnapshot`, nimmt die
  Container-Sperre) **innerhalb** ihrer eigenen Sperre – überkreuzte Sperrreihenfolge.
  Folge: N.I.N.A. nahm keine Eingaben mehr an, meldete sich aber als „Responding" (ein
  wartender STA-Thread pumpt nur noch einen Teil der Nachrichten). Jetzt wird alles aus den
  Containern **vor** der eigenen Sperre gelesen.
- *Leere Bandauswahl nach Filterwechsel.* Die Bandliste war bei jedem Abruf eine neue
  Liste; eine WPF-ComboBox setzt dann ihre Auswahl zurück. Jetzt gibt es je Katalogfilter
  genau eine Liste, im Block-Modus immer die vollständige.

0.1.1 behebt einen Befund aus diesem Test: 0.1.0 schrieb bei Leerdurchläufen je
Bedingung und Prüfung eine Protokollzeile (2000 Zeilen je 1000 Durchläufe); jetzt nur bei
einem echten Zustandswechsel. N.I.N.A. selbst schreibt weiter 4 Zeilen je übersprungenem
Block – ein Grund mehr für den Warte-Befehl.
