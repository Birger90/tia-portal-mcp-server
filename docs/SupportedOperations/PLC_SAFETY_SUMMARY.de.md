# Fehlersichere (F-) PLC-Operationen (`plc_read`, `plc_write`, `plc_download`)

**Sprache:** [English](PLC_SAFETY_SUMMARY.md) · Deutsch

Teilweise live getestet auf TIA Portal V21 am 2026-11-02 (Testprojekt `MCP_Safety_01`, [Live-Bericht](../superpowers/acceptance/reports/2026-11-02-simatic-sd-and-safety-live.md)):
`get_safety_administration`, `compile_check` der F-CPU (E6), der Acknowledge-Guard im Modus `full` (E9), eine SD-Netzwerk-Bearbeitung
des Main-Safety-Bausteins (E7) und `list_safety_blocks` (fehlgeschlagen: Defekt D13, inzwischen behoben); am selben Tag mit `b79b2df`
erneut getestet (L1-L4 im Live-Bericht: der SD-Import löst weiterhin die Verknüpfung zum Main-Safety-Baustein, `set_safety_runtime_group` ist
auf V21 `api_member_missing`, ein von Hand angelegter temporärer F-Baustein als Workaround erhält die Verknüpfung). Die automatische Route über einen
temporären F-Baustein, die diesen Workaround jetzt umsetzt, die Namensattribut-Route von `set_safety_runtime_group` und `attributeInfos`
sind nur offline verifiziert. Jedes Member von Siemens.Engineering.Safety wird
zur Laufzeit gebunden; nichts hier ist gegen `Siemens.Engineering.Safety.dll` kompiliert, die zur separat
installierten Option STEP 7 Safety gehört und fehlen kann.

> **Sicherheitsabnahme.** Ein Sicherheitsprogramm (F-Programm) und sein Zugriffsschutz müssen von einer
> qualifizierten Person validiert und abgenommen werden (Sicherheitsabnahme wie im Handbuch SIMATIC Safety beschrieben, mit dem Sicherheitsausdruck und der
> F-Gesamtsignatur). Dieser MCP-Server ersetzt die Sicherheitsabnahme nie; nichts, was er meldet (Übersetzungsergebnisse,
> Signaturen, Rücklesungen), ist ein Abnahmenachweis. Jeder fehlersichere Schreibvorgang trägt diesen Hinweis in seinem Guard, seiner Abfrage und
> seinem Ergebnis.

Die Oberfläche bleibt 13/32/32: Die Operationen gehören zu `plc_read` und `plc_write`, weil das Sicherheitsprogramm PLC-Software
ist. Methodennamen stehen in `PlcSafetyMethods` (Contracts), Parameter in `WorkerRequest.PlcSafety`, die Verteilung in
`TiaMcpServer.OpennessWorker/Program.PlcSafety.cs`, der Host-Code in `TiaMcpServer/Plc/Safety/`.

## Operationen

| Tool | Operation | Felder | Was sie tut |
| --- | --- | --- | --- |
| `plc_read` | `get_safety_administration` | optional `plcName` (weggelassen: jede PLC) | Pro PLC: ob ihr CPU-Geräteelement `SafetyAdministration` anbietet; `passwordSet` (`IsSafetyOfflineProgramPasswordSet`), `loggedOn` (`IsLoggedOnToSafetyOfflineProgram`); `settings` (`SafetySystemVersion`, `ActivationOfFChangeHistory`, `CreateDriverInstanceDataBlocksWithoutPrefix`, `SafetyModeCanBeDisabled`), `applicableSafetySystemVersions`, `blockNumbers` (`AssignmentOfBlockNumbers`); jede F-Ablaufgruppe mit `MainSafetyBlockName`, `MainSafetyBlockIDbName`, `FOBName`, `FOBEventClass`, `InfoDbName`, `MaximumCycleTime`, `WarnCycleTime`, `PreProcessingName`, `PostProcessingName` und `attributeInfos` (jedes Attribut, das `GetAttributeInfos()` auflistet, mit seinem `accessMode`, sodass ein Live-Lesen zeigt, was beschreibbar ist; null, wenn die Gruppe keine anbietet oder der Aufruf fehlschlug, der Grund in `problems`); `programSignatures` (F-Gesamtsignaturen, dezimal und hexadezimal); die `Siemens.Engineering.Safety.*`-Dienste, die die CPU auflistet; die Namen der Safety Units; `problems` (eine Zeile pro fehlendem oder nicht lesbarem Member). Außerdem die `GlobalSettings`-Getter von TIA Portal (`SafetyModificationsPossible`, `UsernameForFChangeHistory`, `GenerationOfDefaultFailsafeProgram`, `ManagementOfFailsafeInSoftwareUnitsEnvironment`). Jeder Wert hat `state` `read`, `api_member_missing` oder `unreadable`. |
| `plc_read` | `list_safety_blocks` | optional `plcName`, `offset`, `limit` (1-2000, Standard 200) | Jeder Baustein (Programmbausteine mit Benutzer- und Systemgruppen, Software Units, Safety Units), der `SafetySignatureProvider` auflistet oder dessen `ProgrammingLanguage` mit `F_` beginnt: Pfad, Nummer, Typ, Sprache, Know-how-Schutz, Signaturen und `signatureState` (`read`; `unreadable` mit dem Grund in `problem`, wenn der Provider aufgelistet ist, aber `GetService` null zurückgibt oder das Lesen eine Ausnahme wirft; null ohne Provider). D13 (live 2026-11-02): `GetServiceInfos()` listete `SafetySignatureProvider` auf und `GetService` gab null zurück, wodurch die gesamte Auflistung fehlschlug; jetzt werden die Signaturen dieses Bausteins als nicht verfügbar gemeldet und die Auflistung gelingt. |
| `plc_write` | `safety_login` | `passwordVariable`, optional `plcName` | `LoginToSafetyOfflineProgram(SecureString)`; Rücklesung `IsLoggedOnToSafetyOfflineProgram == true`. Bereits angemeldet: nicht aufgerufen, Warnung. Kein Passwort gesetzt: `validation_error`. |
| `plc_write` | `safety_logoff` | optional `plcName` | `LogoffFromSafetyOfflineProgram()`; Rücklesung `false`. |
| `plc_write` | `set_safety_password` | `passwordVariable` (das neue Passwort), optional `plcName` | `SetSafetyOfflineProgramPassword(SecureString)`, wenn keines gesetzt ist; Rücklesung `IsSafetyOfflineProgramPasswordSet == true`. |
| `plc_write` | `revoke_safety_password` | `passwordVariable` (das aktuelle Passwort), optional `plcName` | `RevokeSafetyOfflineProgramPassword(SecureString)`, wenn eines gesetzt ist; Rücklesung `false`. |
| `plc_write` | `create_safety_runtime_group` | `runtimeGroupName`, optional `plcName`, `mainSafetyBlockPath`, `mainSafetyInstanceDbPath` (Bausteinpfade mit dem PLC-Namen) | `RuntimeGroupComposition.Create(String)` (TIA Portal erzeugt den Main-Safety-Baustein), `Create(String, PlcBlock)` (ein F-FC) oder `Create(String, PlcBlock, PlcBlock)` (ein F-FB und sein IDB); unter der Anmelderegel unten (eine Anmeldung nur, wenn ein Safety-Passwort gesetzt ist); ein bestehender Name wird abgelehnt; Rücklesung mit `Find`. |
| `plc_write` | `set_safety_runtime_group` | `runtimeGroupName` (eine bestehende Gruppe), `mainSafetyBlockPath`, optional `plcName`, `mainSafetyInstanceDbPath` | Weist einer bestehenden F-Ablaufgruppe den Main-Safety-Baustein (und den IDB eines F-FB) zu: die Reparatur, wenn eine Gruppe ihren Main-Safety-Baustein verloren hat (E7). Die Zuweisung wird an der Live-`RuntimeGroup` gebunden, in dieser Reihenfolge, und das Ergebnis nennt die Route (`assignments[].route`, `signature`): (i) eine öffentliche beschreibbare Eigenschaft `MainSafetyBlock` / `MainSafetyBlockIDB`, deren Typ von `PlcBlock` zuweisbar ist; (ii) ein Attribut dieses Namens, das `GetAttributeInfos()` als beschreibbar auflistet, gesetzt mit `SetAttribute(name, block)`; (iii) nur wenn `GetAttributeInfos()` `MainSafetyBlockName` / `MainSafetyBlockIDbName` mit einem beschreibbaren Zugriffsmodus (`Write`, `ReadWrite`) auflistet, `SetAttribute(thatName, blockName)` mit dem Bausteinnamen als String (Route `setAttributeName`, nicht live getestet). Keine davon: `api_member_missing`, nichts geändert (beide Member und beide Bausteine werden vor dem ersten Aufruf gebunden und aufgelöst); die Meldung listet jedes Attribut, das `GetAttributeInfos()` gemeldet hat, mit seinem Zugriffsmodus. **Live auf V21 (2026-11-02, L2):** Die `RuntimeGroup` hat keine öffentliche Eigenschaft `MainSafetyBlock` und `GetAttributeInfos()` listet kein Attribut `MainSafetyBlock`: `api_member_missing`. Eine verlorene Referenz in der Safety Administration von TIA Portal reparieren. Jede Zuweisung wird mit `GetAttribute("MainSafetyBlockName")` / `GetAttribute("MainSafetyBlockIDbName")` zurückgelesen, das dem Bausteinnamen entsprechen muss, sonst `postcondition_failed`. Anmelderegel und Abnahme-Guard wie bei `create_safety_runtime_group`; eine unbekannte Gruppe ist bei der Planung `target_not_found`. Nicht live getestet. |
| `plc_download` | `decisions: { "SafetyProgram": "<member>" }` | | Beantwortet die Download-Konfiguration des F-Programms `Siemens.Engineering.Safety.Download.Configurations.SafetyProgram` (keine Standardantwort). PLCSIM und reale Steuerungen gleichermaßen: Acknowledge-Guard `download_includes_safety_program`; eine reale Steuerung löst außerdem `download_to_hardware` aus, wie jeder Hardware-Download. Read-write bestätigt den Aufruf, full läuft unter Policy. **Live 2026-11-02 (V21, CPU 1516F-3 PN/DP V2.9, PLCSIM): TIA Portal verweigert den Download selbst** ("Laden oder Überladen fehlersicherer Daten mit Openness ist nicht erlaubt.", `EngineeringTargetInvocationException` aus `DownloadProvider.Download`), daher wird das F-Programm in der Oberfläche von TIA Portal geladen; der MCP ist nicht strenger und reicht die Ablehnung von TIA durch. |

`passwordVariable` benennt die Passwortquelle, wie `passwords` bei `plc_download` ([Passwortquellen](../guides/password-sources.de.md)):
einen NAMEN einer Umgebungsvariable (`"NAME"` oder `{ "env": "NAME" }`) oder eine generische Anmeldeinformation der Windows-Anmeldeinformationsverwaltung
(`{ "credential": "TiaMcp/..." }`); wenn sie weggelassen wird, kann die Passwortzuordnung des Servers sie liefern (Zweck `safety`). Sie wird
geschwärzt, während die Anfrage gelesen wird: Ein Wert, der keines von beiden ist oder eine Variable benennt, die nicht gesetzt ist, erreicht nie eine
Antwort oder das Audit und wird als `validation_error` abgelehnt. Der Worker löst die Quelle in einen schreibgeschützten
`SecureString` auf (`PasswordSources.Resolve`) und gibt ihn nach dem Aufruf frei; eine Anmeldeinformations-Quelle außerhalb von Windows schlägt mit
`unsupported_capability` fehl. Ergebnisse tragen nur das Quell-Token.

## Änderungen an F-Bausteinen

Ein F-Baustein ist ein Baustein, dessen `ProgrammingLanguage` mit `F_` beginnt (F_LAD, F_FBD, F_DB, ...), oder ein SimaticML-Dokument,
das eine solche Sprache deklariert.

**Regel (Entscheidung des Eigentümers 2026-11-01: Der Server ist nicht strenger als TIA Portal).** Jede Operation, die TIA Portal für
einen F-Baustein anbietet, läuft auch hier. Eine Anmeldung ist nur erforderlich, wenn das Safety-Offline-Programm der PLC ein Passwort hat und die
Sitzung nicht angemeldet ist, was die eigene Regel von TIA Portal ist; ohne Passwort erlaubt TIA Portal Änderungen am F-Programm
ohne Anmeldung, und der Server ebenso. Was der Server nicht beurteilen kann (die PLC bietet keine `SafetyAdministration` an, oder der
Zugriffszustand ist nicht lesbar), wird mit dem Info-Guard `plc_safety_tia_decides` an TIA Portal übergeben; die eigene Ablehnung von TIA Portal
ist der Fehlschlag des Elements (`engineering_rejected`, wo die Worker-Methode ihn meldet, siehe AGENTS.md).

| Operation an einem F-Baustein | Regel |
| --- | --- |
| `update_block_logic` (xml oder source) | Anmelderegel; der Host erkennt einen F-Baustein aus dem SimaticML (xml) oder aus `get_block_interface` (source), der Worker liest die Sprache des Bausteins vor dem Import selbst |
| `add/update/delete_interface_member` | Anmelderegel; geplant und importiert über `update_block_logic`, dessen Worker-Gate die Anmeldung erneut prüft |
| `update_db_member` (F-DB) | Anmelderegel (der Host liest die Sprache aus `get_db_values`) |
| `delete_block`, `rename_block`, `renumber_block` | Anmelderegel (der Host liest die Sprache aus dem Projektbaum) |
| `copy_block`, `move_block` | Anmelderegel; der SimaticML-Export/Import-Rundlauf läuft wie bei jedem Baustein (die generische XML-Route, die TIA Portal für konsistente F-Bausteine anbietet, siehe [Import-/Export-Optionen](IMPORT_EXPORT_OPTIONS_SUMMARY.de.md)); die Ablehnung durch TIA Portal rollt die Transaktion zurück |
| `delete_block_group` mit F-Bausteinen | Anmelderegel und Abnahme-Guard, wenn der Baum einen F-Baustein in der Gruppe zeigt |

„Angemeldet“ ist `PlcSafetyPolicy.Decide`: Ein Passwort ist gesetzt und `IsLoggedOnToSafetyOfflineProgram` ist true. Die einzige
Ablehnung ist `LoginRequired` (Passwort gesetzt, nicht angemeldet und kein früheres `safety_login` im Aufruf): Block-Guard
`plc_safety_login_required`, bevor irgendetwas läuft, und der Worker lehnt ab (`target_kind_unsupported`), unmittelbar vor der
Änderung. Der Host entscheidet mit dem geplanten Zustand des Aufrufs (ein früheres `safety_login`, `safety_logoff`,
`set_safety_password` oder `revoke_safety_password` desselben Aufrufs wird der Reihe nach angewendet), der Worker entscheidet mit dem
Live-Zustand unmittelbar vor der Änderung; der geplante Zustand autorisiert für sich allein nie etwas.

## Referenzen auf den Main-Safety-Baustein bei Importen

Live auf TIA Portal V21, 2026-11-02 (Testprojekt `MCP_Safety_01`, F-Ablaufgruppe "F-Ablaufgruppe 1", Main-Safety-Baustein
`Main_Safety_RTG1`, ein F_FBD-FB mit Nummer 1 und `AutoNumber` false, IDB `Main_Safety_RTG1_DB`):

- **L1 (E7, zweimal reproduziert, auch mit `b79b2df`).** Jeder SIMATIC-SD-Import (`ImportFromDocuments` Override) des
  Main-Safety-Bausteins bringt TIA Portal dazu, den `MainSafetyBlockName` der Gruppe zu verwerfen (null; `MainSafetyBlockIDbName` bleibt); das
  Übersetzen der PLC schlägt dann fehl mit "The F-runtime group "F-Ablaufgruppe 1" does not have a valid main safety block." und die
  F-Gesamtsignatur wird 0.
- **L2.** Die Gruppe lässt sich über Openness nicht neu verknüpfen: keine öffentliche Eigenschaft `MainSafetyBlock`, kein Attribut
  `MainSafetyBlock` in `GetAttributeInfos()` (`api_member_missing`, ehrlich gemeldet von der Element-Verifikation
  `safetyRuntimeGroup`).
- **L3 (E7b).** Ein SimaticML-Import (`update_block_logic` xml, Override; `set_network_text` und ein vollständiges Dokument) desselben
  Bausteins erhält die Referenz.
- **L4.** Von Hand mit bestehenden Operationen durchgeführt, endend mit 0 Übersetzungsfehlern und intakter Referenz: das SD-Dokument als
  temporärer F-FB (`create_block_from_document`, Header `S7_Safety := "TRUE"`, automatische Nummer 2), `compile_check` dieses
  Bausteins (erforderlich: davor lieferte `get_block_content xml` nur die `.s7dcl`, BetterTIA-Fallstrick B4), sein SimaticML
  mit `Name`, `Number` und `AutoNumber` auf die Werte des Main-Bausteins gesetzt (`<AutoNumber>false</AutoNumber>` als erstes
  Kind von `AttributeList` eingefügt), `update_block_logic xml` auf den Main-Baustein mit seinem aktuellen Hash, `delete_block` des
  temporären Bausteins.

Was der Server tut (nur offline verifiziert):

- **Die Route (L4, automatisch).** Ein Schreibvorgang mit SD-Dokument auf den Main-Safety-Baustein (`MainSafetyBlockName`, nicht den IDB)
  einer F-Ablaufgruppe, also `update_block_logic` im Format sd oder ein Bündel ohne `.xml`, `insert_network`, `replace_network`,
  `delete_network` (verändert als `update_block_logic` sd) und `create_block_from_document` mit `overwrite` auf einen solchen
  Baustein, wird nie auf diesen Baustein importiert. In einem Worker-Aufruf (`MainSafetyBlockSdUpdate`,
  `PlcMainSafetyBlockSdRouteWorker`, das Siemens-freie `PlcMainSafetyBlockSdRoute.Run`, gemeinsam mit dem FakeWorker): die übliche
  Vorprüfung (Dokumente, deklarierter Name, Anmelderegel, Hash im Format der Anfrage); ein temporärer Name
  `zzMcpTmp_<block>_<8 hex>` (die Hex-Ziffern aus dem SHA-256 des Namens und der Dokumente, damit er deterministisch ist; ein
  Baustein dieses Namens irgendwo in der PLC lässt das Element ablehnen, nichts geändert); die `.s7dcl` mit diesem Namen, ohne Header
  `S7_BlockNumber` (der temporäre Baustein darf die Nummer des Main-Bausteins nicht beanspruchen) und mit hinzugefügtem `S7_Safety := "TRUE"`, wenn
  der Header kein `S7_Safety` hat; `ImportFromDocuments` Override in die Gruppe des Main-Bausteins; `compile_check` des
  temporären Bausteins über den bestehenden Übersetzungspfad für einen Baustein; sein SimaticML; `Name`, `Number` und `AutoNumber`
  auf die Werte des Main-Bausteins gesetzt (gelesen aus dem eigenen SimaticML-Export des Main-Bausteins, sonst aus seinen Live-Attributen) durch das
  Siemens-freie `SimaticMlBlockIdentity` (nur diese drei Elemente; jedes andere Zeichen bleibt); der bestehende
  `update_block_logic`-xml-Import auf den Main-Baustein mit seinem aktuellen xml-Hash; der temporäre Baustein gelöscht (erst
  danach: ihn vor dem Main-Import zu löschen, ließ TIA Portal V21 live am 2026-11-02 abstürzen, K36). Ein
  Übersetzungsfehler oder ein fehlendes SimaticML stoppt, bevor der Main-Baustein berührt wird: Der temporäre Baustein wird gelöscht und das Element schlägt fehl
  mit den Compilermeldungen (`worker_operation_failed`). Jeder Fehlerpfad löscht den temporären Baustein; einer, der nicht
  gelöscht werden kann, wird in einer Warnung und im Nachweis genannt (`temporaryBlockState: "left"`), nie stillschweigend zurückgelassen. Das
  Elementergebnis trägt `mainSafetyBlockRoute` (`route: "sdViaXmlForMainSafetyBlock"`, `temporaryBlockName`, `temporaryBlockPath`,
  `temporaryBlockState`, `temporaryCompile`, `mainImport: "simaticMl"`, `mainNumber`, `mainAutoNumber`, `identitySource`,
  `steps`, `notes`); ein fehlgeschlagenes Element trägt dasselbe Objekt als sein `result` (über den Nachweiskanal `PlcDownloadOutcome`).
  Bei `create_block_from_document` behält der Main-Baustein seine Gruppe (ein abweichender `targetGroupPath` wird gemeldet).
  Die Schritte laufen nacheinander ohne Transaktion, wie live: Eine Transaktion würde den Main-Import zusammen
  mit dem temporären Baustein zurückrollen, und Übersetzen und Export innerhalb einer offenen Transaktion sind nicht verifiziert.
- **Andere F-Bausteine** (kein Main-Safety-Baustein, einschließlich des IDB eines solchen) behalten den direkten SD-Import: Es gibt keinen Hinweis
  darauf, dass sie etwas verlieren. Wenn der Worker die Ablaufgruppen vor dem Schreiben nicht lesen kann, sagt er das in einer Warnung und
  importiert direkt.
- **Planung.** Für einen Import über die SD-Route eines Bausteins, den das Planungslesen als Main-Safety-Baustein (oder IDB) einer
  Ablaufgruppe zeigt, nennt der Info-Guard `plc_import_may_unlink_main_safety_block` die Gruppen; für einen Main-Safety-Baustein beschreibt sein
  Text die Route und ihre sechs Schritte (ein direkter SD-Import verwirft die Referenz, live 2026-11-02). Das Element läuft.
- **Sicherheitsnetz auf jeder Route** (`update_block_logic` xml, sd und source, und damit jede geplante Bearbeitung;
  `create_block_from_document` mit `overwrite`): Vor dem Import eines F-Bausteins zeichnet der Worker die Ablaufgruppen auf, deren
  `MainSafetyBlockName` oder `MainSafetyBlockIDbName` ihn benennt; nach dem Import (auch nach einem fehlgeschlagenen) liest er sie erneut
  und versucht, eine verlorene Referenz mit dem Mechanismus von `set_safety_runtime_group` neu zuzuweisen (auf V21 nicht möglich, L2). Jede
  verlorene Referenz wird zu einer Warnung mit der Reparatur (Safety Administration von TIA Portal). Der Host liest dann
  `get_safety_administration` erneut; eine aufgezeichnete Referenz, die den Baustein nicht mehr benennt, macht den Verifikationseintrag des Elements
  zu `success: false` mit der Prüfung `safetyRuntimeGroup`.

Nicht abgedeckt: `move_block` und `copy_block` (ein Verschieben importiert in eine andere Gruppe und löscht das Original; ob TIA
Portal die Gruppenreferenz dann behält, ist ungetestet, und der Rundlauf läuft in seiner eigenen Transaktion), und Importe
über `vci_write`. Danach `get_safety_administration` prüfen. Die automatische Route ist nicht live als ein Aufruf gelaufen.

## Safety-Units in den allgemeinen Lesevorgängen (Audit T4, nicht live getestet)

Bisher lasen nur `get_safety_administration`/`list_safety_blocks` `PlcUnitSystemGroup.SafetyUnits`; jeder allgemeine
Durchlauf ging nur über `Units`, sodass auf einer F-CPU mit fehlersicheren Software-Units das F-Programm in
`browse_project_tree`, der Baustein- und Typauflösung von `plc_read`/`plc_write`, den Variablenkommentaren, externen Quellen
und `read_cross_references` fehlte. Diese Durchläufe nutzen jetzt `PlcUnits.All` (Worker `Openness/Plc/PlcUnits.cs`):
`Units`, dann `SafetyUnits` (members.txt, per Reflection gelesen; die Referenz-Stubs deklarieren es nicht), jeweils über
`PlcUnitBase` (`BlockGroup`, `TagTableGroup`, `TypeGroup`, `ExternalSourceGroup`, `Name`). Eine Safety-Unit wird wie eine
Software-Unit gezeigt und adressiert (`<PLC>/Units/<Name>`, Knotentyp `SoftwareUnit`), wie `list_safety_blocks` es schon tat.
Ein Lesefehler von `SafetyUnits` wird gemeldet (Baum: `skipped`; Variablenkommentare: `messages`), die Standard-Units bleiben
lesbar. Schreibvorgänge in eine Safety-Unit durchlaufen weiter die F-Schranken (`plc_safety_login_required`, Abnahme-Guards);
`generate_loadable` lehnt einen Safety-Unit-Namen ab (die dokumentierte Überladung nimmt `PlcUnit`); die Ordner- und
Container-Schreibvorgänge (`PlcSourceFolderTree.Unit`) bleiben auf Standard-Units beschränkt.

## Guards, Bestätigung und Audit

| Guard | Schweregrad | Ausgelöst durch |
| --- | --- | --- |
| `plc_safety_acceptance_required` | acknowledge | jeden fehlersicheren Schreibvorgang und jede Änderung an einem F-Baustein; die Meldung trägt den Abnahmehinweis |
| `plc_safety_login_required` | block | eine Änderung an einem F-Baustein oder das Anlegen einer Ablaufgruppe auf einer PLC, deren Sicherheitsprogramm ein Passwort hat, während die Sitzung nicht angemeldet ist (geplanter Zustand) |
| `plc_safety_tia_decides` | info | einen fehlersicheren Schreibvorgang oder eine Änderung an einem F-Baustein, deren Zugriffszustand nicht gelesen werden konnte oder deren PLC keine `SafetyAdministration` anbietet: TIA Portal entscheidet |
| `plc_import_may_unlink_main_safety_block` | info | einen Import über die SD-Route eines Bausteins, den das Planungslesen als Main-Safety-Baustein (oder IDB) einer F-Ablaufgruppe zeigt (L1/E7; die SimaticML-Route behielt die Referenz live, L3/E7b); für einen Main-Safety-Baustein beschreibt der Text die Route über den temporären F-Baustein, die der Worker statt des SD-Imports nimmt |

`plc_write` bestätigt einen Aufruf (`ConfirmsEveryCall`) genau dann, wenn er einen fehlersicheren Schreibvorgang enthält oder die Planung eine Änderung an einem F-Baustein
gefunden hat, wie `library_write` mit seinen Lifecycle-Elementen: read-write fragt den Menschen einmal (Formular-Elicitation; ein Client
ohne diese wird mit `access_denied` abgelehnt), full erfüllt den Guard per Policy, ein Probelauf fragt nie. Die Abfrage listet
jedes fehlersichere Element mit seiner PLC und seinen Änderungen, jeder Wert des Aufrufers in Anführungszeichen. Unsichere operationIds werden für solche Aufrufe abgelehnt.
Gewöhnliche `plc_write`-Aufrufe fragen weiterhin nichts. Ein abhängiges Element, das neu geplant wird, behält die Abnahme, die sein Planungsdurchlauf
ausgelöst hat; ein Abnahme-Guard, den nur die Neuplanung auslöst, kommt zu spät und wird in read-write blockiert. Audit-Datensätze tragen
`confirmation.by` `user` oder `policy`; Passwörter erscheinen nie.

## Nachweise für jedes Siemens-Member

Alle Member werden per Reflection gebunden (`PlcSafetyApiCatalog`, `PlcSafetyReflection`, Worker `PlcSafetyService`): Dienste
werden unter `IEngineeringServiceProvider.GetServiceInfos()` über den exakten Typnamen gefunden, oder über das dokumentierte generische
`GetService<T>()`, geschlossen über den per Name geladenen Typ; Eigenschaften über den exakten Namen; Methoden über den exakten Namen und die
Argumenttypen (`LibraryRuntimeBinding`). Ein fehlendes Member ist `api_member_missing` (ein Zustand eines gelesenen Werts, eine Fehlerkategorie beim Schreiben).
Es wurde kein Stub hinzugefügt.

| Member | Nachweis | Quelle |
| --- | --- | --- |
| `DeviceItem.GetService<SafetyAdministration>()` (CPU-Element mit `SoftwareContainer`) | Siemens-Primärquelle | `tia-portal-ai-extensions` `plc-safety-administration/SKILL.md` §2 |
| `SafetyAdministration.Settings`, `.RuntimeGroups` | Siemens-Primärquelle | `plc-safety-administration/SKILL.md` §1, §3, §5; `libraries-and-alarms/SKILL.md` |
| `SafetySettings.SafetySystemVersion`, `GetApplicableSafetySystemVersions()`, `AssignmentOfBlockNumbers` (`ManagementMode`, `FromDB`..`ToFC`) | Siemens-Primärquelle | `plc-safety-administration/SKILL.md` §3, §4 |
| `RuntimeGroupComposition`-Aufzählung, `Find(String)`, `Create(String, PlcBlock, PlcBlock)`; `RuntimeGroup.Name` | Siemens-Primärquelle | `plc-safety-administration/SKILL.md` §5; `libraries-and-alarms/SKILL.md` |
| `RuntimeGroup`-Attribute `MainSafetyBlockName`, `MainSafetyBlockIDbName` (`GetAttribute`) | Siemens-Primärquelle | `libraries-and-alarms/SKILL.md`; `engineering-objects/SKILL.md` |
| `RuntimeGroup.MainSafetyBlock`, `RuntimeGroup.MainSafetyBlockIDB` (`PlcBlock`) im Objektmodell; "Call `SetAttribute` directly on the `RuntimeGroup` object" | Siemens-Primärquelle (Membernamen und die SetAttribute-Regel; der Attributpfad und die Beschreibbarkeit sind **nicht** dokumentiert) | `plc-safety-administration/SKILL.md` §1 (Objektmodell), §5 ("Setting attributes on a RuntimeGroup"). Verwendet von `set_safety_runtime_group` und der automatischen Neuverknüpfung, zur Laufzeit gebunden als beschreibbare Eigenschaft, als ein laut `GetAttributeInfos()` beschreibbares Attribut oder als ein laut `GetAttributeInfos()` beschreibbares `MainSafetyBlockName`/`MainSafetyBlockIDbName`, gesetzt auf den Bausteinnamen (`PlcSafetyRuntimeGroupBinding`), nie geraten. Live V21 2026-11-02 (L2): Weder die Eigenschaft noch das Attribut `MainSafetyBlock` existiert |
| `IEngineeringObject.GetAttributeInfos()` (`Name`, `AccessMode`), `SetAttribute(String, Object)` | Siemens-Primärquelle; Compile-Stubs | `engineering-objects/SKILL.md`; bereits von den Umbenennungen verwendet |
| `PlcBlock.GetService<SafetySignatureProvider>()` | Siemens-Primärquelle (keine Membernamen) | `change-detection/SKILL.md` |
| `PlcUnitGroup.SafetyUnits` | Siemens-Primärquelle | `libraries-and-alarms/SKILL.md` |
| `SafetyAdministration.ProgramSignatures`, `IsSafetyOfflineProgramPasswordSet`, `IsLoggedOnToSafetyOfflineProgram`, `LoginToSafetyOfflineProgram(SecureString)`, `LogoffFromSafetyOfflineProgram()`, `SetSafetyOfflineProgramPassword(SecureString)`, `RevokeSafetyOfflineProgramPassword(SecureString)` | V21-Baseline der installierten API, Verwendung durch Dritte | Czarnak/totally-integrated-claude `api-baselines/v21/safety.json` und `skills/tia-plc-operations/references/safety-administration.md` (extrahiert aus einer installierten V21-`Siemens.Engineering.Safety.dll` und ihrer XML-Dokumentation); asckye/TIA_Portal_Openness_MCP `SafetyManagementAdapter.cs` (MIT, gegen V21 kompiliert) |
| `SafetySettings.ActivationOfFChangeHistory`, `CreateDriverInstanceDataBlocksWithoutPrefix`, `SafetyModeCanBeDisabled`; `RuntimeGroupComposition.Create(String)`, `Create(String, PlcBlock)`; `RuntimeGroup.FOBName`, `FOBEventClass`, `InfoDbName`, `MaximumCycleTime`, `WarnCycleTime`, `PreProcessingName`, `PostProcessingName` | V21-Baseline der installierten API | wie oben |
| `SafetySignatureProvider.Signatures`, `SafetySignature.Type`, `.Value` (ganzzahlig) | V21-Baseline, Verwendung durch Dritte | wie oben |
| `TiaPortal.GetService<GlobalSettings>()` und seine parameterlosen Getter | V21-Baseline, Verwendung durch Dritte | wie oben (Setter sind Überladungen mit einem Argument und werden nie aufgerufen) |
| `Siemens.Engineering.Safety.Download.Configurations.SafetyProgram.CurrentSelection` (`SafetyProgramSelections`) | V21-Baseline; Member `ConsistentDownload` aus Verwendung durch Dritte | Typliste der Baseline; asckye `DownloadPromptPolicy.cs`. Das Member wird live gelesen (`availableSelections`) |

Als Baseline oder Drittverwendung gekennzeichnete Member sind nicht gegen eine Siemens-Primärveröffentlichung verifiziert: Die Seiten des 
Siemens-V21-Openness-Handbuchs dazu konnten nicht abgerufen werden (support.industry.siemens.com verweigerte den Download, und
docs.tia.siemens.cloud stellte dem Abrufprogramm das Safety-Kapitel nicht bereit). Sie werden als `api_member_missing` gemeldet, wenn
sie in einer Installation fehlen.

## Weggelassen, und warum

| Weggelassen | Grund |
| --- | --- |
| Ein eigenes Safety-Übersetzen | In der Typliste der Safety-Assembly existiert kein Übersetzungs-Member; die Implementierung des Drittanbieters stellt fest: "F compile is not in the PublicAPI". `compile_check` führt das gewöhnliche `ICompilable.Compile` der PLC-Software aus, und auf einer F-CPU übersetzt das auch das Sicherheitsprogramm: live auf V21 am 2026-11-02 (E6) meldete `compile_check plcName` "Compile safety program 'Safety Administration'". |
| `SafetyPrintout.Print` | In der Baseline aufgeführt, aber das Schreiben eines Ausdrucks braucht eine Wahl des Druckertreibers und eine Dateibehandlung, die die Nachweise nicht festlegen (`SafetyPrintoutFilePrinter`, Member von `SafetyPrintoutOption`); sein Vorhandensein wird in `safetyServices` gemeldet. |
| Schreibvorgänge an `SafetySettings` / `AssignmentOfBlockNumbers` / `SafetySystemVersion`, Schreibvorgänge an `RuntimeGroup`-Attributen außer dem Main-Safety-Baustein und seinem IDB (`set_safety_runtime_group`) sowie `Delete`, `GenerateGlobalFIOStatusBlock`, `CleanSystemGeneratedObjects`, `SafetyBaseIdProvider.GenerateBaseId`, `GlobalSettings`-Setter | Änderungen der Safety-Konfiguration mit Folgen, die die Quellen nicht beschreiben (Bereichsprüfungen, neu erzeugte F-Systembausteine, Auswirkungen auf ganz TIA Portal); hier nur lesend. |
| `SafetyValidationAssistant` (Aktivierungstests, Sicherheitsfunktionen) | Eine separate Assembly (`Siemens.Engineering.SafetyValidation`), deren Ablauf Teil der Abnahme selbst ist. |
| Anlegen von F-Bausteinen mit `create_block` | Keine verifizierte SimaticML-Vorlage für F-Sprachen; `create_block` lehnt `F_*`-Sprachen weiterhin ab. `create_safety_runtime_group` ohne Main-Baustein verwenden (TIA Portal erzeugt ihn) oder über TIA Portal importieren. |
| `create_instance_db` für einen F-FB | Nicht geprüft; der eigene Schutz von TIA Portal greift. |

## Live-Abnahme

Erledigt am 2026-11-02: siehe den [Live-Bericht](../superpowers/acceptance/reports/2026-11-02-simatic-sd-and-safety-live.md) (E6-E9, und den erneuten Test mit `b79b2df`: L1-L4,
D13/D14/E5 als behoben bestätigt). Offen: die automatische Route über den temporären F-Baustein als ein Aufruf, die `attributeInfos`, die die
Live-`RuntimeGroup` meldet (und ob Route (iii) von `set_safety_runtime_group` existiert), und der Rest des
[Live-Testplans für PLCSIM Advanced F-CPU](../development/plcsim-fcpu-live-test-plan.md).
