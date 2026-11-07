# Online- und Offline-Openness-Operationen für PLCs (Vergleich, Online-Zustand, Hochladen der Station, Beobachtungs-/Forcetabellen)

**Sprache:** [English](PLC_ONLINE_OPENNESS_SUMMARY.md) · Deutsch

Diese Referenz behandelt die Openness-V21-Funktionen, die dieser Fork rund um den PLC-Online-Workflow ergänzt:

- Online/Offline-Vergleich und Softwarevergleich (`plc_read` `compare_to_online`, `compare_software`) und der Hardwarevergleich
  (`compare_hardware`, `compare_hardware_to_online`; Issue #7);
- den Online-Zustand und das Online-/Offline-Gehen (`plc_read` `get_online_state`, Tool `plc_tia_online`);
- das Hochladen der Station in das Offline-Projekt (`plc_read` `list_station_upload_interfaces`, Tool
  `plc_station_upload`);
- Beobachtungs- und Forcetabellen (`plc_read` `list_watch_and_force_tables`, `read_watch_table`,
  `read_force_table`, `get_watch_table_content`, `get_force_table_content`; `plc_write`
  `create_watch_table`, `delete_watch_table`, `create_watch_table_group`, `delete_watch_table_group`,
  `update_watch_table_content`, `update_force_table_content`);
- die Offline-Aktualisierung der Anweisungen `plc_write` `update_program`.

> **Status: nicht live getestet.** Diese Funktionen sind nur offline qualifiziert: gegen die
> nur zum Übersetzen dienenden Referenz-Stubs gebaut, auf der Siemens-freien Logik unit-getestet und durchgängig
> über den geskripteten FakeWorker ausgeführt (`plc-online-roundtrip`, `plc-online-confirmation`, `plc-online-tls`). Nichts hiervon ist gegen TIA Portal gelaufen.
> Die mechanische Signaturprüfung ist `scripts/verify-reference-stubs.ps1` (das die Probe
> `reference-stubs/TiaMcpServer.OpennessReferenceProbe/OnlineReferenceSurface.cs` übersetzt) plus ein Windows-
> Build des Workers und der Probe gegen das installierte
> `C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48`. Jedes Member, das in der
> [Herkunftstabelle](#herkunft-jedes-verwendeten-siemens-members) als *angenommen* markiert ist, muss diese
> Übersetzung gegen die echten DLLs bestehen, bevor ein Live-Test stattfindet; die ersten Live-Prüfungen sind unter
> [Live-Abnahmeprüfungen](#live-abnahmeprüfungen) aufgeführt.

Tool-Anzahlen: 13 Tools in read-only, 32 in read-write und 32 in full seit `siemens_docs` (31 seit `tia_inbox`, 30 seit `retrieve_project`, 29 seit den Bibliotheks-Tools, 27, als diese Tools hinzukamen; `plc_tia_online` und
`plc_station_upload` sind die beiden hier hinzugefügten schreibfähigen Tools; jede andere Operation liegt in
`plc_read`/`plc_write`).

**Nicht die Web-API-Tools.** `plc_online_read`/`plc_online_write` ([Web-API-Referenz](PLC_ONLINE_WEBAPI_SUMMARY.de.md))
sprechen vom Host aus über HTTPS mit einer konfigurierten S7-1500-CPU; alles hier läuft über TIA Portal Openness im
Worker auf dem gebundenen Projekt. Das Verbindungs-Tool heißt `plc_tia_online` (die Online-Verbindung von TIA Portal zu einer PLC)
statt `plc_online_connection`, weil `plc_online_*` bereits die Web-API-Tool-Familie benennt (AGENTS-Projekt-
tabelle, Installationsanleitung, Test-Harness und ein Konformitätstest, der nach Präfix auswählt und
`plc_online_connection` sofort fälschlich als Web-API-Tool einstufte).

## Zugriff und Bestätigung

Die Entscheidung des Eigentümers für diesen Fork: Die KI darf alles tun, was der PLC-Ingenieur in TIA Portal tun kann,
gesteuert durch den Zugriffsmodus. Read-only ist Beobachtung; read-write behandelt Projektänderungen wie die
Schreibvorgänge der anderen Domänen (keine Rückfrage) und lässt jede Online- oder Lifecycle-Aktion per
Elicitation bestätigen; full führt alles ohne Rückfragen aus. Nichts wird hart blockiert außer ungültigen oder
mehrdeutigen Eingaben.

| Operation / Tool | Worker-Methode | Capability | read-only | read-write | full |
| --- | --- | --- | --- | --- | --- |
| `plc_read` `compare_software`, `compare_hardware`, `get_online_state`, `list_station_upload_interfaces`, `list_watch_and_force_tables`, `read_watch_table`, `read_force_table` | gleicher Name | `Observe` | ja | ja | ja |
| `plc_read` `get_watch_table_content`, `get_force_table_content` | gleicher Name | `TemporaryExport` | ja | ja | ja |
| `plc_read` `compare_to_online`, `compare_hardware_to_online` | gleicher Name | `OnlineObserve` (neu) | ja | ja | ja |
| `plc_write` Schreibvorgänge an Beobachtungs-/Forcetabellen, `update_program` | gleicher Name | `ProjectMutation` | nein | ja, keine Rückfrage | ja |
| `plc_tia_online` (`go_online`, `go_offline`) | `plc_go_online`, `plc_go_offline` | `OnlineControl` | nein (nicht aufgeführt) | ja, eine Rückfrage pro tatsächlichem Aufruf | ja, Policy |
| `plc_station_upload` | `plc_station_upload` | `ProjectMutation` | nein (nicht aufgeführt) | ja, eine Rückfrage pro tatsächlichem Aufruf | ja, Policy |

### Warum `compare_to_online` eine eigene Capability hat

`PlcSoftware.CompareToOnline()` erreicht das Zielgerät über das Netzwerk, ändert aber weder das
Gerät noch das Projekt. `Observe` des Repositorys wurde als "inspect project data without side
effects" definiert, daher passt eine Netzwerkverbindung zu einer echten CPU nicht sauber dazu; die Entscheidung des Eigentümers sagt,
read-only bedeutet Beobachtung, und das ist es. `OperationCapability.OnlineObserve` wird daher als
letzte angehängt (die Zahlenwerte der vorhandenen Capabilities bleiben stabil) und in jedem Modus erlaubt. Sie
von `Observe` getrennt zu halten, macht die Netzwerkreichweite im Policy-Katalog explizit und erlaubt einer späteren Policy,
sie zu verschärfen, ohne Offline-Lesevorgänge anzufassen. `plc_read scan_accessible_devices`
(`ConfigurationPcInterface.GetAccessibleDevices()` an einer ausdrücklich benannten PC-Schnittstelle, Live-Netzwerk-
erkennung, keine Änderung) nutzt dieselbe Capability und, wie `compare_to_online`, einen Bestätigungsbereich mit festem Nein
(ein abgelehnter Dialog lässt ihn fehlschlagen); siehe [PLC_SOURCES_FOLDERS_SUMMARY.md](PLC_SOURCES_FOLDERS_SUMMARY.md#accessible-devices-scan_accessible_devices).
Sie gilt für die Fehlerklassifizierung als nicht verändernd
(eine Siemens-`EngineeringTargetInvocationException` ist `worker_operation_failed`, nie
`engineering_rejected`), und sie erfordert die verifizierte Sitzungsidentität wie jeder Inhaltslesevorgang.

Der Vergleich wendet keinen Verbindungspfad an. War die PLC vorher nicht online und ist nach dem Vergleich `Online` oder
`Connecting`, gleich ob der Vergleich zurückkehrte oder eine Ausnahme warf, ruft der Worker `GoOffline()`
in einem `finally`-Pfad auf, um den früheren Zustand wiederherzustellen (die Vorgabe der Quelle: nur für eine Verbindung offline gehen,
die diese Anwendung aufgebaut hat), und wartet bis zu 10 s auf `Offline` (Siemens-freier Kern
`PlcCompareOnlineRun`). `restoreAttempted` sagt, ob er lief; `disconnectedAfterCompare` ist nur dann true,
wenn `Offline` innerhalb der Wartezeit beobachtet wurde, sodass eine fehlgeschlagene oder abgelaufene Wiederherstellung `false` mit dem
Endzustand in `onlineStateAfter` meldet (und eine Warnung: mit `plc_tia_online` offline gehen). Während der Vergleich
läuft, ist ein geschlossen fehlschlagender `OnlineLegitimation`-Handler an der Verbindungskonfiguration der PLC
(`OnlineProvider.Configuration`) registriert und wird in `finally` entfernt: Er beantwortet keine Authentifizierungs- oder Passwort-
abfrage und eine TLS-Zertifikatsprüfung (`TlsVerificationConfiguration`) nur aus den
`trustedCertificates` des Elements (siehe [TLS-Zertifikatsvertrauen](#tls-zertifikatsvertrauen-trustedcertificates)). Jede
vorgelegte Konfiguration wird in `configurationsSeen` des Ergebnisses festgehalten; jede abgelehnte Abfrage verwirft den
Vergleich (`worker_operation_failed`), und das `result` des fehlgeschlagenen Elements enthält die Belege
(`configurationsSeen`, `abort` mit `tls`, die Online-Zustände; `root` null). Ob `CompareToOnline()`
selbst eine Verbindung öffnet, eine vorhandene benötigt oder die Legitimation überhaupt über dieses Ereignis auslöst, ist in den
Quellen nicht dokumentiert; für einen deterministischen Pfad zuerst mit `plc_tia_online` online gehen. Ob TIA
Portal ein während eines Vergleichs beantwortetes Vertrauen nach dem Aufruf beibehält, ist ebenfalls nicht dokumentiert.

### Warum `plc_station_upload` jeden Aufruf bestätigen lässt

Das Hochladen der Station ist eine `ProjectMutation` (es fügt dem Offline-Projekt ein Gerät hinzu), daher ist es
bei den anderen schreibfähigen Tools registriert und in read-only verweigert. Es verbindet sich außerdem mit einem Gerät unter einer Adresse des
Aufrufers, was nach der Entscheidung des Eigentümers eine Online-Aktion ist, daher lässt es jeden tatsächlichen read-write-Aufruf
bestätigen wie `plc_tia_online` und `plc_download` (`ConfirmsEveryCall = true`). Die Rückfrage nennt die Adresse,
den Modus, die PC-Schnittstelle, die Folge (ein neues, ungespeichertes Gerät) und genau, welche Callback-Konfigurationen
der Aufrufer beantwortet.

## `plc_read`-Operationen

Alle erfordern eine verifizierte Bindung. Die Lesevorgänge für Vergleich, Online-Zustand und Upload-Schnittstellen sind
nur für Standalone (wie `list_download_targets`); die Lesevorgänge für Beobachtungs-/Forcetabellen sind Projektinhalt und
funktionieren auch in lokalen Multiuser-Sitzungen.

| Operation | Felder | Ergebnis |
| --- | --- | --- |
| `compare_to_online` | optional `plcName`, `maxDepth` (1-64, Standard 16), `maxElements` (1-5000, Standard 300), `differencesOnly` (Standard true), `trustedCertificates` (SHA-256-Fingerabdrücke, die der Benutzer freigegeben hat, siehe [TLS-Zertifikatsvertrauen](#tls-zertifikatsvertrauen-trustedcertificates); nicht live getestet), `passwords` (seit 2026-11-01: Online-Passwortkonfigurationen wie `OnlineReadAccessPassword` nach kurzem Typnamen → Passwortquelle, nie ein Wert; weggelassene Einträge aus der Passwort-Map des Servers; siehe die [Anleitung zu Passwortquellen](../guides/password-sources.de.md)) | `PlcCompareResultInfo`: `compareTarget: "online"`, `onlineStateBefore`, `onlineStateAfter`, `restoreAttempted`, `disconnectedAfterCompare`, `rootState`, `identical`, `root`, Obergrenzen, `elementsListed`, `identicalElementsSkipped`, `complete`, `omission`, `configurationsSeen` (die vorgelegten Online-Legitimationskonfigurationen, mit `tls` für eine TLS-Prüfung), `abort` (in einem gelieferten Vergleich immer null; nur in den Belegen des fehlgeschlagenen Elements gesetzt) |
| `compare_software` | `compareTarget` `plc` (mit `targetName` = andere PLC), `project_library` oder `master_copy` (mit `targetName` = Kopiervorlage im Wurzelordner der Kopiervorlagen der Projektbibliothek) oder `secondary_plc` (mit `targetName` = eine PLC des offenen Sekundärprojekts `targetProject`; Issue #20, nicht live getestet); optional `plcName` und die Obergrenzen oben; `trustedCertificates` wird abgelehnt | dieselbe Form; Online-Member sind null, `configurationsSeen` ist leer und `abort` null |
| `compare_hardware` | `compareTarget` `plc` (mit `targetName` = andere PLC) oder `master_copy` (mit `targetName` = Kopiervorlage im Wurzelordner der Kopiervorlagen der Projektbibliothek); optional `plcName`, `hardwareScope` (`device`, Standard: die Station der PLC; `cpu`: ihr CPU-Geräteelement; bei `plc` das Objekt der anderen PLC im selben Umfang) und die Obergrenzen oben; `trustedCertificates` wird abgelehnt | dieselbe Form wie `compare_software`; siehe [Hardwarevergleich](#hardwarevergleich) |
| `compare_hardware_to_online` | optional `plcName`, `hardwareScope`, die Obergrenzen, `trustedCertificates`, `passwords` (genau wie `compare_to_online`) | dieselbe Form wie `compare_to_online` |
| `get_online_state` | optional `plcName` (weggelassen: jede PLC wird aufgeführt) | `{ plcs[{ plcName, deviceName, onlineProviderAvailable, state, isConfigured, modes[{ name, pcInterfaces[{ name, number, targetInterfaces[{ name }] }] }] }] }` aus `OnlineProvider.State`, `Configuration.IsConfigured` und `Configuration.Modes` (offline; nie `GetAccessibleDevices()`) |
| `list_station_upload_interfaces` | keine | `{ available, modes[{ name, pcInterfaces[{ name, number, targetInterfaces: [] }] }] }` aus `StationUploadProvider.Configuration` |
| `list_watch_and_force_tables` | optional `plcName` | pro PLC: `groups` (Pfade der Anwendergruppen), `watchTables` und `forceTables` `{ groupPath, name, isConsistent }`, `complete` (höchstens 5.000 Einträge) |
| `read_watch_table` / `read_force_table` | `tableName`, optional `plcName`, `folderPath` (Gruppenpfad, Standard `/`), `maxElements` (1-5000, Standard 1000) | die Tabelle mit `entries[{ index, name, address, displayFormat, monitorTrigger, modifyIntention, modifyTrigger, modifyValue, unreadableAttributes }]` (Force: `forceIntention`, `forceValue` statt der Modify-Member), `entryCount`, `entriesComplete` |
| `get_watch_table_content` / `get_force_table_content` | `tableName`, optional `plcName`, `folderPath` | `{ format: "xml", content, contentHash, warnings }`: das SimaticML der Tabelle (`Export(FileInfo, ExportOptions.None)` in eine temporäre Datei, `DocumentInfo` entfernt wie bei Bausteinexporten) |

Der Vergleichsbaum wird in Tiefensuche in Dokumentreihenfolge projiziert. Jedes Element ist
`{ leftName, rightName, comparisonResult, detailedInformation, elements[], elementsTruncated }`;
Texte werden bei 1.000 Zeichen abgeschnitten. Mit `differencesOnly` wird ein Element, dessen Zustand
`ObjectsIdentical` oder `FolderContentsIdentical` ist, übersprungen und gezählt (die Wurzel wird immer aufgeführt).
Stoppt die Tiefen- oder Elementobergrenze den Durchlauf, ist `complete` false, `omission` sagt, welche Obergrenze, und
jedes abgeschnittene übergeordnete Element hat `elementsTruncated: true`. `identical` ist true, wenn der Wurzelzustand einer der
beiden identischen Zustände ist. Ein Ergebnis über dem Wertbudget wird als Ganzes mit Hinweisen ausgelassen (die Obergrenzen senken).

### Hardwarevergleich

> **Nicht live getestet** (GitHub-Issue #7). Nachweis: die V21-IntelliSense-Datei `Siemens.Engineering.Base.xml`,
> `M:Siemens.Engineering.HW.HardwareObject.CompareTo(Siemens.Engineering.HW.IHardwareCompareTarget)` ("Compare the hardware object
> vs the given target", liefert `Siemens.Engineering.Compare.CompareResult`) und `T:Siemens.Engineering.HW.IHardwareCompareTarget`
> ("Access to the device/device item in a HW compare scenario", ohne Member). Die XML nennt keine Vererbung; welche Typen die
> Schnittstelle umsetzen, ist daher nicht dokumentiert.

`compare_hardware` (`Observe`) und `compare_hardware_to_online` (`OnlineObserve`, jeder Modus, wie `compare_to_online`) vergleichen die
Station der PLC (`hardwareScope:"device"`, das `Device` über ihrem CPU-Geräteelement) oder ihr CPU-Geräteelement (`"cpu"`); beide sind
`HardwareObject`s. Worker `Openness/PlcOnline/PlcHardwareCompareService.cs`; das Ergebnis ist derselbe begrenzte
`PlcCompareResultInfo`-Baum wie bei den Softwarevergleichen.

- **Bindung.** `CompareTo` steht nicht in den Compile-Stubs: Es wird am Live-Objekt über den Namen und ein Argument gebunden, das vom
  Laufzeittyp des Ziels zuweisbar ist, und der gebundene Parameter muss `IHardwareCompareTarget` sein. Bietet das Objekt dieses
  `CompareTo`, setzt das Ziel die Schnittstelle aber nicht um, scheitert das Element mit `unsupported_capability` und nennt den
  Laufzeittyp des Ziels, bevor etwas verglichen (offline) oder kontaktiert (online) wird; gar kein `CompareTo` ist `api_member_missing`.
- **Offline-Ziele.** `plc`: Station oder CPU der anderen PLC (`targetName` darf nicht auf die verglichene PLC zeigen); `master_copy`:
  die Kopiervorlage dieses Namens im Wurzelordner (`MasterCopyFolder.MasterCopies.Find`), verglichen, wenn die Kopiervorlage von TIA
  Portal die Schnittstelle umsetzt. Die Projektbibliothek selbst wird nicht angeboten (eine Bibliothek ist kein Hardwareobjekt). In einem
  Projekt mit nur einer PLC (K81, live am 2026-11-03) hat `plc` kein Ziel; der einzige Offline-Vergleich ist dann der mit einer
  Kopiervorlage: die Station oder CPU zuerst in die Projektbibliothek legen (`library_write create_master_copy`) oder
  `compare_hardware_to_online` gegen das Gerät verwenden.
- **Online-Ziel.** Der `OnlineProvider` der CPU (`GetService<OnlineProvider>()` am CPU-Geräteelement) wird nur dann als Ziel
  übergeben, wenn er `IHardwareCompareTarget` zur Laufzeit umsetzt; die Prüfung läuft, bevor der Legitimations-Handler angehängt
  wird. Eine PLC ohne Online-Provider ist `unsupported_capability`. Der Ablauf ist der von `compare_to_online`
  (`PlcCompareService.RunOnline`): ausfallsicherer Legitimations-Handler (Passwörter nur aus `passwords` und der Passwortzuordnung
  des Servers, eine TLS-Prüfung nur aus `trustedCertificates`, nie `NonVerified`: die PLCSIM-Ausnahme K52 gilt nicht für Vergleiche),
  TIA-Portal-Bestätigungsdialoge mit Nein beantwortet und `Offline` mit `GoOffline()` wiederhergestellt, wenn der Vergleich eine
  Verbindung geöffnet hinterlassen hat; eine abgelehnte Abfrage verwirft das Ergebnis mit demselben Nachweis.
- **Offene Frage für den Live-Test.** Ist das Online-Hardwarevergleichsziel von TIA Portal ein anderes Objekt als der
  `OnlineProvider`, scheitert die Online-Operation mit `unsupported_capability`, ohne das Gerät zu kontaktieren; kein anderes Objekt
  wird geraten.

Einträge von Beobachtungs- und Forcetabellen werden schemafrei gelesen: Die V21-Referenz nennt die Eigenschaft `Entries`,
dokumentiert aber keinen Eintrags-Composition-Typ, daher liest der Worker `Entries` per Reflection am Laufzeit-
objekt (das Siemens-Muster object-tree-walking) und die dokumentierten Attribute jedes Eintrags über
`IEngineeringObject.GetAttribute(name)`. Zeichenfolgen werden als Text geliefert, Enums mit dem Member-Namen, Booleans
als Booleans; ein Attribut, das eine Ausnahme wirft oder einen nicht skalaren Wert liefert, wird in `unreadableAttributes` aufgeführt,
und sein Member ist null.

## `plc_write`-Operationen

Dieselben Regeln für Pipeline, Reihenfolge, 50 Elemente, Stopp beim ersten Fehlschlag und kein Rollback wie für jedes `plc_write`-
Element, keine Rückfrage in read-write. Gruppenpfade verwenden `folderPath` (`/` oder weggelassen für die Systemgruppe).

| Operation | Felder | Siemens-Aufruf | Hinweise |
| --- | --- | --- | --- |
| `create_watch_table` | `tableName`, optional `plcName`, `folderPath` | `group.WatchTables.Create(name)` | die Gruppe muss existieren (geplant oder im Inventar); Kollisionen sind die Ablehnung durch das Portal |
| `delete_watch_table` | `tableName`, optional `plcName`, `folderPath` | `PlcWatchTable.Delete()` | Info-Guard `plc_deletes_watch_table` |
| `create_watch_table_group` | `folderPath` (die neue Gruppe, z. B. `/Line/Cell`), optional `plcName` | `parent.Groups.Create(name)` | die übergeordnete Gruppe muss existieren |
| `delete_watch_table_group` | `folderPath` (die Gruppe), optional `plcName` | `PlcWatchAndForceTableUserGroup.Delete()` | die Systemgruppe kann nicht gelöscht werden; der Info-Guard führt die entfernten Tabellen und Gruppen auf |
| `update_watch_table_content` / `update_force_table_content` | `tableName`, `content`, `expectedContentHash`, optional `plcName`, `folderPath` | `group.WatchTables.Import(file, ImportOptions.Override)` / `group.ForceTables.Import(...)` | `content` ist das bearbeitete SimaticML aus `get_*_table_content`; der Hash wird bei der Planung und erneut vom Worker vor dem Import geprüft (`state_changed`, wenn veraltet); vor dem Import muss das Dokument genau die adressierte Tabelle benennen (ein Tabellenelement derselben Art und `AttributeList/Name`, Form übernommen aus dem eigenen Export dieser Tabelle durch den Worker, geschlossenes Fehlschlagen, wenn der Export nicht erkennbar ist; DTDs abgelehnt), sonst `validation_error`, und nichts wird importiert; der Import muss außerdem genau die adressierte Tabelle liefern, sonst `postcondition_failed` (das Projekt wurde geändert) |
| `update_program` | optional `plcName` | `PlcSoftware.UpdateProgram()` innerhalb von `TiaPortal.ExclusiveAccess` und einer Projekt-`Transaction` (Quelle a: "within the normal ExclusiveAccess/transaction workflow") | aktualisiert Bausteinanweisungen offline auf die neueste Version, die die PLC-Firmware unterstützt; aufgerufen per exaktem Laufzeit-Lookup (ihr Rückgabetyp ist undokumentiert); die öffentlichen skalaren Member des zurückgegebenen Objekts werden gelesen (`returnedMembers`, `returnedType`): ein fehlschlagendes Zustands-, Fehleranzahl- oder Erfolgs-Member lässt den Aufruf fehlschlagen und die Transaktion unbestätigt (zurückgerollt); ein Member, das Erfolg meldet, ergibt `outcome: "succeeded"`; nichts Interpretierbares (einschließlich einer void-Rückgabe) ergibt `outcome: "uninterpreted"` mit einer Warnung (bestätigt, nicht verifiziert: übersetzen und prüfen); der Commit wird nur angefordert, wenn `Transaction.CanCommit` true ist, sonst scheitert der Aufruf und meldet, dass die Änderung zurückgerollt wurde (Audit T5, nicht live getestet). Live ausgeführt am 2026-11-07 (Build 986928f, CPU 1516-3 PN/DP V2.9): der Aufruf lief und meldete `outcome: "uninterpreted"`, wie vorgesehen |

Die Planung liest das Inventar der Beobachtungs- und Forcetabellen der PLC einmal pro Aufruf (nur, wenn ein Element es benötigt),
löst Update-/Löschziele auf (`target_not_found`/`target_ambiguous`), wendet frühere Elemente an, sodass spätere
sie sehen und `dependsOn` tragen, und blockiert mit `plc_state_unverifiable`, wenn das Inventar seine
Obergrenze erreicht hat. Inhaltsänderungen werden durch einen Re-Export verifiziert (`contentHash`, verwendbar als nächster
`expectedContentHash`); die anderen Elemente sind `trusted`.

**Einträge werden nur über den SimaticML-Round-Trip hinzugefügt, geändert und entfernt.** Die V21-Referenz
dokumentiert keinen Eintrags-Composition-Typ, kein `Create` für Einträge und kein `Delete` für Einträge (nur die Composition der Kommentar-
einträge hat ein `Create`, ohne dokumentierte Parameter), daher bietet das Tool keine typisierten Eintragsbearbeitungen.
Forcetabellen werden nur offline bearbeitet: Die Referenz dokumentiert kein `Create` an der Composition der Forcetabellen,
kein `Delete` an einer Forcetabelle und **keine Online-Force-API**, daher wird hier nichts online geforct oder
gesteuert. (`WatchAndForceTableAccessManager` und die Zugriffsregeltypen im V21-
Step7-Modul beschreiben den Webserver-Zugriff auf Tabellen, nicht das Forcen.)

## `plc_tia_online`

Nur in read-write und full registriert; nur Standalone-Projekte.

| Argument | Standard | Bedeutung |
| --- | --- | --- |
| `action` | erforderlich | `go_online` oder `go_offline` |
| `plcName` | keiner | Name der PLC-Software oder des Geräts; optional, wenn das Projekt genau eine PLC hat |
| `pcInterface` | keiner | nur `go_online`, **erforderlich** `{ name, number }`, wie `get_online_state` es aufführt; ein explizites `null` ist ein Fehler der Argumentform. Standardmäßig wird keine Schnittstelle gewählt, Hardware-Schnittstellen eingeschlossen. |
| `targetInterface` | keiner | nur `go_online`; optional, wenn die PC-Schnittstelle genau eine anbietet |
| `mode` | `PN/IE` | nur `go_online` |
| `timeoutSeconds` | 30 | 1-120; Polling-Budget für `Online`/`Offline` |
| `passwords` | keiner | nur `go_online`: `{ "<configuration type short name>": "<NAME of an environment variable of the server process>" }`; nie ein Passwort (siehe [Argument-Maps](#argument-maps-und-tia-portal-bestätigungen)) |
| `portalConfirmations` | `decline` | beide Aktionen: `decline` oder `accept`, siehe [TIA-Portal-Bestätigungen](#argument-maps-und-tia-portal-bestätigungen) |
| `trustedCertificates` | keiner | nur `go_online` (`go_offline` lehnt es ab): SHA-256-Fingerabdrücke der TLS-Zertifikate des Geräts, die der Benutzer freigegeben hat, siehe [TLS-Zertifikatsvertrauen](#tls-zertifikatsvertrauen-trustedcertificates); weggelassen oder `null` vertraut nichts |
| `dryRun` | false | Pfad und Guards auflösen, ohne zu verbinden oder nachzufragen |

`go_online` plant aus `get_online_state`: exakte ordinale Übereinstimmung von Modus, PC-Schnittstelle (Name und
Nummer) und Zielschnittstelle, kein Fallback. Der Worker löst dieselben Namen erneut an **derselben
`OnlineProvider`-Instanz** auf, die sie anwendet (Zielschnittstellen sind an die Instanz gebunden, aus der sie
aufgezählt wurden), registriert einen geschlossen fehlschlagenden `OnlineLegitimation`-Handler, ruft
`ConnectionConfiguration.ApplyConfiguration(target)` und, falls das true lieferte, `GoOnline()` auf und pollt dann
`OnlineProvider.State` alle 200 ms bis `Online`, einem Endzustand (`Incompatible`, `NotReachable`,
`Protected`) oder dem Timeout, weil `GoOnline()` zurückkehrt, bevor die Verbindung aufgebaut ist. Eine PLC,
die bereits `Online` ist, bleibt unberührt (`ApplyConfiguration` wirft eine Ausnahme, solange eine Verbindung aktiv ist):
Das Ergebnis meldet `outcome: "already_online"`, `path: null` und `pathVerified: false` (nichts wurde angewendet, und
die bestehende Verbindung wird nicht gegen den angeforderten Pfad verifiziert) mit einer `note`-Warnung. Eine PLC im Zustand
`Connecting` oder `Disconnecting` schlägt mit `state_changed` fehl, bevor etwas angewendet wird. `go_offline` ruft
`GoOffline()` auf (kein Pfad nötig) und pollt auf `Offline`; eine bereits offline befindliche PLC wird ohne
Aufruf gemeldet.

Die Online-Legitimation wird nur vom Aufrufer beantwortet: Passwortkonfigurationen durch `passwords`-Einträge
(`SetPassword(SecureString)` aus der benannten Umgebungsvariable, der Wert wird nie festgehalten), die TLS-
Zertifikatsprüfung (`TlsVerificationConfiguration`) nur durch `trustedCertificates` (`Trusted`, wenn der SHA-256-Fingerabdruck jedes
vorgelegten Zertifikats aufgeführt ist, sonst `NonTrusted` und Abbruch mit den
Zertifikatsbelegen; siehe [TLS-Zertifikatsvertrauen](#tls-zertifikatsvertrauen-trustedcertificates)). Jede
andere vorgelegte Konfiguration (zum Beispiel `OnlineAuthenticationConfiguration`) bricht die Verbindung ab;
`configurationsSeen` und `abort` des Ergebnisses nennen sie mit ihrer Art und, bei einer Auswahl, ihren
`availableSelections`. Die Rückfrage in read-write führt die Passwortschlüssel und jeden vertrauten Fingerabdruck auf.

Guards (informativ, die Rückfrage nennt sie): `online_hardware_target`, wenn die PC-Schnittstelle nicht
`PLCSIM` ist; `online_disconnects` für `go_offline` (es schließt auch eine in der Oberfläche von TIA Portal geöffnete
Verbindung).

Ergebniswert `PlcOnlineActionResultInfo`: `action`, `plcName`, `deviceName`, `path` (nur, wenn ein Pfad angewendet wurde),
`outcome` (`connected`, `already_online`, `configuration_not_applied`, `not_reached`; go_offline: `disconnected`,
`already_offline`, `not_reached`; null in Fehlerbelegen), `pathVerified` (nur für `connected` true), `stateBefore`,
`stateAfter`, `goOnlineReturned`, `configurationApplied`, `goalReached`, `timedOut`, `timeoutSeconds`,
`elapsedMilliseconds`, `statesObserved` (verschiedene aufeinanderfolgende Zustände, höchstens 50),
`configurationsSeen`, `abort`, `note`, `portalConfirmationPolicy`, `portalConfirmations`,
`portalConfirmationsObserved`, `portalConfirmationsComplete`. Wurzel-`success` ist nur dann true, wenn der Zielzustand
erreicht wurde, nichts abgebrochen wurde und keine TIA-Portal-Bestätigung abgelehnt wurde. Ein nicht erreichtes Ziel ist `phase: applied`, `success: false`, `isError: false`, das
Element `status: failed` (`worker_operation_failed`, mit Nennung des Endzustands) mit den Belegen in
`result.value`; TIA Portal kann nach einem Timeout noch verbinden (zum Abbrechen offline gehen).

## `plc_station_upload`

Nur in read-write und full registriert; nur Standalone-Projekte.

| Argument | Standard | Bedeutung |
| --- | --- | --- |
| `address` | erforderlich | Geräteadresse, die an der PC-Schnittstelle angelegt wird (`Addresses.Create(address)`), z. B. `192.168.0.1`; nicht leer, höchstens 64 Zeichen, keine Steuerzeichen oder umgebenden Leerzeichen; das Format beurteilt TIA Portal |
| `pcInterface` | erforderlich | `{ name, number }`, wie `list_station_upload_interfaces` es aufführt |
| `mode` | `PN/IE` | exakter Verbindungsmodus |
| `decisions` | keiner | `{ "<configuration type short name>": "<enum member name>" }`, z. B. `{ "UploadMissingProducts": "NoAction" }` |
| `passwords` | keiner | `{ "<configuration type short name>": "<environment variable NAME>" }`, z. B. `{ "ModuleReadAccessPassword": "PLC_READ_PW" }` |
| `portalConfirmations` | `decline` | `decline` oder `accept`, siehe [TIA-Portal-Bestätigungen](#argument-maps-und-tia-portal-bestätigungen) |
| `trustedCertificates` | keiner | SHA-256-Fingerabdrücke der TLS-Zertifikate des Geräts, die der Benutzer freigegeben hat, siehe [TLS-Zertifikatsvertrauen](#tls-zertifikatsvertrauen-trustedcertificates); weggelassen oder `null` vertraut nichts |
| `dryRun` | false | Pfad und Guards auflösen, ohne zu verbinden oder nachzufragen |

Der Worker löst Modus und PC-Schnittstelle exakt an demselben `StationUploadProvider` auf, der hochlädt,
legt die Adresse an und ruft `StationUpload(address, callback)` auf (nie innerhalb einer Transaktion). Unmittelbar
vor `StationUpload` abonniert er einen `OnlineLegitimation`-Handler an der `Configuration` desselben Providers
(der `ConnectionConfiguration`-Instanz, die den Pfad aufgelöst hat) und entfernt ihn in
`finally`. Der Upload-Callback und der Legitimations-Handler teilen sich einen Siemens-freien `PlcCallbackRecorder`,
sodass beide Arten von Konfiguration in einer `configurationsSeen`-Liste mit einem `abort` landen:

- der kanonische Typ ist der erste öffentliche Typ in der Laufzeit-Typkette der Konfiguration, dessen
  Namespace `Siemens.Engineering.Upload.Configurations` oder (Online-Legitimation)
  `Siemens.Engineering.Online.Configurations` ist; sein kurzer Name ist der Entscheidungsschlüssel;
- die TLS-Zertifikatsprüfung (`TlsVerificationConfiguration`) wird nur aus `trustedCertificates` beantwortet
  (nie durch eine Entscheidung; ein `decisions`- oder `passwords`-Schlüssel `TlsVerificationConfiguration` ist ein
  `validation_error`);
- eine Auswahlkonfiguration (genau ein öffentliches les- und beschreibbares Enum `CurrentSelection`) wird
  nur durch einen `decisions`-Eintrag beantwortet, dessen Wert ein exakter Member-Name des Live-Enums ist;
- eine Passwortkonfiguration (genau ein öffentliches `SetPassword(SecureString)`) wird nur durch einen
  `passwords`-Eintrag beantwortet, der eine gesetzte, nicht leere Umgebungsvariable benennt;
- alles andere bricht ab: kein Eintrag (`decision_missing`), ein Eintrag der falschen Art
  (`decision_not_applicable`), eine Auswahl, die kein Member ist (`selection_unavailable`), eine nicht gesetzte Variable
  (`password_unavailable`), eine Antwort, die eine Ausnahme wirft (`answer_rejected`; ein verlorenes Portal behält `portal_lost`),
  ein nicht beantwortbarer oder fremder Typ (`unsupported_configuration`), mehr als 100 Konfigurationen
  (`configuration_limit_exceeded`), ein nicht freigegebenes TLS-Zertifikat (`certificate_not_trusted`), eine
  Passwortkonfiguration, deren `IsSecureCommunication` false oder nicht lesbar ist, ohne `allowInsecurePasswordTransfer: true`
  (`insecure_communication`, Audit T1, nicht live getestet: Das Passwort wird nicht gesendet; der Wert steht als
  `secureCommunication` am Eintrag; `go_online` und `plc_station_upload` nehmen die Zustimmung an,
  `compare_to_online`/`compare_hardware_to_online` nicht, siehe Liste des Ausgelassenen). Nach einem
  Abbruch wird jede spätere Konfiguration festgehalten und wirft eine Ausnahme.
- Audit T2 (nicht live getestet): Eine Auswahlkonfiguration, die mit `decision_missing`, `selection_unavailable` oder
  `decision_not_applicable` abbrechen würde oder nach dem Abbruch kommt und deren Live-Enum das exakte dokumentierte Member
  `NoAction` hat (z. B. `UploadMissingProducts`), wird mit `NoAction` beantwortet und OHNE Werfen als (oder nach dem)
  Abbruch festgehalten, wie die abgelehnte TLS-Frage (K27); der Aufruf scheitert trotzdem mit dem festgehaltenen Abbruch.
  Alles andere wirft weiter.

Der Abbruchdatensatz enthält `decisionKey`, `kind` und `availableSelections`, sodass eine Wiederholung die
exakte Entscheidung liefern kann. Check-Entscheidungen (`true`) werden nicht akzeptiert (`validation_error`, bevor etwas läuft): Der
V21-Upload-Namespace dokumentiert keine Check-Konfiguration.

Ergebniswert `PlcStationUploadResultInfo`: `address`, `path`, `state` (`UploadResult.State` nach Namen),
`errorCount`, `uploadedStationName`, `messages` (schemafrei: lesbare skalare Attribute über
`GetAttributeInfos`/`GetAttribute`, verschachtelte Meldungen über aufzählbare Engineering-Objekt-
Eigenschaften; höchstens 8 Ebenen, 500 Meldungen, je 20 Attribute), `messagesComplete`,
`messageOmission`, `configurationsSeen`, `abort`, `unusedDecisions`, `projectModified: true` und die vier
`portalConfirmation*`-Member. Erfolg erfordert ein zurückgegebenes Ergebnis, einen Zustand außer `Error`, `errorCount` 0, eine
hochgeladene Station, keinen Abbruch und keine abgelehnte TIA-Portal-Bestätigung; ein Erfolg warnt, dass das Projekt nicht gespeichert ist (`save_project`), ein Fehlschlag, dass eine teilweise
hochgeladene Station hinzugefügt worden sein kann (mit `browse_project_tree` prüfen). Der Guard
`station_upload_adds_device` (info) nennt die Folge. Der Host lehnt Belege ab (`protocol_error`),
deren festgehaltene Antworten nicht genau den Einträgen des Aufrufers entsprechen oder die einen anderen Pfad beschreiben.

**F-CPU (Live-Befund 2026-11-02, TIA Portal V21, CPU 1516F-3 PN/DP auf PLCSIM).** TIA Portal lädt eine F-CPU, in der
Oberfläche wie über Openness, nur dann hoch, wenn in der Safety Administration die Option „Konsistentes Laden von der F-CPU
ermöglichen“ aktiviert und in die CPU geladen ist. Ohne sie lehnt TIA Portal das Hochladen ab („Das konsistente
fehlersichere Laden ... ist nicht aktiviert“). Mit ihr lief `plc_station_upload` mit 0 Fehlern, und die F-Signaturen der
hochgeladenen Station waren identisch mit der Quelle (0x455072D1 / 0xB241A1AD / 0x930ED124). `plc_read`
`get_safety_administration` meldet diese Option nicht: Sie war über die V21-API, so wie gelesen, nicht sichtbar.

## Argument-Maps und TIA-Portal-Bestätigungen

`passwords` (beide Tools) und `decisions` (`plc_station_upload`) werden über die Argument-Maps von `plc_download`
gebunden (`PlcDownloadPasswordMap`, `PlcDownloadDecisionMap`): Ein doppelter Schlüssel oder ein nicht skalarer Wert ist ein Fehler der Argument-
form, und ein `passwords`-Wert, der kein Umgebungsvariablenname ist (zum Beispiel ein eingefügtes Passwort), oder ein
gültiger Name, dessen Variable in der Umgebung des Servers nicht gesetzt ist, wird beim Binden durch einen Schwärzungsplatzhalter
ersetzt, sodass er nie die Pipeline, die Antwort oder das Audit erreicht (die Guarded Pipeline auditiert die angeforderten
Elemente), und die Validierung lehnt ihn ab, ohne ihn zu wiederholen. Ein Name wird nur festgehalten, wenn seine Variable gesetzt ist; auch das
Detail `password_unavailable` des Workers wiederholt den Namen nie. Ein `decisions`-Wert `true` (ein Check)
wird gebunden, aber von `plc_station_upload` abgelehnt. Ein `decisions`- oder `passwords`-Schlüssel `TlsVerificationConfiguration`
wird von jedem Tool abgelehnt (`validation_error`: Einem Gerätezertifikat wird nur über
`trustedCertificates` vertraut).

`portalConfirmations` funktioniert genau wie bei `plc_download` (Vertrag 2.0) und verwendet dessen Typen wieder: Der Worker öffnet
`TiaPortalSession.AnswerConfirmations` um die Aktion, sodass jeder TIA-Portal-`Confirmation`-Dialog, der erscheint, bis die
Belege der Aktion gesammelt sind, durch die Policy beantwortet und festgehalten wird (`PlcDownloadPortalConfirmationLog`, höchstens
100 aufgeführt, alle gezählt; Typ und der Dialogtext, wenn lesbar):

- `decline` (Standard): mit Nein beantwortet; jeder abgelehnte Dialog lässt den Aufruf fehlschlagen (`status: failed`,
  `worker_operation_failed`), mit den Belegen in `result.value.portalConfirmations`;
- `accept`: mit Ja beantwortet (nie in read-only, wo die Sitzung jede Bestätigung verweigert) und festgehalten; der Aufruf
  schlägt deswegen nicht fehl und trägt eine Warnung.

Ein Dialog, der erscheint, nachdem der Bereich geschlossen wurde (zum Beispiel während ein abgelaufenes `go_online` noch verbindet), ist nicht in den
Belegen des Aufrufs, behält aber die Antwort des Aufrufs: Der Bereich bleibt bestehen, bis die nächste Worker-Anfrage beginnt, protokolliert jeden verspäteten
Dialog sofort und meldet sie als Warnung in dieser nächsten Antwort. `go_online` lässt außerdem bis dahin einen geschlossen fehlschlagenden
`OnlineLegitimation`-Handler registriert, der jede verspätete Passwort-, TLS- oder
Authentifizierungsabfrage ablehnt (und meldet). Die Bestätigungsrückfrage
in read-write nennt die Policy. `compare_to_online` öffnet denselben Bereich mit festem Nein (ein Lesevorgang stimmt einem
Dialog nie zu, daher nimmt er keine Option): Ein abgelehnter Dialog verwirft den Vergleich und lässt das Element fehlschlagen, mit den Dialogtypen und
-texten in der Meldung; zuerst mit `plc_tia_online` online gehen und solche Dialoge dort beantworten. Der Host prüft die
Belege wie der Download (Policy gleich der Anforderung, Anzahlen konsistent, jede Antwort passt zur Policy)
und lehnt alles andere als `protocol_error` ab. Die Belege jeder Online-Aktion laufen weiterhin auf
`WorkerResponse.PlcDownloadOutcome` als Payload-Text; jede Domäne dekodiert sie mit ihrem eigenen strengen Vertrag, sodass
Belege einer Online-Aktion nie als Download-Belege dekodiert werden (getestet).

## TLS-Zertifikatsvertrauen (`trustedCertificates`)

> **Nicht live getestet.** Hinzugefügt nach einem Live-Befund mit `plc_download` (TIA Portal V21, PLCSIM Advanced 8.0:
> Die Verbindung schlug fehl, während die eigene Oberfläche von TIA Portal zuerst fragt, ob dem unbekannten
> selbstsignierten Zertifikat des Geräts vertraut werden soll). Dieselben Regeln gelten für `plc_download` (siehe die
> [Download-Referenz](PLC_DOWNLOAD_SUMMARY.md#tls-certificate-trust-online-legitimation)). Nur Offline-
> Qualifizierung: Unit-Tests der Siemens-freien Regel und der Recorder (`PlcTlsTrustPolicyTests`,
> `PlcCallbackRecorderTlsTests`) und die FakeWorker-Szenarien `plc-online-tls` und `plc-download-tls`
> (`PlcTlsTrustMcpTests`), die über die Produktions-Recorder eine Ersatz-`TlsVerificationConfiguration` mit zwei festen Test-
> zertifikaten (einem Leaf `CN=PLC_1` und seiner CA) vorlegen.

`plc_tia_online` `go_online`, `plc_station_upload` und `plc_read` `compare_to_online` registrieren einen
`OnlineLegitimation`-Handler an der `ConnectionConfiguration`, die sie verwenden (`OnlineProvider.Configuration`
bzw. `StationUploadProvider.Configuration`), entfernen ihn in `finally` und halten jede vorgelegte
Konfiguration in `configurationsSeen` fest (Quelle b, "TLS legitimation and online authentication").

- **PLCSIM ohne Zertifikat (K52, Entscheidung des Eigentümers 2026-11-02).** Live V21 2026-11-02, `plc_station_upload`
  von einer PLCSIM-Advanced-Instanz: TIA Portal stellte die TLS-Frage dreimal mit `VerificationInfo`
  „certificate not matching“ und ganz ohne Zertifikat; kein Fingerabdruck war prüfbar, NonTrusted ließ den Upload
  scheitern. Für `go_online` und `plc_station_upload` über die PC-Schnittstelle genau `PLCSIM` wird eine Frage mit
  lesbaren Mitgliedern und null Zertifikaten mit `NonVerified` beantwortet (ohne Prüfung weiter; `tls.selectionSet`
  `NonVerified`, `tls.source` `plcsimNoCertificate`; `PlcTlsTrustPolicy.AllowsNonVerifiedOnPlcSim`), und der Host-Vertrag
  nimmt diese Antwort nur für einen PLCSIM-Pfad an. Ein vorgelegtes Zertifikat behält die Fingerabdruck-Regel; jede
  andere PC-Schnittstelle, `plc_download` und `compare_to_online` lehnen weiter ab. Noch nicht live getestet.

- **Argument.** `trustedCertificates`: ein JSON-Array von SHA-256-Fingerabdrücken (64 hexadezimale Zeichen,
  ohne Beachtung der Groß-/Kleinschreibung; Doppelpunkte und Leerzeichen werden ignoriert), höchstens 16, keine Wiederholungen nach der Normalisierung. Ein
  Nicht-Array oder ein Element, das keine Zeichenfolge ist, ist ein Fehler der Argumentform; ein fehlerhafter Fingerabdruck, eine Wiederholung oder mehr als 16 sind ein `validation_error`, bevor der
  Worker gefragt wird. `go_offline` und `compare_software` lehnen das Argument ab. Die normalisierte Liste
  (Großbuchstaben, keine Trennzeichen, Reihenfolge des Aufrufers) ist Teil der Effekte (`effects.trustedCertificates` beider
  Tools), wird in der read-write-Bestätigungsrückfrage aufgeführt (`plc_read` hat keine Rückfrage) und damit im
  Audit-Datensatz; Fingerabdrücke sind keine Geheimnisse.
- **Regel** (`PlcTlsTrustPolicy`, Contracts, gemeinsam genutzt von Host, Worker und FakeWorker). Die Belege werden zuerst
  per Reflection am Laufzeitobjekt gelesen: `PlcName` und `VerificationInfo` als Text, jedes
  `Certificates[i].Certificate` (`X509Certificate2`) mit SHA-256 über `RawData`, SHA-1-Thumbprint,
  Subject, Issuer, `NotBefore`, `NotAfter`; `CurrentSelection` muss genau eine öffentliche les- und beschreibbare
  Eigenschaft des dokumentierten Enums `TlsVerificationConfigurationSelection` sein. `CurrentSelection` wird
  nur dann `Trusted`, wenn jedes Member lesbar war, mindestens ein und höchstens 16 Zertifikate vorgelegt wurden
  und der SHA-256 **jedes** vorgelegten Zertifikats aufgeführt ist. Andernfalls wird `NonTrusted` gesetzt (nie in
  ein fehlendes oder fremdes `CurrentSelection` geschrieben). **Wenn `NonTrusted` gesetzt wurde, kehrt der Callback normal zurück (K27,
  seit 2026-11-02):** Der Eintrag wird als `selected` `NonTrusted` festgehalten, `abort` wird mit den Belegen festgehalten,
  TIA Portal lässt die Verbindung selbst fehlschlagen, und `go_online`, `plc_station_upload` und `compare_to_online`
  schlagen sauber mit `abort.tls` fehl, gleich was `GoOnline`/`StationUpload`/der Vergleich dann tut (zurückkehren, eine Ausnahme werfen,
  `State` `NotReachable`); `go_online` kann `State` dennoch bis zu seinem Timeout pollen. Nur wenn `NonTrusted`
  nicht gesetzt werden kann, wirft der Callback eine Ausnahme, was die Operation abbricht (Quelle b). Live-Beobachtung
  2026-11-02 (`MCP_Safety_01`, PLC_F1, PLCSIM #1, siehe die [PLC-Download-Referenz](PLC_DOWNLOAD_SUMMARY.md#tls-certificate-trust-online-legitimation)):
  Mit der früheren Ausnahme endete `go_online` in `portal_lost`, und TIA Portal wurde beendet, während
  "Verbindung abbrechen" in der TIA-Oberfläche (dasselbe NonTrusted) es weiterlaufen ließ. Die Ablehnung ohne Ausnahme ist
  nicht live getestet. Ob eine Ausnahme für eine unbeantwortete Upload- oder Passwortkonfiguration TIA
  Portal ebenfalls beenden kann, ist durch keinen Live-Beleg gestützt; sie ist unverändert (kein dokumentierter Abbruch ohne Ausnahme). Eine spätere TLS-Prüfung
  nach einem Abbruch wird erneut ohne Ausnahme mit `NonTrusted` beantwortet.
  Kein Standardvertrauen, kein Allen-Vertrauen, nie `EnableLegacyCommunication`. Ein `decisions`-/`passwords`-Eintrag mit dem Schlüssel
  `TlsVerificationConfiguration` ist ein `validation_error`, sodass eine einfache Entscheidung nie `Trusted` auswählen kann.
- **Belege.** Der Konfigurationseintrag und, bei einer Ablehnung, der `abort` tragen `tls { plcName,
  verificationInfo, certificates[{ sha256, sha1Thumbprint, subject, issuer, notBefore, notAfter,
  approved }], certificatesPresented, certificatesComplete, unreadableMembers, selectionSet, source }`;
  der Abbruch hat `decisionKey: "TlsVerificationConfiguration"` und den Grund `certificate_not_trusted`
  (`decision_not_applicable`, wenn Belege unlesbar waren oder `CurrentSelection` fehlt oder fremd ist,
  `selection_unavailable`, wenn dem Live-Enum `Trusted` fehlt, `answer_rejected`, wenn das Setzen eine Ausnahme warf). Die Fehlermeldung (`PlcCallbackRecorder.AbortMessage`) führt
  jedes Zertifikat auf, weist den Agenten an, den **Benutzer** die Fingerabdrücke mit dem Zertifikat des Geräts
  vergleichen zu lassen (TIA Portal "Zertifikate anzeigen") und erst dann mit
  `trustedCertificates: [...]` zu wiederholen, das jeden vorgelegten Fingerabdruck aufführt.
- **Host-Prüfungen** (`PlcOnlinePayloadContract.ValidateCallbacks`): `protocol_error`, wenn ein Nicht-TLS-Eintrag
  `tls` trägt, ein `approved`-Flag von der geplanten Liste abweicht, `Trusted` nach einem Abbruch oder
  mit Belegen festgehalten wird, die es nicht zulassen, eine `NonTrusted`-Antwort ohne `tls.selectionSet: NonTrusted`
  oder ohne einen `abort` kommt (sie verankert den Abbruch wie ein abgebrochener Eintrag), der `tls` des Abbruchs von dem des
  ersten abgebrochenen oder abgelehnten Eintrags abweicht, oder `certificate_not_trusted` mit Belegen kommt, die `Trusted` zulassen würden.
  Ein gelieferter `compare_to_online` darf keinen Abbruch haben; seine Fehlerbelege dürfen keinen Baum haben.
- **Verspätete Abfragen.** Der Handler, den `go_online` bis zur nächsten Anfrage bestehen lässt, hat keine vertrauten
  Fingerabdrücke: Eine TLS-Prüfung, die erscheint, nachdem der Aufruf zurückgekehrt ist, wird abgelehnt und als Warnung gemeldet.

## Obergrenzen und Budgets

| Obergrenze | Wert |
| --- | --- |
| Vergleichstiefe / -elemente | 1-64 (Standard 16) / 1-5000 (Standard 300) |
| Vergleichs- und Callback-Text | 1.000 Zeichen |
| Beobachtungs-/Force-Einträge pro Lesevorgang | 1-5000 (Standard 1000) |
| Tabellen + Gruppen pro PLC-Auflistung | 5.000 |
| Timeout von GoOnline/GoOffline | 1-120 s (Standard 30), gepollt alle 200 ms; Wiederherstellung nach dem Vergleich 10 s |
| beobachtete Online-Zustände | 50 |
| Callback-Konfigurationen | 100 (die 101. bricht ab) |
| Entscheidungen + Passwörter pro Aufruf | 64; Schlüssel und Auswahlen sind Bezeichner, Variablennamen höchstens 128 Zeichen |
| `trustedCertificates` | 16 Fingerabdrücke; pro TLS-Prüfung gelesene vorgelegte Zertifikate: 16 (mehr werden nie vertraut) |
| festgehaltene TIA-Portal-Bestätigungen | 100 pro Aufruf aufgeführt, alle gezählt |
| Upload-Meldungen | 8 Ebenen, 500 Meldungen, je 20 Attribute |

Das Timeout der Worker-Anfrage (`--worker-timeout-minutes`, Standard 5) begrenzt jeden Aufruf. Die Dokumente
verwenden die effektiven Wert-/Dokumentbudgets (`--max-item-chars`, `--max-document-chars`); ein übergroßer
Ergebniswert wird als Ganzes mit Hinweisen ausgelassen, nie abgeschnitten.

## Herkunft jedes verwendeten Siemens-Members

Quellen (schreibgeschützte Referenzdaten, nie ausgeführt): **(a)** `totally-integrated-claude`
`skills/tia-plc-operations/references/compare.md`; **(b)** `.../references/online-status.md`;
**(c)** `.../references/download-upload.md`; **(d)** `.../references/watch-force-tables.md`;
**(e)** `skills/tia-networks/references/online-connection-configuration.md` (b, c und e geben an, dass sie
gegen die installierte V21-XML-API-Dokumentation geprüft wurden; a zitiert das Kapitel des V21-Openness-Handbuchs,
und d ist ein Auszug aus der V21-Step7-API-Referenz); **(f)** Siemens `tia-portal-ai-extensions`
`openness_development/skills/online-and-download/SKILL.md`; **(g)** Siemens `engineering-objects` und
`object-tree-walking` `SKILL.md`; **(h)** die V21-Typnamen-Baseline, extrahiert aus echten DLL-Metadaten
(`api-baselines/v21/base.json`, `step7.json`; nur Typnamen und Assemblies).

Status: **Signatur aus der Quelle** (die Quelle zeigt das Member mit seinen Typen oder seiner Aufrufform);
**Name aus der Quelle, Typ angenommen** (die Quelle nennt das Member; der Stub-Typ folgt den
Composition-/Gruppenmustern des Repositorys und muss die Übersetzung gegen die echten DLLs bestehen); **Laufzeit-Lookup** (das
Member wird zur Laufzeit über den exakten Namen erreicht, nie einkompiliert); **vorhanden** (eine unveränderte vorhandene
Stub-Deklaration). Jeder Typ unten liegt in der Baseline-Assembly, in der die Stubs ihn deklarieren (h), festgeschrieben
durch `PlcOnlineReferenceStubTests`.

| Siemens-Member | Assembly | Quelle | Status |
| --- | --- | --- | --- |
| `PlcSoftware.CompareToOnline()` → `CompareResult` | Step7 | a | Signatur aus der Quelle |
| `PlcSoftware.CompareTo(ISoftwareCompareTarget)` → `CompareResult`; `PlcSoftware` implementiert `ISoftwareCompareTarget` | Step7 / Base | a | Signatur aus der Quelle |
| `CompareResult.RootElement` → `CompareResultElement` | Base | a | Signatur aus der Quelle |
| `CompareResultElement.ComparisonResult` → `CompareResultState` | Base | a | Signatur aus der Quelle |
| `CompareResultElement.LeftName`, `.RightName`, `.DetailedInformation` (`string`) | Base | a | Name aus der Quelle, Typ angenommen (`string`; die Quelle interpoliert sie) |
| `CompareResultElement.Elements` → `CompareResultElementComposition` (zählt `CompareResultElement` auf) | Base | a, h | Name aus der Quelle; Aufzählung aus dem `foreach` in a; Composition-Typ aus h |
| Member-Namen von `CompareResultState` (`ObjectsIdentical`, `FolderContentsIdentical`, …) | Base | a | Namen aus der Quelle, nach Namen verglichen (das Stub-Enum hat keine Member) |
| `Project.ProjectLibrary` → `ProjectLibrary`; `ProjectLibrary` als `ISoftwareCompareTarget` | Base | a | Name und Aufrufform aus der Quelle; deklarierender Typ `Project` und Eigenschaftstyp angenommen |
| `ProjectLibrary.MasterCopyFolder` → `MasterCopySystemFolder`, `.MasterCopies` → `MasterCopyComposition`, `.Find(string)` → `MasterCopy`; `MasterCopy` als `ISoftwareCompareTarget` | Base | a, h | Aufrufkette aus der Quelle; Eigenschaftstypen angenommen (h nennt die Typen) |
| `DeviceItem.GetService<OnlineProvider>()` am CPU-Geräteelement der PLC | Base | b, e, f | Signatur aus der Quelle; vorhandenes `GetService<T>` |
| `OnlineProvider.State` → `OnlineState` | Base | b, f | Signatur aus der Quelle |
| `OnlineProvider.GoOnline()` → `OnlineState` | Base | b | Signatur aus der Quelle (`OnlineState resultState = onlineProvider.GoOnline();`) |
| `OnlineProvider.GoOffline()` | Base | b, f | Name aus der Quelle, Rückgabetyp angenommen `void` (in der Probe als `Action` festgeschrieben) |
| `OnlineProvider.Configuration` → `ConnectionConfiguration` | Base | b, e, f | Signatur aus der Quelle |
| Member-Namen von `OnlineState` (`Offline`, `Connecting`, `Online`, `Disconnecting`, `Incompatible`, `NotReachable`, `Protected`) | Base | b | Namen aus der Quelle, nach Namen verglichen |
| `ConnectionConfiguration.IsConfigured` (`bool`) | Base | b, e | Signatur aus der Quelle |
| `ConnectionConfiguration.ApplyConfiguration(ConfigurationTargetInterface)` → `bool` | Base | e | Signatur aus der Quelle (`bool applied = config.ApplyConfiguration(target);`) |
| `ConnectionConfiguration.OnlineLegitimation` (Ereignis von `OnlineConfigurationDelegate`) an `OnlineProvider.Configuration` (go_online, compare_to_online) und `StationUploadProvider.Configuration` (Hochladen der Station) | Base | b | Signatur aus der Quelle (`+=`/`-=` desselben Delegates, Entfernen in `finally`); dass das Hochladen der Station seine Legitimation über die Konfiguration seines Providers auslöst, ist nicht angegeben: angenommen aufgrund des gemeinsamen Typs `ConnectionConfiguration` — live verifizieren |
| `Online.Configurations.TlsVerificationConfiguration` (kanonischer Typ über den vollständigen Namen in der Laufzeit-Typkette; nie einkompiliert) | Base | b, h | Typname in h; Laufzeit-Lookup |
| `TlsVerificationConfiguration.PlcName`, `.VerificationInfo` | Base | b | Laufzeit-Lookup (Reflection); Namen aus der Quelle, Typen undokumentiert → als Text gelesen; fehlend oder Ausnahme werfend schlägt geschlossen fehl |
| `TlsVerificationConfiguration.Certificates` (`Online.Security.VerificationCertificateComposition` von `VerificationCertificate`, aufgezählt) | Base | b, h | Laufzeit-Lookup; Name aus dem Callback-Beispiel der Quelle, Namen von Composition- und Elementtyp in h, `IEnumerable`-Aufzählung angenommen (nicht aufzählbar schlägt geschlossen fehl) |
| `VerificationCertificate.Certificate` → `X509Certificate2` (`RawData`, `Thumbprint`, `Subject`, `Issuer`, `NotBefore`, `NotAfter` sind .NET-Member) | Base | b, h | Laufzeit-Lookup; Typ aus der Quelle ("exposing the target `X509Certificate2` through `Certificate`") |
| `TlsVerificationConfiguration.CurrentSelection` vom Typ `TlsVerificationConfigurationSelection`; Member `Trusted`, `NonTrusted` nach Namen gesetzt (`NonVerified` nie) | Base | b, h | Laufzeit-Lookup; Eigenschaft, Typ und Member aus der Quelle (Callback-Beispiel und Katalog), Typname in h; der vollständige Name des Eigenschaftstyps wird verglichen, Member mit `Enum.IsDefined` geprüft |
| `IsSecureCommunication` von `OnlinePasswordConfiguration`/`UploadPasswordConfiguration` | Base | members.txt | Laufzeit-Lookup (`PlcPasswordChannel`, Audit T1): vor jedem `SetPassword` gelesen; false oder nicht lesbar ohne `allowInsecurePasswordTransfer` bricht mit `insecure_communication` ab; nicht live getestet |
| `OnlineAuthenticationConfiguration`, `OnlineCredentials`, `UserType`, `AuthenticationType`, `OnlineAuthenticationConfiguration.IsSecureCommunication`, `EnableLegacyCommunication` | Base | b, h | nicht verwendet (Authentifizierung bricht ab; `EnableLegacyCommunication` wird nie gelesen oder gesetzt) |
| `OnlineConfigurationDelegate` = `void (OnlineConfiguration)`; `Online.Configurations.OnlineConfiguration` | Base | b, h | Signatur aus der Quelle |
| `ConnectionConfiguration.Modes`, `ConfigurationMode.Name/.PcInterfaces`, `ConfigurationPcInterface.Name/.Number/.TargetInterfaces`, `ConfigurationTargetInterface.Name` | Base | b, e, f | vorhanden (plc_download) |
| `ConfigurationPcInterface.Addresses` → `ConfigurationAddressComposition`; `.Create(string)` → `ConfigurationAddress` | Base | b, c, e | Signatur aus der Quelle |
| `ProjectBase.GetService<StationUploadProvider>()` | Base | c | Signatur aus der Quelle (`project.GetService<StationUploadProvider>()`); vorhandenes `GetService<T>` |
| `StationUploadProvider.Configuration` → `ConnectionConfiguration` | Base | c | Signatur aus der Quelle |
| `StationUploadProvider.StationUpload(ConfigurationAddress, UploadConfigurationDelegate)` → `UploadResult` | Base | c | Signatur aus der Quelle |
| `UploadConfigurationDelegate` = `void (UploadConfiguration)`; `Upload.Configurations.UploadConfiguration.Message` | Base | c, h | Signatur aus der Quelle; Typ von `Message` angenommen `string` |
| `UploadResult.State` (`UploadResultState`), `.ErrorCount` (`int`), `.UploadedStation` (`Device`), `.Messages` (`UploadResultMessageComposition` von `UploadResultMessage`) | Base | c, h | Signatur aus der Quelle für `State`/`UploadedStation`; Typ von `ErrorCount` und die Aufzählung der Meldungs-Composition angenommen |
| `UploadResultState.Error` | Base | c | Name aus der Quelle, nach Namen verglichen |
| `Device.Name` (hochgeladene Station) | Base | vorhanden | vorhanden |
| Attribute von `UploadResultMessage` über `IEngineeringObject.GetAttributeInfos()` / `EngineeringAttributeInfo.Name` / `GetAttribute(string)`; verschachtelte Meldungen über aufzählbare Engineering-Objekt-Eigenschaften | Base | g | Laufzeit-Lookup (schemafrei; kein Meldungs-Member ist einkompiliert) |
| Auswahl-Callbacks `CurrentSelection` (z. B. `UploadMissingProducts`, Member `NoAction`, `TryUpload`) | Base | c (b für den Online-Namespace) | Laufzeit-Lookup (Reflection am Live-Objekt; Enum nach exaktem Member-Namen geparst) |
| Passwort-Callbacks `SetPassword(SecureString)` (Spezialisierungen von `UploadPasswordConfiguration`, `OnlinePasswordConfiguration`, `OnlineReadAccessPassword`) | Base | b, c | Laufzeit-Lookup (Reflection; eine öffentliche Methode, die `SecureString` nimmt) |
| Kanonische Callback-Typen in `Siemens.Engineering.Upload.Configurations` / `.Online.Configurations` | Base | b, c, h | Typnamen aus h; Durchlauf der Laufzeit-Typkette |
| `PlcSoftware.WatchAndForceTableGroup` → `PlcWatchAndForceTableSystemGroup` | Step7 | a, d | Name aus der Quelle ("Get the Plc watch table system group"), Typ aus dieser Zusammenfassung angenommen |
| `PlcWatchAndForceTableGroup.WatchTables` (`PlcWatchTableComposition`), `.ForceTables` (`PlcForceTableComposition`), `.Groups` (`PlcWatchAndForceTableUserGroupComposition`), `.Name` | Step7 | d, h | Namen aus der Quelle; Typen aus den Zusammenfassungen und h; davon abgeleitete System-/Anwendergruppen angenommen (Muster der Baustein-/Variablengruppen) |
| `PlcWatchAndForceTableUserGroup.Delete()`; Aufzählung von `PlcWatchAndForceTableUserGroupComposition` und `.Create(string)` | Step7 | d | Name aus der Quelle; Rückgabetypen angenommen |
| Aufzählung von `PlcWatchTableComposition`, `.Create(string)` → `PlcWatchTable`, `.Import(FileInfo, ImportOptions)` → `IList<PlcWatchTable>` | Step7 | d | Parameter aus der Quelle; Rückgabetypen angenommen (Muster der Baustein-Composition) |
| `PlcWatchTable.Name`, `.IsConsistent` (`bool`), `.Export(FileInfo, ExportOptions)`, `.Delete()` | Step7 | d | Parameter aus der Quelle; Typ von `IsConsistent` und `void`-Rückgaben angenommen |
| Aufzählung von `PlcForceTableComposition`, `.Import(FileInfo, ImportOptions)` → `IList<PlcForceTable>`; `PlcForceTable.Name`, `.IsConsistent`, `.Export(FileInfo, ExportOptions)` | Step7 | d | Parameter aus der Quelle; Rückgabe- und Eigenschaftstypen angenommen |
| `PlcWatchTable.Entries`, `PlcForceTable.Entries` | Step7 | d, g | Name aus der Quelle; Laufzeit-Lookup (Reflection; der Composition-Typ ist undokumentiert und nicht deklariert) |
| Eintragsattribute `Name`, `Address`, `DisplayFormat`, `MonitorTrigger`, `ModifyIntention`, `ModifyTrigger`, `ModifyValue` (Beobachtung) und `ForceIntention`, `ForceValue` (Force) | Step7 | d, g | Namen aus der Quelle; Laufzeit-Lookup (`GetAttribute`) |
| `ImportOptions.Override`, `ExportOptions.None` | Base | vorhanden | vorhanden |
| `PlcSoftware.UpdateProgram()` | Step7 | a | Name und leere Parameterliste aus der Quelle; Rückgabetyp undokumentiert → Laufzeit-Lookup (`GetMethod("UpdateProgram", Type.EmptyTypes)`); zurückgegebene Member per Reflection gelesen (Laufzeit-Lookup) |
| `TiaPortal.ExclusiveAccess(string)`, `ExclusiveAccess.Transaction(ITransactionSupport, string)`, `Transaction.CommitOnDispose()` (update_program) | Base | a (Workflow), vorhanden | vorhanden (Subnetz-Lifecycle, VCI) |
| `EngineeringException`, `EngineeringTargetInvocationException`, `EngineeringObjectDisposedException`, `NonRecoverableException` | Base | vorhanden | vorhanden |
| TIA-Portal-Ereignis `Confirmation`, beantwortet über `TiaPortalSession.AnswerConfirmations` (`ConfirmationEventArgs.Result`, `ConfirmationResult.Yes/No`); Dialogtext über `PlcDownloadService.ReadConfirmationText` | Base | vorhanden (plc_download) | vorhanden; der Text ist ein Laufzeit-Lookup einer unverifizierten öffentlichen `Text`-Zeichenfolge |

Stub-Konventionen, die keine Siemens-Fakten sind: Die neuen Stub-Enums (`CompareResultState`,
`OnlineState`, `UploadResultState`) deklarieren keine Member (der Worker kompiliert keine Enum-Konstante), und
die Interface-Listen `IEngineeringObject`/`IEngineeringComposition` folgen dem vorhandenen Stub-Stil.
Parameternamen in Stub-Signaturen sind Platzhalter.

### Weggelassen, und warum

- **Vergleich mit globaler Bibliothek.** Die Quelle zeigt `CompareTo(globalLibrary)`, aber keinen Weg, eine
  geöffnete globale Bibliothek zu erhalten; eine zu öffnen wäre eine eigene, Lifecycle-ähnliche Aktion.
- **Kopiervorlagen unterhalb des Wurzelordners der Kopiervorlagen.** Die Quelle zeigt nur
  `MasterCopyFolder.MasterCopies.Find(name)`; kein Member zum Durchlaufen von Ordnern wird verwendet, und der eigene Name der
  Kopiervorlage wird nicht zurückgelesen.
- **Hardwarevergleich mit einem anderen Objekt als Station/CPU einer PLC, einer Kopiervorlage im Wurzelordner oder dem
  `OnlineProvider` der CPU.** `HardwareObject.CompareTo(IHardwareCompareTarget)` ist als `compare_hardware`/`compare_hardware_to_online`
  umgesetzt (Issue #7); die V21-XML nennt keinen Umsetzer von `IHardwareCompareTarget`, daher wird kein anderes Ziel geraten.
- **`GoOnline(ConfigurationAddress)`, `ApplyConfiguration(ConfigurationAddress)` (Gateway-/Subnetz-
  Pfade), `RHOnlineProvider`** sind dokumentiert, werden aber nicht verwendet: Der Pfad ist immer der exakte Modus → PC-
  Schnittstelle → Zielschnittstelle, den bereits der Download verwendet. Online gehen zur R/H-Primär- oder Reserve-CPU
  (`RHOnlineProvider.GoOnlineToPrimary/GoOnlineToBackup`, `PrimaryState`/`BackupState`, Audit N1) braucht ein neues Feld in
  `plc_tia_online` und ein Pfadinventar aus `RHOnlineProvider.Configuration` in `get_online_state`; das bleibt einem späteren
  Paket. Die Provider-Suche des Workers (`RhSystemProviders`: CPU-Geräteelement, dann Elternobjekte bis zum `Device`,
  Audit T3) deckt `RHOnlineProvider` dafür bereits per Name ab.
- **Antworten auf Online-Authentifizierung** (`OnlineAuthenticationConfiguration`, `OnlineCredentials`,
  `UserType`): Für dieses Tool existiert keine freigegebene Authentifizierungs-Policy, daher brechen diese Konfigurationen mit
  Belegen ab. TLS-Vertrauen wird beantwortet, aber nur aus den vom Benutzer freigegebenen `trustedCertificates` (siehe
  [TLS-Zertifikatsvertrauen](#tls-zertifikatsvertrauen-trustedcertificates)). `EnableLegacyCommunication` wird
  nie angefasst (ein TLS-Downgrade). `TlsVerificationConfigurationSelection.NonVerified` wird nie gesetzt.
- **Bindung von `PlcName` an das Ziel**: Die Quelle verlangt, `PlcName`, `VerificationInfo` und jedes
  Zertifikat an das freigegebene Ziel zu binden; hier sind die vom Benutzer freigegebenen Fingerabdrücke diese Bindung, und
  `PlcName`/`VerificationInfo` werden für den Benutzer gemeldet, nicht mit dem PLC-Namen des Projekts verglichen
  (der Name des Geräts in der Zertifikatsprüfung muss nicht dem Namen der PLC-Software im Projekt entsprechen).
- **`GetAccessibleDevices()`** (Live-Netzwerkerkennung) wird von diesen Operationen nie aufgerufen; nur
  `plc_read scan_accessible_devices` ruft es auf, an einer ausdrücklich benannten PC-Schnittstelle (siehe oben und
  [PLC_SOURCES_FOLDERS_SUMMARY.md](PLC_SOURCES_FOLDERS_SUMMARY.md#accessible-devices-scan_accessible_devices)).
- **Typisierte Eintragsbearbeitungen**: Kein Eintrags-Composition-Typ, kein `Create` und kein `Delete` für Einträge ist dokumentiert;
  Einträge ändern sich nur über den SimaticML-Round-Trip. Kommentareinträge
  (`PlcTableCommentEntryComposition.Create`, keine dokumentierten Parameter) werden nicht angeboten.
- **Anlegen/Löschen von Forcetabellen und Online-Forcen**: nicht dokumentiert (siehe oben). `ShowInEditor` (Oberfläche)
  und `CreateFrom(MasterCopy)` werden nicht verwendet.
- **Upload-Check-Entscheidungen** (`true`): Keine Upload-Check-Konfiguration ist dokumentiert.
  `UploadResult.WarningCount` ist nicht dokumentiert und wird nicht gemeldet.
- **"Offline aus online aktualisieren" nach einem Vergleich**: Die Quellen dokumentieren keine solche Vergleichsaktion;
  `UpdateProgram()` ist die dokumentierte Aktualisierung der Anweisungen und wird genau als solche angeboten.
- **`allowInsecurePasswordTransfer` für `compare_to_online`/`compare_hardware_to_online`** (Audit T1): Ein
  Vergleichspasswort über einen nicht als sicher gemeldeten Kanal bricht ohne Zustimmung mit `insecure_communication` ab.
  Die Zustimmung fehlt hier, weil die Host-Prüfung der Vergleichsnachweise die zusammengeführte Passwortliste nicht kennt
  (sie prüft Vergleichsnachweise gegen keine Passwörter), sodass ein `password_set`-Eintrag eines Vergleichs schon jetzt
  als `protocol_error` abgewiesen wird (offen, beim Beheben von T1 gefunden). Stimmt der Benutzer zu, zuerst
  `plc_tia_online go_online` mit der Zustimmung verwenden.
- **Eine Option `portalConfirmations` für `compare_to_online`**: Ein Lesevorgang stimmt einem TIA-Portal-Dialog nie zu,
  daher lehnt der Vergleich immer ab (und schlägt fehl, wenn er einen abgelehnt hat).

## Bekannte Grenzen und offene Risiken

- Nicht live getestet; jedes oben als *angenommen* markierte Member kann bei der Übersetzung gegen die echten DLLs scheitern, und die Laufzeit-Lookup-
  Member (`Entries`, Eintragsattribute, `UpdateProgram`, Callback-`CurrentSelection`/`SetPassword`,
  Meldungsattribute) können zur Laufzeit fehlen oder anders geformt sein (jedes schlägt geschlossen fehl:
  `target_kind_unsupported`, unlesbare Attribute oder ein abgebrochener Callback).
- `compare_to_online` kann selbst eine Verbindung öffnen; ob es die Legitimation über das Ereignis
  `OnlineLegitimation` der Verbindungskonfiguration auslöst (wo der geschlossen fehlschlagende Handler lauscht) oder auf
  einem anderen Weg (möglicherweise einem Dialog in der Oberfläche von TIA Portal), ist unbekannt. Das wiederherstellende `GoOffline` läuft nur, wenn der
  Zustand vorher nicht `Online` war und danach `Online` oder `Connecting` ist; eine Wiederherstellung, die fehlschlägt oder abläuft,
  lässt TIA Portal online und wird gemeldet, nicht wiederholt.
- TLS-Vertrauen ist nicht live getestet: Ob V21 die Vertrauensfrage über diese `OnlineLegitimation`-
  Ereignisse auslöst (Hochladen der Station: über die Konfiguration seines Providers), die Laufzeitformen von `PlcName`,
  `VerificationInfo` und `Certificates` und ob TIA Portal ein Vertrauen nach dem Aufruf beibehält, sind
  unverifiziert. Das Tool kann nicht verifizieren, dass ein Mensch die Fingerabdrücke geprüft hat: `full` hat keine Rückfrage, und
  `compare_to_online` (`plc_read`) fragt nie nach, sodass ein Agent Fingerabdrücke aus den Abbruch-
  belegen kopieren und selbstständig wiederholen kann; in read-write führen die Rückfragen von `go_online` und `plc_station_upload` sie auf.
- `go_online` lässt eine bereits online befindliche PLC unberührt und kann den Pfad dieser Verbindung nicht verifizieren; das Ergebnis sagt das
  (`already_online`, `path: null`, `pathVerified: false`) und zählt dennoch als Erfolg, weil die PLC online ist.
- Eine abgelehnte TIA-Portal-Bestätigung lässt den Aufruf fehlschlagen, nachdem die Aktion bereits gewirkt haben kann (zum Beispiel kann die
  PLC online sein); die Belege sagen, was geschehen ist, mit `get_online_state` prüfen. Ob TIA Portal für diese Aktionen überhaupt solche
  Dialoge auslöst, ist nicht dokumentiert.
- Ein Timeout von `go_online` lässt TIA Portal möglicherweise noch verbindend zurück; nichts bricht das
  automatisch ab.
- Die Planung liest eine andere Provider-Instanz als die Aktion (nur Namen überqueren diese Grenze); eine
  Pfadänderung zwischen Planung und Aktion wird durch die exakte erneute Auflösung des Workers erkannt
  (`state_changed`).
- Das Hochladen der Station fügt ein Gerät hinzu, auch wenn es später Fehler meldet; es gibt kein Rollback. Der
  `Addresses.Create`-Eintrag bleibt in der Konfiguration der PC-Schnittstelle.
- Der SimaticML-Import wird nur darauf geprüft, dass er genau die adressierte Tabelle benennt (Form kalibriert am
  eigenen Export des Workers); das Tool kann den Rest des Dokuments nicht gegen ein Schema validieren. Ein Import,
  der dennoch andere oder mehr Tabellen liefert, schlägt mit `postcondition_failed` fehl, nachdem das Projekt geändert wurde.
- Das Ergebnis von `update_program` wird über Member-Namen interpretiert (`State`/`Status`/`Result`, `ErrorCount`,
  `Success`/`Succeeded`); ein V21-Rückgabetyp mit anderen Membern ist `uninterpreted`. Ob TIA Portal ein
  `UpdateProgram()` innerhalb einer unbestätigten Transaktion vollständig zurückrollt, ist nicht verifiziert.
- Namen von Beobachtungstabellen und Gruppenpfade werden ohne Beachtung der Groß-/Kleinschreibung und eindeutig per Aufzählung zugeordnet;
  die eigenen Eindeutigkeitsregeln von TIA Portal sind nicht verifiziert.

## Live-Abnahmeprüfungen

Zuerst an einem Wegwerfprojekt mit PLCSIM (oder PLCSIM Advanced) ausführen, dann an einer Labor-CPU:

1. `plc_read get_online_state`: Die aufgeführten Modi/PC-Schnittstellen/Zielschnittstellen entsprechen dem TIA-Portal-
   Dialog "Go online"; `state` folgt der Oberfläche.
2. `plc_tia_online go_online` über `PLCSIM`: `statesObserved` endet in `Online`, die Oberfläche von TIA Portal zeigt
   die PLC online; dann kehrt `go_offline` zu `Offline` zurück. Mit bereits online befindlicher PLC wiederholen
   (den Hinweis "already online" erwarten).
3. `go_online` gegen eine S7-1500- oder PLCSIM-Advanced-Instanz mit TLS und einem unbekannten Zertifikat: bestätigen,
   dass `configurationsSeen` `TlsVerificationConfiguration` mit lesbarem `tls` zeigt (leere
   `unreadableMembers`), `selectionSet: NonTrusted`, den Eintrag `selected` `NonTrusted` (keine Ausnahme, K27), dass
   TIA Portal offline bleibt **und weiterläuft** (`bind_project list_portals`); die
   Fingerabdrücke mit "Zertifikate anzeigen" vergleichen; mit jedem Fingerabdruck in `trustedCertificates` wiederholen und
   `Trusted` (Quelle `caller`) und `Online` bestätigen. Für `compare_to_online` und
   `plc_station_upload` wiederholen und festhalten, ob ein Vertrauen in einen späteren Aufruf ohne das Argument fortbesteht.
4. `compare_to_online` mit der PLC offline und online; `disconnectedAfterCompare` prüfen, den Baum
   gegen den Vergleichseditor von TIA Portal und die Obergrenzen mit einem großen Programm.
5. `compare_software` gegen eine andere PLC, die Projektbibliothek und eine Kopiervorlage im Wurzelordner.
5a. `compare_hardware` zweier Stationen und zweier CPUs (eine mit anderer Firmware), gegen eine Kopiervorlage einer Station im
   Wurzelordner, und `compare_hardware_to_online` mit der PLC offline und online: festhalten, welche Ziele `IHardwareCompareTarget` umsetzen.
6. `list_watch_and_force_tables`, `read_watch_table`, `read_force_table`: bestätigen, dass `Entries` gefunden wird
   und jedes Attribut lesbar ist (keine `unreadableAttributes`), und die Werttypen (Enum-Namen,
   Booleans).
7. `get_watch_table_content` → bearbeiten (einen Eintrag hinzufügen, ändern, entfernen) → `update_watch_table_content`;
   `importedTables` und die Tabelle in der Oberfläche bestätigen; für die Forcetabelle wiederholen (nur offline).
8. `create/delete_watch_table(_group)` und `update_program` an einer Kopie; danach übersetzen.
9. `plc_station_upload` von einer PLCSIM-/Labor-CPU ohne Entscheidungen: `decisionKey`/`availableSelections` des Abbruchs
   festhalten; mit der Entscheidung wiederholen; das hochgeladene Gerät prüfen, übersetzen
   und nicht speichern, sofern nicht beabsichtigt.
10. In read-write bestätigen, dass jeder Aufruf von `plc_tia_online`/`plc_station_upload` einmal nachfragt und dass
    Ablehnen nichts berührt.
11. Eine TIA-Portal-Bestätigung während `go_online` und `plc_station_upload` provozieren (falls es für das
    Ziel eine gibt): Mit dem Standard `decline` schlägt der Aufruf fehl und führt sie auf, mit `accept` wird sie mit Ja beantwortet und
    aufgeführt; bestätigen, dass der Dialogtext erfasst wird (`text`, ein unverifiziertes Member) und dass `compare_to_online`
    fehlschlägt, statt zuzustimmen.
