# SIMATIC SD: LAD-, FBD- und SCL-Netzwerke als Text (`plc_read`, `plc_write`)

**Sprache:** [English](PLC_SIMATIC_SD_SUMMARY.md) · Deutsch

Teilweise live getestet auf TIA Portal V21 am 2026-11-02 ([Live-Bericht](../superpowers/acceptance/reports/2026-11-02-simatic-sd-and-safety-live.md), E1-E7): `get_block_content` sd,
`update_block_logic` sd mit einem FB aus 15 LAD/FBD/SCL-Netzwerken, `insert_network`/`replace_network`/`delete_network` verkettet in
einem Aufruf und `create_block_from_document` (mit `de-DE`-Texten; mit einer `en-US`-Kultur, die das Projekt nicht hat, fehlgeschlagen) liefen
live; ein erneuter Test mit `b79b2df` am selben Tag bestätigte die optionale `.s7res` (E5) und zeigte die Befunde L1-L4 zum Main-Safety-Baustein.
Alles andere und jede Änderung, die nach diesen Läufen gemacht wurde (der Hinweis zur Kultur, die Route für den Main-Safety-Baustein,
die D15-Warnung), ist nur offline verifiziert (Unit-Tests über echte V21-Exporte und das FakeWorker-Szenario `plc-write-roundtrip`).

SIMATIC SD ist das Bausteindokumentformat von TIA Portal V21: `PlcBlock.ExportAsDocuments` schreibt einen Baustein als `.s7dcl`-Dokument
(Header-Pragma, Deklaration, Schnittstelle, Netzwerke) und als `.s7res`-Dokument (die mehrsprachigen Texte), und
`PlcBlockComposition.ImportFromDocuments(DirectoryInfo, string, ImportDocumentOptions.Override)` importiert sie. Der
Siemens-V21-Skill `simatic-sd` (MIT, Siemens AG, `openness_development/skills/simatic-sd/SKILL.md`) nennt das „the
correct path for adding LAD/FBD/SCL networks to existing blocks via Openness“. Diese Operationen machen daraus einen Bearbeitungsweg auf
Netzwerkebene, sodass ein Agent LAD- (KOP), FBD- (FUP) und F-LAD/F-FBD-Logik ohne einen Umweg über SimaticML (FlgNet-XML)
ändern kann. Es wurde kein Tool hinzugefügt (11/30/30 bleiben); jede Operation ist eine `plc_read`- oder `plc_write`-Operation.

Für die LAD/FBD-Syntax selbst, mit Belegen pro Konstrukt, siehe [LAD und FBD als SIMATIC SD schreiben](../guides/lad-fbd-simatic-sd.de.md).

## Operationen

| Operation | Tool | Felder | Was sie tut |
| --- | --- | --- | --- |
| `get_block_content` `format: "sd"` | `plc_read` | `blockPath` | Nur die SIMATIC-SD-Dokumente von `ExportAsDocuments` (`.s7dcl` zuerst, `.s7res`, wenn TIA Portal eine schreibt), abgegrenzt wie das xml-Bundle (`--- FILE: Name.s7dcl ---`). `contentHash` ist `sd:sha256:<hex>` über diesen Text, berechnet wie bei den anderen Formaten und gleich dem, was der nächste Lesezugriff liefert. `failSafe: true` erscheint, wenn der `.s7dcl`-Header `S7_Safety := "TRUE"` deklariert. Was TIA Portal nicht exportiert, schlägt mit dessen Fehler oder Ergebniszustand fehl; nichts ist strenger. |
| `list_networks` | `plc_read` | `blockPath` | Host-Berechnung über `get_block_content sd`: `blockKind`, `blockName`, `preferredLanguage`, `failSafe`, jeder Eintrag des Header-Pragmas (`S7_PreferredLanguage`, `S7_Safety`, `S7_BlockNumber`, ... keiner weggelassen) und pro Netzwerk `number` (1-basiert, Dokumentreihenfolge), `language` und `languageSource` (`pragma` aus `S7_Language`; `detected` nach §5 des Siemens-Skills; null, wenn mehrdeutig), jeder Pragma-Eintrag, `titleId`/`title` und `commentId`/`comment` (`S7_NetworkTitle`/`S7_NetworkComment`-IDs aus der `.s7res` aufgelöst, ein Text pro Kultur), `code` (der Rumpf ohne seine gemeinsame Einrückung) und `exportOnlyForms` (siehe [Export- und Importformen](#export--und-importformen)). `contentHash` ist der sd-Hash. |
| `update_block_logic` `format: "sd"` | `plc_write` | `blockPath`, `content`, `expectedContentHash` (sd) | Importiert ein sd-Bundle mit `ImportFromDocuments(..., Override)`: genau eine `.s7dcl`, die den Zielbaustein deklariert, optional ihre `.s7res`, keine `.xml`. Ein veralteter Hash ist `state_changed`; ein anderer deklarierter Name ist ein `validation_error` (update_block_logic benennt nie um und legt nie an). Die ältere implizite Route (Format xml mit einem Bundle ohne `.xml`) ist unverändert. |
| `insert_network` | `plc_write` | `blockPath`, `code`, optional `language`, `position`, `expectedContentHash` (sd) | Fügt ein Netzwerk vor Netzwerk `position` ein (1-basiert; eins hinter dem letzten oder weggelassen hängt an) mit dem Pragma `{ S7_Language := "<language>" }`. |
| `replace_network` | `plc_write` | `blockPath`, `networkNumber`, `code`, optional `language`, `expectedContentHash` (sd) | Ersetzt den Rumpf eines Netzwerks. Sein Pragma bleibt mit jedem Schlüssel erhalten (`S7_NetworkTitle`, `S7_NetworkComment`, unbekannte Schlüssel); nur der Wert von `S7_Language` ändert sich, wenn `language` abweicht. Eine Bearbeitung, die nichts ändert, wird abgelehnt. |
| `delete_network` | `plc_write` | `blockPath`, `networkNumber`, `expectedContentHash` (sd) | Entfernt ein Netzwerk mit seinem Pragma. Kommentarzeilen oberhalb des Pragmas bleiben. |
| `create_block_from_document` | `plc_write` | `targetGroupPath`, `content`, optional `overwrite` | Importiert ein sd-Bundle (`.s7dcl`, die den neuen Baustein deklariert, und optional seine gleichnamige `.s7res`) in eine deterministische Bausteingruppe (`PLC_1/Blocks`, `PLC_1/Blocks/Line`, wie `copy_block` sie annimmt). Die `.s7res` ist seit 2026-11-02 optional: TIA Portals eigener Export eines Bausteins ohne Texte hat keine (E1), und die frühere Ablehnung war strenger als TIA Portal (E5). Eine vorhandene `.s7res` wird strukturell geprüft (höchstens eine, gleicher Name), und die Meldungen des Elements listen ihre Kulturen auf: jede Kultur muss eine Projektsprache sein (E4, live: "Cannot import multilingual text with culture 'en-US' in File …s7res: Document option "Override" is not valid for Multilingual text."). Ein Baustein des deklarierten Namens irgendwo in der PLC lehnt das Element ab, außer bei `overwrite: true` (dann `Override`, Info-Guard `plc_sd_document_replaces_block`). |

### `code`

Die akzeptierten Formen sind die aus §6 des Siemens-Skills: ein RUNG-Rumpf (mehrere RUNGs, parallele Zweige mit
`wire#wN` und `END_RUNG wire#wN` eingeschlossen), ein vollständiges `NETWORK ... END_NETWORK` mit oder ohne das Pragma davor
(einzeilig `{ S7_Language := "LAD" }` oder der mehrzeilige Block) oder SCL-Anweisungen. Nur die äußeren Hüllen und das
`S7_Language`-Pragma werden entfernt; innere Pragmas wie `{ S7_Templates := "time_type := Time" }` und
`{ f_user_card := "1"; f_image_card := "0" }` bleiben; Leerzeilen werden entfernt (der Skill berichtet, dass eine zusätzliche Leerzeile
innerhalb eines RUNG den Import scheitern lässt; TIA Portal V21 selbst exportierte live am 2026-11-02 eine leere Zeile innerhalb eines RUNG vor `Move(`,
und dieser Export ließ sich wieder importieren, also ist die Regel ein Bericht). Die relative Einrückung des Aufrufers bleibt erhalten, und der Rumpf wird
eingerückt wie die Netzwerke des Dokuments. Den Code nicht in Anführungszeichen einschließen (§9).

`language` wird so geschrieben, wie angegeben (`LAD`, `FBD`, `SCL`, `STL`, ...); TIA Portal entscheidet. Weggelassen, nimmt `insert_network`
das eigene `S7_Language`-Pragma des Codes, sonst die Erkennung nach §5 (`RUNG wire#` ist LAD, `RUNG "..."` oder `RUNG <number>` ist
FBD, kein RUNG ist SCL); wenn keines zutrifft, ist es ein `validation_error`, der nach `language` fragt. `replace_network` behält
die Sprache des Netzwerks, wenn sie weggelassen wird.

Als `validation_error` abgelehnt, weil jedes davon das bearbeitete Dokument beschädigen würde: mehr als ein NETWORK in einem Code (§10),
unausgeglichene oder verschachtelte RUNG/END_RUNG, unausgeglichene NETWORK/END_NETWORK, NETWORK oder END_NETWORK nicht auf einer eigenen Zeile,
irgendetwas anderes als ein Pragma vor NETWORK oder irgendetwas nach END_NETWORK, ein `S7_Language` innerhalb des Rumpfs, ein
Sprachname, der kein Bezeichner ist (er wird in einen Pragma-Wert in Anführungszeichen geschrieben), und die Pragma-Schlüssel
`S7_NetworkTitle`/`S7_NetworkComment`: Titel und Kommentare sind `.s7res`-IDs, die Netzwerkbearbeitungen berühren die
`.s7res` nie (§10), und für literale Inline-Titel wird berichtet (1748477582/tia-portal-openness-mcp, nicht verifiziert), dass sie beim
Import scheitern. Texte anschließend mit `set_network_text` setzen. Eine Netzwerknummer oder Position außerhalb des Bausteins ist
`target_not_found`. Die Semantik von LAD/FBD/SCL wird nie beurteilt: Import und Übersetzen von TIA Portal entscheiden.

### Planung, Verkettung und Verifikation

Die Netzwerkbearbeitungen folgen `set_network_text`: Sie werden auf Host-Seite auf dem Export von `get_block_content sd` geplant
(`PlcSdNetworkEditPlanner`), und die Mutation importiert genau das geplante Bundle über `update_block_logic` Format sd
gegen den Hash des Exports. Mehrere Elemente auf einem Baustein in einem Aufruf werden in der Vorschau gegen das zuvor geplante Dokument berechnet und
tragen `dependsOn`; sie werden unmittelbar vor ihrem eigenen Import gegen den realen Zustand neu geplant, sodass ein späteres Element
die Netzwerke so nummeriert, wie die früheren Elemente sie hinterlassen haben. `expectedContentHash` ist optional (sd); er kann kein Dokument beschreiben, das ein
früheres Element des Aufrufs ändert, und wird dann abgelehnt. Eine Netzwerkbearbeitung eines Bausteins, den `create_block_from_document`
früher im Aufruf anlegt, wird geplant, wenn sie an der Reihe ist.

Die Verifikation exportiert den Baustein erneut in sd und meldet seinen `contentHash` (als nächster `expectedContentHash` verwendbar).
Die Prüfung `networkCode` ist erfolgreich, wenn der Import erfolgreich war und der erneute Export die erwartete Netzwerkanzahl hat und, bei
insert/replace, ein Netzwerk an der erwarteten Position in der erwarteten Sprache. Der übermittelte Code wird mit
dem erneut exportierten Netzwerk nach Normalisierung verglichen (Zeilen getrimmt, Leerzeilen entfernt) und in der Meldung als
`codeMatchesExport: true|false` gemeldet, mit beiden Texten in `expected`/`actual`: Für V21 wird berichtet, dass es manche Anweisungen in
einer Form exportiert, die von der Importform abweicht (unten; für die getesteten Anweisungen live nicht reproduziert), und TIA Portal
formatiert Leerraum um (eine leere Zeile vor `Move(`, die `Add`-Vorlage als mehrzeiliges Pragma, E2), also ist ein abweichender Text
eine Information, kein fehlgeschlagener Import. `delete_network`
meldet `remainingNetworksMatchExport`. Wenn die `.s7dcl`-Konstrukt-Fallstricke von V21 im geplanten Dokument gefunden wurden,
lässt ihr Vergleich mit dem erneuten Export den Eintrag fehlschlagen, wie es `s7dclConstructs` bei update_block_logic tut. Jeder
erfolgreiche Eintrag empfiehlt `compile_check`: Ein Import kann Instanz-DBs und Aufrufer inkonsistent hinterlassen (BetterTIA B2,
Info-Guard `plc_text_edit_reimports_block`). `create_block_from_document` wird durch einen erneuten Export des gemeldeten
Bausteins in sd verifiziert (Prüfung `createdBlock`: der erneute Export deklariert den Namen; `contentHash` sd).

### Fehlersichere Bausteine

Ein `.s7dcl`-Header `S7_Safety := "TRUE"` (der V21-Export eines fehlersicheren FB im BetterTIA-Beispiel `FB_Safety`)
kennzeichnet einen fehlersicheren Baustein für jeden SD-Schreibvorgang (`update_block_logic` sd, die drei Netzwerkbearbeitungen,
`create_block_from_document`), zusätzlich zu `ProgrammingLanguage F_*` und der SimaticML-Sprache. Die Regeln sind die
der xml-Route: Der Acknowledge-Guard `plc_safety_acceptance_required` löst aus (read-write fragt einmal, full läuft unter
Policy), der Block-Guard `plc_safety_login_required` stoppt den Aufruf nur, wenn das Sicherheitsprogramm ein Passwort hat und
die Sitzung nicht angemeldet ist (`safety_login` früher im Aufruf zählt), und das Worker-Gate
(`PlcSafetyGate.RequireImportPermitted`) prüft vor dem Import erneut. `create_block_from_document` mit
`overwrite` zählt auch einen existierenden F-Baustein des deklarierten Namens. `get_block_content sd` und `list_networks` melden
`failSafe: true`.

### Main-Safety-Bausteine von F-Ablaufgruppen

Live auf V21 (2026-11-02, E7 und der erneute Test mit `b79b2df`, L1-L4): Jeder SD-Import (`ImportFromDocuments` Override)
von `Main_Safety_RTG1`, dem Main-Safety-Baustein der F-Ablaufgruppe "F-Ablaufgruppe 1", war erfolgreich, hinterließ aber den
`MainSafetyBlockName` der Gruppe als null (`MainSafetyBlockIDbName` blieb), das Übersetzen der PLC schlug fehl ("The F-runtime group
"F-Ablaufgruppe 1" does not have a valid main safety block.") und die Gesamt-F-Signatur wurde 0 (L1); Openness
bietet kein Member, um ihn neu zuzuweisen (L2); die SimaticML-Route (`Import(..., Override)`) behielt beide Namen (L3); ein temporärer
F-Baustein, übersetzt, als SimaticML exportiert, auf den Main-Baustein umbenannt und umnummeriert und als SimaticML auf ihn importiert,
behielt die Referenz mit 0 Übersetzungsfehlern (L4).

Seitdem wird ein Schreibvorgang mit SD-Dokument auf einen Main-Safety-Baustein (`update_block_logic` sd oder ein Bundle ohne `.xml`, die
Netzwerkbearbeitungen, `create_block_from_document` mit `overwrite`) **nie auf diesen Baustein importiert**: Der Worker führt L4
im selben Element aus (temporärer Baustein `zzMcpTmp_<block>_<8 hex>` in der Gruppe des Bausteins, übersetzt mit dem Übersetzungspfad für
einen Baustein, SimaticML-Transplantation von Name/Number/AutoNumber, `update_block_logic` xml auf den Main-Baustein mit dessen aktuellem
Hash, temporärer Baustein gelöscht). Ein Übersetzungsfehler des temporären Bausteins oder ein fehlendes SimaticML stoppt, bevor der Main-Baustein berührt wird,
und lässt das Element mit den Compilermeldungen fehlschlagen; der temporäre Baustein wird auf jedem Pfad gelöscht oder benannt, wenn das nicht möglich ist.
`mainSafetyBlockRoute` im Ergebnis des Elements (`route: "sdViaXmlForMainSafetyBlock"`, `temporaryBlockName`, `mainImport:
"simaticMl"`, `steps`, `notes`) sagt, was getan wurde; ein fehlgeschlagenes Element trägt es als sein `result`. Der Info-Guard
`plc_import_may_unlink_main_safety_block` beschreibt die Route bei der Planung. Die Verifikation `networkCode` /
`contentHash` / `createdBlock` exportiert den Main-Baustein wie bisher erneut in sd. Jede Importroute liest die Gruppen weiterhin
danach erneut und lässt die Verifikation des Elements (`safetyRuntimeGroup`) fehlschlagen, wenn eine Referenz verloren ist. Andere F-Bausteine behalten den
direkten SD-Import. Siehe die [Safety-Referenz](PLC_SAFETY_SUMMARY.md#main-safety-block-references-across-imports). Die
Route ist nur offline verifiziert.

## Export- und Importformen

**Live auf V21 am 2026-11-02 nicht reproduziert** für `GT_Contact`/`EQ_Contact` (mit `SrcType`-Vorlage), `Move`,
`P_Trig`/`N_Trig`, `TON`/`TOF`-Multiinstanzen und `Add`: Der erneute Export eines FB mit 15 Netzwerken war gleich der Importform
bis auf Leerraum (E2). Der folgende Bericht gilt daher nur für nicht getestete Anweisungen; der Guard bleibt
für sie informativ.

Der Drittanbieter-Skill von 1748477582/tia-portal-openness-mcp (Skill §9a, dort auf V21 am 2026-06-11 verifiziert; berichtet,
hier nicht verifiziert) gibt an, dass sich die Exportgrammatik bei manchen Anweisungen von der Importgrammatik unterscheidet: Ein Export
schreibt `Gt{ SrcType := Int }( IN1, IN2 )` (ebenso `Lt`, `Eq`, `Ne`, `Ge`, `Le`), `PBox(...)` (und `NBox`) und
`Move{ Card:=1; DisableENO:=TRUE }( IN, OUT1 )`, die bei `ImportFromDocuments` mit einem allgemeinen "Failed importing" scheitern;
die Importformen sind `GT_Contact( in1 := ..., in2 := ... )` mit `{ S7_Templates := "SrcType := Int" }`,
`P_Trig( #mem )` und `Move( in := ..., out1 => ... )`. Jede Netzwerkbearbeitung importiert die Netzwerke, die sie nicht berührt,
in ihrer Exportform erneut, wie es `set_network_text` mit SimaticML tut. Dieser Server schreibt Exportformen nicht um (keine
Zuordnung ist belegt). Stattdessen benennt der Info-Guard `plc_sd_reimports_export_form` jedes unberührte Netzwerk, das
eine dieser Formen enthält (für `update_block_logic` sd: die übermittelten Netzwerke, die gegenüber dem aktuellen
Export unverändert sind), und `list_networks` listet sie pro Netzwerk in `exportOnlyForms`. Wenn TIA Portal einen solchen Import ablehnt,
diese Netzwerke mit `replace_network` in der Importform neu schreiben oder Texte über die SimaticML-Route
(`set_network_text`) ändern.

Andere berichtete Regeln (gleiche Quelle): Die `.s7res` muss auch ohne Texte vorhanden sein (minimal:
`MultiLingualTexts:` mit einem `- id: MLC_x`-Eintrag und `zh-CN`/`en-US`-Zeilen) — live am 2026-11-02 widerlegt: TIA
Portals eigener Export eines Bausteins ohne Texte hat keine (E1), und eine `en-US`-Kultur, die das Projekt nicht hat, ließ den Import scheitern
(E4); `create_block_from_document` akzeptiert seitdem ein Bundle ohne `.s7res` (E5) und erzeugt nie IDs; beide
Dateien UTF-8 mit BOM (jedes bereitgestellte Dokument wird
mit genau einem BOM geschrieben, ein Dokument ohne BOM wird in den Meldungen des Ergebnisses gemeldet); ein reiner SCL-Baustein kann keine LAD-Netzwerke
enthalten, während ein Baustein mit `S7_PreferredLanguage := "LAD"` SCL-Netzwerke enthalten kann (nicht abgelehnt: TIA Portal entscheidet);
die Aufrufformen `"FCName"()`, `"InstanceDB"( p := ..., o => ... )` und die Inline-Vorlagenform
`#tonInst.TON{ time_type := Time }( PT := T#500ms, ET => )` neben der Siemens-Pragma-Form werden vom
Parser unverändert erhalten. BetterTIA (B1 in [V21-Fallstricke](V21_PITFALLS.md#pitfalls-reported-by-bettertia-not-verified)) berichtet, dass ein
LAD-Import für jedes `.s7res`-Element einen `en-US`-Text benötigen kann: berichtet von BetterTIA/1748477582; live am 2026-11-02
in einem de-DE-Projekt widerlegt, wo ein `en-US`-Text den Import scheitern ließ und `de-DE`-Texte allein erfolgreich waren (die
Sprachen des Projekts sind vermutlich nur de-DE, nicht verifiziert).

## Textbehandlung

Das `.s7dcl`-Modell ist `SimaticSdDocument` (Contracts, Siemens-frei). Ein führendes UTF-8-BOM bleibt als U+FEFF im
Text und im Hash erhalten (der Worker liest die exportierten Dateien als Bytes, `SimaticSdFileText`), CRLF- oder LF-Zeilenenden
bleiben erhalten, und unberührte Zeilen sind byte-identisch. Das Pragma eines Netzwerks ist die nächstgelegene nicht leere Zeile vor seiner
`NETWORK`-Zeile (die einzeilige Form) oder der dort endende mehrzeilige Block; Einfügen oder Löschen hält jedes Pragma
unmittelbar vor seinem `NETWORK` (§4, §7; der Importfehler „duplicate pragma“ des Skills). Pragma-Einträge werden als
`key := value` geparst, getrennt durch `;` außerhalb von Anführungszeichen, und in Reihenfolge mit ihren Rohwerten erhalten, sodass unbekannte Schlüssel jede
Neuschreibung überstehen; ein geänderter Wert wird an Ort und Stelle ersetzt. Der `.s7res`-Leser (`SimaticSdResources`) liest einfache, in Anführungszeichen gesetzte und
Block-YAML-Skalare. Die Ergebnismeldungen des Imports (`DocumentImportResultForBlocks.Messages`, vom Skill
nur als `result.Messages.Select(m => m.Message)` dokumentiert) werden per Reflection gelesen und als Warnungen mit dem Präfix
`SIMATIC SD import:` (eine Zeile, höchstens 256 Zeichen) bei einem Update gemeldet, und in der Fehlermeldung von
`create_block_from_document`.

## Herkunft der Siemens-Member

| Member | Beleg | Verwendung |
| --- | --- | --- |
| `PlcBlock.ExportAsDocuments(DirectoryInfo, string)` → `DocumentExportResult` | Siemens-Skill simatic-sd §2, §8; Compile-Stubs (`reference-stubs/Siemens.Engineering.Step7/Blocks.cs`) | `get_block_content sd`, jeder Planungs- und Verifikationsexport |
| `DocumentExportResult.State`, `.ExportedDocuments` | Compile-Stubs (`Software.cs`); bereits vom xml-Bundle-Export verwendet | Exportergebnis und Dateiliste |
| `PlcBlockComposition.ImportFromDocuments(DirectoryInfo, string, ImportDocumentOptions)` → `DocumentImportResultForBlocks` | Siemens-Skill §3, §8; Compile-Stubs | `update_block_logic` sd und die implizite Route; `create_block_from_document` |
| `ImportDocumentOptions.Override` | Siemens-Skill §3; Compile-Stubs (das einzige Member) | jeder SD-Import (`create_block_from_document` ohne `overwrite` prüft zuerst auf einen existierenden Baustein) |
| `DocumentImportResult.State`, `DocumentResultState.Success` | Siemens-Skill §3; Compile-Stubs | Erfolgstest (alles andere lässt den Import fehlschlagen) |
| `DocumentImportResultForBlocks.Messages`, `Message` seiner Elemente | Siemens-Skill §3 (`result.Messages.Select(m => m.Message)`); nicht in den Compile-Stubs (Typen undokumentiert) | zur Laufzeit über den exakten Namen gebunden (`SimaticSdImportMessages`); ein nicht lesbares Member ergibt einen Hinweis, nie einen eigenen Fehlschlag |
| `PlcBlockComposition.Find(string)`, `PlcBlockGroup.Blocks`, `PlcBlock.Name` | Compile-Stubs; bereits verwendet | Rücklesen bei `create_block_from_document` |

Es wurde kein Stub-Member hinzugefügt und nichts anderes gebunden.

## Weggelassen

- Exportformen in Importformen umschreiben: Keine verlässliche Zuordnung ist belegt (stattdessen Guard und Dokumentation).
- Netzwerktitel oder -kommentare über die SD-Route schreiben: Sie sind `.s7res`-IDs (§10); `set_network_text` (SimaticML)
  erledigt das.
- `.s7res`-IDs oder eine minimale `.s7res` für `create_block_from_document` erzeugen: Der Aufrufer liefert das Dokument
  (für einen Baustein ohne Texte ist keines nötig).
- `.s7res`-Kulturen ablehnen, die keine Projektsprachen sind: TIA Portal lehnt sie selbst ab (E4); das Element listet stattdessen die
  Kulturen auf, und die Projektsprachen bei jedem Anlegen zu lesen, würde einen Lesezugriff für eine Prüfung hinzufügen, die TIA Portal macht.
- TIA Portals SD-Importmeldungen in der geschlossenen Fehlerzusammenfassung von `update_block_logic` weiterreichen: Der Vertrag für das Importergebnis
  bleibt geschlossen; die Meldungen werden stattdessen als kontrollierte Warnungen transportiert.
- SD für PLC-Datentypen (`get_type_content`/`update_type_content`): Der Siemens-Skill dokumentiert `ExportAsDocuments` und
  `ImportFromDocuments` nur für Bausteine, also ist `sd` ein Bausteinformat.
- Live-Abnahme noch offen: `create_block_from_document` mit `overwrite`, ein F-Baustein-Import mit gesetztem Safety-Passwort,
  die automatische Route für den Main-Safety-Baustein als ein Aufruf und die Exportformen von Anweisungen, die am
  2026-11-02 nicht getestet wurden (`create_block_from_document` ohne `.s7res` bestand live im erneuten Test; `set_safety_runtime_group` ist
  `api_member_missing` auf V21, L2). Erledigt am 2026-11-02 ([Live-Bericht](../superpowers/acceptance/reports/2026-11-02-simatic-sd-and-safety-live.md)): die Netzwerkbearbeitungen
  auf LAD/FBD/SCL-Netzwerken und auf einem F-Baustein, die Export-/Importform für die oben aufgeführten Anweisungen und die `.s7res`-Kulturregel.
