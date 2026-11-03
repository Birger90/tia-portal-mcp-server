# Operationen für Projekt- und globale Bibliotheken (`library_read`, `library_write`)

**Sprache:** [English](LIBRARY_OPERATIONS_SUMMARY.md) · Deutsch

Diese Referenz behandelt die beiden Bibliotheks-Tools, die dieser Fork hinzufügt:

- `library_read` (jeder Zugriffsmodus): die Projektbibliothek und die geöffneten globalen Bibliotheken, Deskriptoren globaler
  Bibliotheken, den Typ-/Kopiervorlagen-Baum mit Paging, Typdetails, Typinstanzen, den Bibliothekstyp, von dem ein PLC-Baustein oder Datentyp eine Instanz ist, die Aktualisierungsprüfung und den Bibliotheksvergleich;
- `library_write` (read-write und full): Lebenszyklus globaler Bibliotheken (öffnen, dearchivieren, anlegen, speichern, speichern unter, archivieren, schließen),
  Kopiervorlagen (erfassen, kopieren, löschen, umbenennen, in eine PLC oder die Hardwarekonfiguration instanziieren), Typversionen (instanziieren, das Projekt aktualisieren, bearbeiten, freigeben, verwerfen, als Default setzen, löschen) und Umbenennungen von
  Typen und Typordnern; seit Issue #17 auch Aufräumen, Harmonisieren, Bibliotheksaktualisierung, Typ-Löschen und DoNotUse,
  Typ- und Kopiervorlagenordner, Typen und Versionen aus Dokumenten, Exporte von Typversionen und Detailvergleiche von Typen,
  Versionen und Kopiervorlagen ([unten](#aus-der-v21-api-beschreibung-ergänzte-operationen-issue-17)).

> **Status: nicht live getestet.** Diese Funktionen sind nur offline qualifiziert: gegen die reinen
> Compile-Referenz-Stubs gebaut, auf der Siemens-freien Logik unit-getestet (`LibraryPolicy`, `LibraryRuntimeBinding`,
> Kataloge, Planung und Guards) und Ende-zu-Ende über den skriptgesteuerten FakeWorker
> (`library-roundtrip`) durchlaufen. Nichts hier ist gegen TIA Portal gelaufen. Die mechanische Signaturprüfung ist
> `scripts/verify-reference-stubs.ps1` (das die Probe
> `reference-stubs/TiaMcpServer.OpennessReferenceProbe/LibraryReferenceSurface.cs` kompiliert) plus ein Windows-Build
> des Workers und der Probe gegen das installierte `C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48`.
> Die meisten Bibliotheks-Member werden zur Laufzeit gebunden (siehe [Herkunft](#herkunft-jedes-verwendeten-siemens-members)); jeder
> bricht mit `unsupported_capability` ab (fail closed), wenn das installierte V21 ihn nicht bereitstellt, und jeder muss
> durch die [Live-Abnahmeprüfungen](#live-abnahmeprüfungen) durchlaufen werden, bevor man sich auf die Tools verlässt.

Tool-Anzahlen: 11 Tools in read-only, 30 in read-write und 30 in full (`library_read` ist das elfte
Read-only-Tool, `library_write` das neunundzwanzigste schreibfähige).

## Warum zwei Tools

`library_read`/`library_write` folgen der Aufteilung `vci_read`/`vci_write`: ein Lese-Tool mit unabhängigen Items
(ein fehlgeschlagenes Item stoppt die anderen nicht) und ein abgesichertes Schreib-Tool mit geordneten Items, das beim ersten
Fehler ohne Rollback stoppt. Lebenszyklus-Items globaler Bibliotheken bleiben in `library_write` statt in einem separaten
Lebenszyklus-Tool, sodass ein Aufruf eine Bibliothek öffnen und darin arbeiten kann; die Domäne bestätigt einen solchen Aufruf einmal (unten).

## Zugriff und Bestätigung

Die Owner-Entscheidung für diesen Fork: die KI darf alles tun, was der SPS-Ingenieur in TIA Portal tun kann, gesteuert
über den Zugriffsmodus. Read-only ist Beobachtung; read-write bearbeitet wie die anderen Domänen-Schreibzugriffe (ohne Rückfrage) und
bestätigt Lebenszyklusaktionen über Elicitation; full führt alles ohne Rückfragen aus. Nichts ist
hart blockiert außer ungültiger oder mehrdeutiger Eingabe: jeder `library_write`-Guard ist informativ, einschließlich
`library_close_unsaved_changes`, das die von diesem Server vorgenommenen ungespeicherten Änderungen benennt, die ein Schließen verwirft
([unten](#schließen-einer-globalen-bibliothek); ein Block bis 2026-11-01, gelockert, weil TIA Portal einen Benutzer ohne Speichern schließen lässt).

| Operation / Tool | Worker-Methode | Capability | read-only | read-write | full |
| --- | --- | --- | --- | --- | --- |
| `library_read` (alle vierzehn Operationen) | `library_<operation>` | `Observe` | ja | ja | ja |
| `library_write`-Planung | `library_plan` | `Observe` | n/a | ja | ja |
| `library_write`-Inhalts-Items (29 Operationen; jeder Aufruf mit `set_portal_setting` fragt nach, unten) | `library_<operation>` | `ProjectMutation` | nein (nicht aufgeführt) | ja, ohne Rückfrage | ja |
| `library_write`-Lebenszyklus-Items: `open_global_library`, `retrieve_global_library`, `create_global_library`, `save_global_library`, `close_global_library`, `archive_global_library`, `save_global_library_as`, `open_secondary_project`, `retrieve_secondary_project`, `close_secondary_project` | `library_<operation>` | `ProjectMutation` | nein (nicht aufgeführt) | ja, **eine Rückfrage pro tatsächlichem Aufruf, der eines enthält** | ja, Policy |

Lebenszyklus-Items legen Bibliotheksdateien außerhalb des Projekts an, entpacken, öffnen (read-write oder mit Upgrade) oder
überschreiben sie; die Siemens-Quelle verlangt für sie eine ausdrückliche Autorisierung, daher wird ein Aufruf, der eines enthält,
in read-write einmal bestätigt (`ConfirmsEveryCall` ist genau für diese Aufrufe true), wie `open_project`/`save_project`.
Die Rückfrage nennt zuerst den Effekt (der Aufruf öffnet, legt an, dearchiviert, speichert oder schließt Dateien globaler Bibliotheken
außerhalb des Projekts), dann eine Zeile pro Lebenszyklus-Item: die Operation, ihr aufgelöstes Ziel, den Öffnungsmodus, den sie
anfordert (`OpenWithUpgrade: may upgrade the library file in place on disk`, `OpenMode ReadWrite: save_global_library
overwrites the library file` oder `OpenMode ReadOnly`, für Öffnen und Dearchivieren), ihre `portalConfirmations`-Policy und ihre
`operationId`, jeder Wert des Aufrufers mit dem gemeinsamen `ConfirmationText` zitiert und maskiert (Ziele enthalten Pfade des Aufrufers,
sodass kein Text des Aufrufers eine Zeile hinzufügen oder die Rückfrage umordnen kann); danach nennt sie, wie viele Inhalts-Items im selben Aufruf laufen.
Ein Lebenszyklus-Item, das von einem früheren Item des Aufrufs abhängt (zum Beispiel `close_global_library` X, dann
`open_global_library` X mit `upgrade:true`), hat bis nach der Bestätigung keine Planung, daher wird seine Zeile aus dem
Item selbst gebildet (Pfad, Archiv und Zielverzeichnis, neuer Name und Verzeichnis oder Bibliothek) und sagt, dass es unmittelbar
vor seiner Mutation aufgelöst wird; der angeforderte Modus, ein Upgrade eingeschlossen, wird in jedem Fall angezeigt. Die Pipeline zeigt in einer Rückfrage nur
Acknowledge-Guards (maskierte Zeilen vor der Beschreibung); `library_write` definiert keine (seine Guards sind
informativ und einer blockierend), daher ist seine Rückfrage allein die Beschreibung, und die informativen Guards (wie
`library_upgrades_in_place`) erscheinen in der Antwort. Ein Aufruf, der ein Lebenszyklus-Item enthält, weist `operationId`s
mit Steuer-, Trenn- oder Formatzeichen ab (`validation_error`, nichts läuft, wie bei jedem bestätigenden Tool): die
Pipeline wendet diese Regel direkt nach `Validate` an (wenn `ConfirmsEveryCall` bekannt ist) und erneut unmittelbar vor
jeder Rückfrage; reine Inhaltsaufrufe fragen nie zurück und behalten sie. Eine abgelehnte, abgebrochene oder nicht unterstützte Rückfrage
weist den Aufruf mit `access_denied` ab, bevor etwas läuft. Sie sind als `ProjectMutation` statt als `ProjectLifecycle` eingestuft: sie öffnen, schließen
oder wechseln das gebundene Projekt nicht, und eine Ablehnung durch Portal ist `engineering_rejected` wie bei jedem Inhalts-Schreibzugriff.

### TIA-Portal-Bestätigungsdialoge (`portalConfirmations`)

Jedes `library_write`-Item nimmt `portalConfirmations`: `decline` (Default) oder `accept`, mit denselben Regeln wie
`plc_download`. Während die Mutation des Items läuft, wird jeder `Confirmation`-Dialog von TIA Portal nach dieser Policy beantwortet
statt mit der sitzungsweiten Antwort (Ja in read-write und full) und protokolliert: der Worker-Dispatch
(`Program.Library`) öffnet `TiaPortalSession.AnswerConfirmations` über `LibraryConfirmationScope.Open` (Owner: die
Worker-Methode) mit einem `PlcDownloadPortalConfirmationLog`, und der Service schließt den Scope, bevor er das
Protokoll in das Ergebnis kopiert (`portalConfirmationPolicy`,
`portalConfirmations[{ type, text, answer }]` höchstens 100 aufgeführt, `portalConfirmationsObserved`,
`portalConfirmationsComplete`). Bei `decline` wird ein Dialog mit Nein beantwortet, und das Item **schlägt fehl**
(`worker_operation_failed`, das Ergebnis bleibt erhalten, der Aufruf stoppt): das Ablehnen kann verändert haben, was der Siemens-Aufruf getan hat
(eine abgelehnte Speichern-Rückfrage während eines Schließens kann Änderungen verwerfen), daher mit `library_read` prüfen und mit
`accept` wiederholen, wenn Ja gewünscht ist. Bei `accept` wird er mit Ja beantwortet (in read-only läuft nichts), und das Item ist erfolgreich mit
einer Warnung. Ein fehlgeschlagenes Item trägt die bis zum Fehler gesammelte Evidenz. Der Host prüft die Evidenz (Policy wie
angefordert, Liste und Anzahl konsistent, jede Antwort die der Policy) und weist alles andere als `protocol_error` ab.
Die Planung führt die Policy als letzte Konsequenz des Items auf, und die Read-write-Rückfrage nennt sie pro Lebenszyklus-Item.
Welche Items überhaupt Dialoge auslösen, ist nicht dokumentiert; die Planung (`library_plan`) und `library_read` laufen ohne eigenen
Scope. Nachdem das Item zurückgekehrt ist, bleibt sein Scope mit derselben Antwort bestehen, bis die nächste Worker-Anfrage
beginnt (`PlcOnlineConfirmationScope.BeginRequest`, wie bei plc_download): ein Dialog, den TIA Portal spät auslöst, wird unter `decline` nie
mit dem sitzungsweiten Ja beantwortet, ist nicht in der Evidenz des Items und wird als Warnung auf
der nächsten Antwort gemeldet (in den `warnings` eines Items oder in den Wurzel-`warnings`, wenn es eine Planungsantwort von `library_write` ist).
`library_write` nimmt keine Passwörter, daher gilt die Passwort-Redaktion von plc_download nicht.

Beide Tools erfordern ein verifiziertes Binding an ein **Standalone**-Projekt (`Project.ProjectLibrary` ist der dokumentierte
Einstiegspunkt; lokale Multiuser-Sitzungen werden nicht bedient, `unsupported_capability`). Globale Bibliotheken gehören zur
TIA-Portal-Sitzung dieses Bindings und bleiben über Aufrufe hinweg geöffnet, bis `close_global_library` (oder TIA Portal)
sie schließt. Änderungen der Projektbibliothek werden über `save_project` gespeichert; Änderungen globaler Bibliotheken nur über
`save_global_library`.

## Selektoren und Pfade

| Feld | Bedeutung |
| --- | --- |
| `library` | `project` (exakt, kleingeschrieben) oder der Pfad einer **geöffneten** globalen Bibliothek genau so, wie `list_libraries` ihn meldet (ohne Beachtung der Groß-/Kleinschreibung verglichen, `/` und `\` gleich, nachgestellte Trennzeichen ignoriert) |
| `targetLibrary` | die zweite Bibliothek von `copy_master_copy` und `compare_libraries` (gleiche Form; muss sich von `library` unterscheiden) |
| `folderPath` | Typordner unterhalb der Wurzel der Typordner: `/` (Wurzel) oder `/A/B`; Segmente werden ohne Beachtung der Groß-/Kleinschreibung abgeglichen und müssen eindeutig sein |
| `typePath` / `typeGuid` | ein Typ als `/Folder/Type` (oder `Type` im Wurzel-Typordner) oder seine GUID (`ILibrary.FindType`) |
| `version` / `versionGuid` | der exakte Versionsnummerntext wie aufgeführt (z. B. `1.0.2`) oder die Versions-GUID (`ILibrary.FindVersion`); optional, wenn der Typ genau eine Version hat |
| `masterCopyName` | eine Kopiervorlage des Kopiervorlagenordners `masterCopyFolderPath` (ohne Angabe: der Wurzel-Kopiervorlagenordner; `MasterCopyComposition.Find`) |
| `masterCopyFolderPath` / `targetMasterCopyFolderPath` | ein Kopiervorlagenordner unter der Wurzel: `/` oder `/A/B` über `MasterCopyFolder.Folders` (V21), Segmente ohne Groß-/Kleinschreibung und eindeutig |
| `path`, `targetDirectory` | absolute Windows-Pfade (Laufwerksbuchstabe oder UNC-Freigabe), kein `..`, keine alternativen Datenströme, keine Win32-Geräte- oder Extended-Length-Pfade (`\\?\`, `\\.\`, auch mit `/` wie `\\./COM1` oder `\\?/UNC/...`), kein Segment, das ein reservierter Gerätename ist (`NUL`, `CON`, `PRN`, `AUX`, `COM1`-`COM9`, `LPT1`-`LPT9`, mit beliebiger Endung; Sicherheits-Review U2); die Existenz prüft der Worker (`target_not_found`) |
| `name` (`create_global_library`) | wird zu einem Verzeichnis- und Dateinamen unterhalb von `targetDirectory`, daher muss er neben den Namensregeln ein gültiger Windows-Dateiname sein: keines von `\ / : * ? " < > \|` oder Steuerzeichen (eine feste Windows-Menge, auch auf einem Linux-Host), kein `..`, kein nachgestellter `.`, kein reservierter Gerätename (`CON`, `PRN`, `AUX`, `NUL`, `COM1`-`COM9`, `LPT1`-`LPT9`, auch mit Erweiterung); `..\..\Temp\Evil` wird abgewiesen. Objektnamen (`newName`, `masterCopyName`, Typ- und Ordnernamen) sind keine Dateinamen: sie sind nicht leer, höchstens 256 Zeichen lang, ohne führende/nachgestellte Leerzeichen, ohne Steuerzeichen oder `/` |

Enum-Optionen tragen die exakten Siemens-Membernamen: `openMode` `ReadOnly` (Default) oder `ReadWrite`;
`masterCopyMode` `ThrowIfExists` (Default), `Rename`, `Replace`; `updatePathsMode` `ThrowIfPathsConflict`
(Default, wie die Quelle es für den unbeaufsichtigten Einsatz empfiehlt), `KeepExistingPathsInTarget`, `UpdatePathsInTarget`;
`dependenciesMode` `DoNotAutomaticallyCreateOrReleaseDependencies` (der einzige Member, den die Quellen nennen);
`updateCheckMode` `ReportOutOfDateOnly` (der einzige Member, den die Quellen nennen). Die Stub-Enums deklarieren keine Werte:
der Worker parst den Namen gegen das Live-Enum und bricht ab (fail closed: `unsupported_capability`, mit Auflistung der Live-Member), wenn er fehlt.

## `library_read`-Operationen

1-50 unabhängige Items, jedes `{ operationId, operation, ... }`; nur die aufgeführten Felder werden akzeptiert.

| Operation | Felder | Ergebnis |
| --- | --- | --- |
| `list_libraries` | keine | `{ libraries[{ library, kind, path, name, runtimeType }], unreadable }`: die Projektbibliothek (`kind: project`) und jede geöffnete globale Bibliothek (`user`, `system`, `corporate` oder `global` für eine andere Spezialisierung) |
| `list_global_library_infos` | keine | `{ infos[{ libraryType, name, path, isOpen }], unreadable }` aus `GetGlobalLibraryInfos()`; `isOpen` ist Pfadgleichheit mit einer geöffneten Bibliothek |
| `get_library_tree` | `library`; optional `folderPath`, `content` (`all`/`types`/`masterCopies`), `includeVersions` (Default true), `maxDepth` (1-32, Default 16), `offset` (Default 0), `limit` (1-500, Default 200) | eine offset/limit-Seite des abgeflachten Baums: `entries[{ kind (typeFolder/type/masterCopy), path, name, depth, guid, typeKind (codeBlock/plcType/other), runtimeType, versions[{ versionNumber, guid, state, isDefault }], contentTypes, unreadable }]`, `total`, `returned`, `complete`, `omission`, `masterCopyScope: "all"` (Kopiervorlagenordner als `kind: masterCopyFolder`; optionales `masterCopyFolderPath` beginnt diesen Durchlauf) |
| `get_type` | `library`, `typePath` oder `typeGuid` | `{ library, path, name, guid, typeKind, runtimeType, versions[{ versionNumber, guid, state, isDefault, dependencies[], dependents[], masterCopiesContainingInstances[], originalLibrary, unreadable }] }` |
| `find_type_instances` | `library`, `typePath`/`typeGuid` (alle Versionen) und/oder `version`/`versionGuid`; optional `plcName` | `{ library, typeName, typeGuid, plcName, deviceName, versions[{ versionNumber, guid, instances[{ name, runtimeType }], complete }] }` aus `FindInstances(PlcSoftware)` |
| `get_instance_info` | `blockPath` oder `plcTypePath` (die Pfadgrammatik von `plc_read`) | `{ plcName, objectKind, objectName, linked, typeName, typeGuid, versionNumber, versionGuid, versionState }`; `linked:false`, wenn das Objekt kein `LibraryTypeInstanceInfo` bereitstellt (keine Typinstanz, z. B. aus einer Kopiervorlage platziert) |
| `update_check` | `library`; optional `updateCheckMode` | `{ library, mode, messages[{ description, messageParts[], messages[] }], messageCount, complete }` aus `UpdateCheck(project, mode)` gegen das gebundene Projekt |
| `compare_libraries` | `library`, `targetLibrary`; optional `maxDepth` (1-64, Default 16), `maxElements` (1-5000, Default 300) | `{ library, compareTo, root, elementsListed, complete, omission }`; jedes Element `{ comparisonResult, attributes[{ name, value }], details[{ attributes }], elements[], elementsTruncated }` |

Der Baum wird in Tiefensuche durchlaufen: die Typen eines Ordners, dann jeder Unterordner, gefolgt von seinem Inhalt. Kopiervorlagen
folgen den Typen, wenn `folderPath` die Wurzel ist (oder `masterCopyFolderPath` angegeben ist): die Kopiervorlagen eines
Kopiervorlagenordners, dann jeder Unterordner, gefolgt von seinem Inhalt. Der Durchlauf stoppt bei 5.000
gesammelten Einträgen oder bei `maxDepth` (dann `complete:false` mit `omission`); das Paging ist reines offset/limit über
diesen Durchlauf ohne Snapshot, daher können Änderungen zwischen den Seiten Einträge verschieben.

`compare_libraries` projiziert Elemente **schemafrei**: die Quellen nennen `RootElement`, `Elements`,
`ComparisonResult` und `DetailedInformation.Properties`, aber weder die Identitätsmember der Elemente noch die
Membernamen von `LibraryCompareResultState`, daher listet jedes Element und jede Detaileigenschaft seine lesbaren skalaren
Attribute auf (`GetAttributeInfos`/`GetAttribute`, höchstens 20), und es wird keine Filterung nach identischem Zustand angeboten.
`update_check`-Nachrichten werden auf dieselbe Weise aus ihren benannten Membern projiziert (`Description`, `MessageParts`
als Text, verschachtelte `Messages`; 8 Ebenen, 500 Nachrichten).

## `library_write`-Operationen

1-50 geordnete Items; `dryRun:true` plant und wertet Guards aus, ohne Mutation oder Rückfrage; ausgelassen oder false
führt aus. Der Aufruf stoppt beim ersten fehlgeschlagenen Item; frühere Items bleiben angewendet (kein Rollback, keine Wiederholung). Jedes Item
akzeptiert außerdem `portalConfirmations` (`decline` Default, `accept`; siehe oben).

| Operation | Felder | Siemens-Aufruf (siehe Herkunft) | Hinweise |
| --- | --- | --- | --- |
| `open_global_library` | `path`; `openMode` oder `upgrade:true` | `GlobalLibraries.Open(FileInfo, OpenMode)` / `OpenWithUpgrade(FileInfo)` | ein Archiv (`.zalNN`) ist ein Argumentfehler, der auf `retrieve_global_library` verweist; eine bereits geöffnete Datei (über den vollständigen Pfad verglichen) wird gemeldet (`alreadyOpen`), und nichts wird aufgerufen; eine vom Aufruf zurückgegebene Bibliothek, die schon vorher geöffnet war, wird ebenfalls als `alreadyOpen` gemeldet; K80 (live am 2026-11-03: TIA Portal lehnte den Pfad von `PrintingSysLib.as21` ab, „System and Corporate global libraries cannot be opened with a path.“): wenn `GetGlobalLibraryInfos()` `path` als `System`- oder `Corporate`-Bibliothek listet, öffnet der Worker sie stattdessen mit `GlobalLibraryComposition.Open(GlobalLibraryInfo)` (gemeinsame Entscheidung `GlobalLibraryInfoOpen`, Contracts); dieser Aufruf nimmt keinen OpenMode, daher sind `openMode: ReadWrite` und `upgrade:true` ein `validation_error`, und TIA Portal öffnet die Bibliothek schreibgeschützt; `note` im Ergebnis nennt den Aufruf; zurückgelesen über `list_libraries` wie jedes Öffnen (nicht live getestet). Selektor bleibt der `path`, den `list_global_library_infos` liefert (der Name ist kein eigener Selektor: `library`-Selektoren und `list_libraries` arbeiten mit Pfaden) |
| `retrieve_global_library` | `path` (Archiv), `targetDirectory`; `openMode` | `GlobalLibraries.Retrieve(FileInfo, DirectoryInfo, OpenMode)` | entpackt und öffnet; das Ergebnis nennt den Pfad der geöffneten Bibliothek |
| `create_global_library` | `targetDirectory`, `name` | `GlobalLibraries.Create<UserGlobalLibrary>(DirectoryInfo, string)` | das Verzeichnis muss existieren; `name` muss ein gültiger Windows-Dateiname sein ([Selektoren](#selektoren-und-pfade)), geprüft vom Host und erneut vom Worker |
| `save_global_library` | `library` (globale Benutzerbibliothek) | `UserGlobalLibrary.Save()` | System-/Corporate-Bibliotheken sind schreibgeschützt (`target_kind_unsupported`) |
| `close_global_library` | `library` (globale Benutzerbibliothek) | `UserGlobalLibrary.Close()` | nur globale Benutzerbibliotheken: die Quellen dokumentieren `Close()` nur für `UserGlobalLibrary`, daher wird eine System- oder Corporate-Bibliothek bei der Planung abgewiesen (`target_kind_unsupported`, nichts läuft; in TIA Portal schließen); abgewiesen, solange Änderungen dieses Servers ungespeichert sind (unten); Zurücklesen: die Bibliothek darf nicht mehr aufgeführt sein (`postcondition_failed`) |
| `create_master_copy` | `library`, `source { kind block/plcType/tagTable/device/textList, path, plcName (tagTable and textList only) }` | `MasterCopyFolder.MasterCopies.Create(IMasterCopySource)` | die Quelle muss ein `IMasterCopySource` sein (sonst `target_kind_unsupported`); Wurzel-Kopiervorlagenordner; ein `textList`-Pfad ist der Name der Anwender-Textliste (A: `MasterCopies.Create(alarmTextList)`; Systemlisten werden abgewiesen) |
| `copy_master_copy` | `library`, `masterCopyName`, `targetLibrary` | `target.MasterCopyFolder.MasterCopies.CreateFrom(MasterCopy)` | z. B. Projektbibliothek → geöffnete globale Benutzerbibliothek |
| `delete_master_copy` | `library`, `masterCopyName` | `MasterCopy.Delete()` | Zurücklesen: `Find(name)` darf nichts zurückgeben |
| `rename_master_copy` | `library`, `masterCopyName`, `newName` | `SetAttribute("Name", newName)` | der Inhalt der Kopiervorlage behält seine Namen |
| `instantiate_master_copy` | `library`, `masterCopyName`, `target`; `masterCopyMode` (PLC-Arten), `newName` | `CreateFrom(MasterCopy, MasterCopyMode)` auf der Baustein-/Typ-/Variablentabellen-Gruppe der PLC-Wurzel (`folderPath`), `TechnologicalObjectGroup.TechnologicalObjects`, `PlcUnitProvider.UnitGroup.Units`, `ExternalSourceGroup.ExternalSources` oder `PlcAlarmTextlistGroup.PlcAlarmUserTextlists` (der dokumentierte Weg, eine Anwender-Textliste anzulegen; siehe die [Referenz zu PLC-Meldungen und Textlisten](PLC_ALARMS_TEXTLISTS_SUMMARY.de.md)); `CreateFrom(MasterCopy)` auf `Project.Devices` oder den `DeviceItems` eines übergeordneten Geräteelements | Zielarten `blockGroup`, `typeGroup`, `tagTableGroup`, `technologyObjects`, `softwareUnits`, `externalSources`, `textLists` (PLC), `devices`, `deviceItem` (`parent` = der Hardware-Selektor von `network_read`); der Aufrufer wählt die Art anhand der `contentTypes` der Kopiervorlage (keine automatische Zuordnung); `newName` benennt das einzelne angelegte Objekt danach um (das Umbenennen nach dem Platzieren aus G); Evidenz: `CreateFrom`, das null zurückgibt, ist `postcondition_failed`, ein ohne Rückgabewert gebundener Member oder eine leere zurückgegebene Collection macht das Item `notVerified` |
| `instantiate_type_version` | `library`, Typ, Version, `target { kind blockGroup/typeGroup, plcName, folderPath }`; `updatePathsMode` | `Blocks.CreateFrom(CodeBlockLibraryTypeVersion, UpdatePathsMode)` / `Types.CreateFrom(PlcTypeLibraryTypeVersion, UpdatePathsMode)` | eine Codebaustein-Version kommt in eine Bausteingruppe, eine PLC-Datentyp-Version in eine Typgruppe (sonst `target_kind_unsupported`); Evidenz wie bei `instantiate_master_copy` (G zeigt diesen Aufruf, ohne seinen Wert zu verwenden, daher ist eine `void`-Bindung `notVerified`, nie trusted) |
| `update_project` | `library`; eines von `typePath`/`typeGuid`/`folderPath`; optional `plcName` | `UpdateProject(<type or folder>, <PlcSoftware>)` | das Default-Aktualisierungsverhalten von TIA Portal (die Modus-Überladungen sind nicht dokumentiert); zuerst `update_check` ausführen |
| `rename_type` | `library`, Typ, `newName` | `SetAttribute("Name", newName)` | zurückgelesen über `LibraryType.Name` |
| `rename_type_folder` | `library`, `folderPath` (ein Benutzerordner), `newName` | `SetAttribute("Name", newName)` | der Wurzel-Typordner kann nicht umbenannt werden |
| `edit_type_version` | `library`, Typ, Version | `LibraryTypeVersion.Edit()` | liefert die in Bearbeitung befindliche Version (`versionAfter`); ihre Instanz mit `plc_write` bearbeiten, übersetzen, dann freigeben; Zurücklesen: `Edit()`, das nichts zurückgibt, oder eine zurückgegebene Version, deren `State` nicht `InWork` ist, ist `postcondition_failed` |
| `release_type_version` | `library`, Typ, Version, `versionNumber`; `author`, `comment`, `dependenciesMode` | `Release(CreateOrReleaseDependenciesMode, Version, string, string)` | `author`/`comment` sind das dritte und vierte Argument (das Beispiel der Quelle übergibt einen Autor und einen Kommentartext; ihre Bedeutung ist nicht weiter dokumentiert); Zurücklesen auf derselben Version (oder per GUID mit `ILibrary.FindVersion` wiedergefunden): `State` muss `Committed` sein und `VersionNumber` muss `versionNumber` entsprechen (als Versionen verglichen, fehlende Komponenten 0), sonst `postcondition_failed` |
| `discard_type_version` | `library`, Typ, Version | `Discard()` | Zurücklesen: `ILibrary.FindVersion(guid)` darf nichts finden, oder der `State` der gefundenen Version muss sich von vorher unterscheiden, sonst `postcondition_failed` |
| `set_default_type_version` | `library`, Typ, Version | `SetAsDefault()` | Zurücklesen: `IsDefault` darf nicht false lesen (`postcondition_failed`); ein unlesbares `IsDefault` macht das Item `notVerified` |
| `delete_type_version` | `library`, Typ, Version | `LibraryTypeVersion.Delete()` | Zurücklesen: `FindVersion(guid)` darf nichts zurückgeben; der Guard listet die bei der Planung gelesenen abhängigen Elemente (Dependents) auf |

Umbenennungen verwenden die dokumentierte generische Attribut-API: `Name` muss von `GetAttributeInfos()` als schreibbar aufgeführt sein
(sonst `unsupported_capability`), dann `SetAttribute("Name", value)` und ein Zurücklesen mit `GetAttribute("Name")`;
eine stillschweigend ignorierte Umbenennung schlägt mit `postcondition_failed` fehl.

### Aus der V21-API-Beschreibung ergänzte Operationen (Issue #17)

Quelle **(V21)**: die Siemens-IntelliSense-Dateien von TIA Portal V21 (`PublicAPI\V21\net48\Siemens.Engineering.Base.xml`, Stand
29.10.2025), die jeden Member mit Parameterliste und Enum-Membern nennen. Jeder Member unten wird **zur Laufzeit** über seinen
dokumentierten Namen und seine Parameterliste gebunden, mit dem Siemens-freien `LibrarySignatureBinding` (Contracts): Enum-Parameter
über den dokumentierten vollständigen Typnamen und den Wert über den exakten Membernamen, Collection-Parameter über den dokumentierten
Elementtyp, andere Parameter über Zuweisbarkeit; genau ein passender Member muss existieren, sonst schlägt das Item mit
`unsupported_capability` fehl, bevor irgendetwas aufgerufen wird. Es wurde kein Stub-Member ergänzt. Nicht live getestet.

| Operation | Felder | Siemens-Aufruf | Hinweise |
| --- | --- | --- | --- |
| `retrieve_global_library` mit `upgrade:true` | wie oben | `GlobalLibraryComposition.RetrieveWithUpgrade(FileInfo, DirectoryInfo, OpenMode)` | Lebenszyklus; die Rückfrage sagt, dass die dearchivierte Bibliothek hochgerüstet wird |
| `archive_global_library` | `library` (globale Anwenderbibliothek), `targetDirectory`, `name` (Archiv-Dateiname, Dateinamensregeln), `archivationMode` (`None`, `Compressed` Default, `DiscardRestorableData`, `DiscardRestorableDataAndCompressed`) | `UserGlobalLibrary.Archive(DirectoryInfo, String, LibraryArchivationMode)` | Lebenszyklus (Rückfrage in read-write); Evidenz: die vom Aufruf geschriebenen Dateien `name` oder `name.*` in `targetDirectory`, gehasht in `result.transfer.files`; keine gefunden: `notVerified` |
| `save_global_library_as` | `library` (globale Anwenderbibliothek), `targetDirectory` | `UserGlobalLibrary.SaveAs(DirectoryInfo)` | Lebenszyklus; die geöffnete Bibliothek wird über ihre Identität wiedergefunden und ihr Pfad danach gemeldet (`result.library`, Hinweis); die geschriebenen Dateien werden gehasht; keine gefunden: `notVerified`. Das Journal ungespeicherter Änderungen wird nicht geleert (die Originaldatei bleibt unverändert) |
| `clean_up_library` | `library`, `typePath`/`typeGuid` oder `folderPath`; `cleanUpMode` (nur Projektbibliothek: `PreserveDefaultVersionOfUnusedTypes` Default, `DeleteUnusedTypes`) | `ProjectLibrary.CleanUpLibrary(IEnumerable<ILibraryTypeOrFolderSelection>, CleanUpMode)` / `UserGlobalLibrary.CleanUpLibrary(IEnumerable<ILibraryTypeOrFolderSelection>)` | Guard `library_deletes` mit der Typ- und Versionszahl der Auswahl; der Hinweis meldet die Zahlen vorher und nachher (ein weiteres Zurücklesen ist nicht dokumentiert) |
| `harmonize_project` | `library`, Auswahl, `harmonizeOptions` (`HarmonizePaths` und/oder `HarmonizeNames`; beide nur, wenn das Live-Enum `[Flags]` ist; `None` wirft laut Dokumentation und wird abgewiesen), optional `plcName` | `HarmonizeProject(IEnumerable<ILibraryTypeOrFolderSelection>, IEnumerable<IUpdateProjectScope>, HarmonizeProjectOptions)` mit der PLC-Software als Scope (wie `update_project`) | Guard `library_changes_project`; `trusted` (kein dokumentiertes Zurücklesen); danach übersetzen |
| `update_library` | `library`, Auswahl, `targetLibrary`; optional `forceUpdateMode`, `deleteUnusedVersionsMode`, `structureConflictResolutionMode` (alle drei oder keiner) | `UpdateLibrary(IEnumerable<ILibraryTypeOrFolderSelection>, ILibrary)` oder die Überladung mit fünf Argumenten | ändert `targetLibrary` (Journal, `library_global_unsaved`, wenn sie global ist); `library_update_defaults` ohne Modi, `library_deletes` mit `AutomaticallyDelete` |
| `update_project` mit Modi | wie oben plus `deleteUnusedVersionsMode` (Projektbibliothek) oder alle drei Modi (globale Bibliothek) | `ProjectLibrary.UpdateProject(…, DeleteUnusedVersionsMode)` / `GlobalLibrary.UpdateProject(…, ForceUpdateMode, DeleteUnusedVersionsMode, StructureConflictResolutionMode)` | ohne Modi wie bisher der dokumentierte Aufruf mit zwei Argumenten |
| `delete_type` | `library`, Typ | `LibraryType.Delete()` | Zurücklesen: `ILibrary.FindType(guid)` darf nichts finden; Guard `library_deletes` nennt die Versionen |
| `set_type_do_not_use` | `library`, Typ, `doNotUse` | Setter von `LibraryType.DoNotUse` (V21: "Gets or sets") | Zurücklesen über dieselbe Eigenschaft; ein anderer Wert ist `postcondition_failed`; `get_type` meldet `doNotUse` |
| `create_type_folder` | `library`, `folderPath` (übergeordneter Ordner, Default Wurzel), `newName` | `LibraryTypeUserFolderComposition.Create(String)` auf `LibraryTypeFolder.Folders` des Elternordners | Zurücklesen: der Ordner muss unter dem Elternordner aufgeführt sein |
| `delete_type_folder` | `library`, `folderPath` (ein Benutzerordner) | `LibraryTypeUserFolder.Delete()` | alles darin wird mitgelöscht (Guard `library_deletes` mit den Zahlen); Zurücklesen: nicht mehr aufgeführt |
| `create_master_copy_folder` | `library`, `masterCopyFolderPath` (Elternordner, Default Wurzel), `newName` | `MasterCopyUserFolderComposition.Create(String)` auf `MasterCopyFolder.Folders` | Zurücklesen: unter dem Elternordner aufgeführt |
| `delete_master_copy_folder` | `library`, `masterCopyFolderPath` (ein Benutzerordner) | `MasterCopyUserFolder.Delete()` | Guard `library_deletes`; Zurücklesen: nicht mehr aufgeführt |
| `rename_master_copy_folder` | `library`, `masterCopyFolderPath`, `newName` | generisches `SetAttribute("Name")` nach Prüfung der Schreibbarkeit über `GetAttributeInfos` | Zurücklesen mit `GetAttribute("Name")` |
| `create_type_from_documents` | `library`, `documentDirectory`, `documentName` (Dateiname ohne Endung), optional `folderPath` (Ziel-Typordner), `plcName` (die PLC-Software als `targetEnvironment`), `libraryImportOptions` (`None` Default, `ActivateInactiveCultures`, `SkipInactiveCultures`) | `LibraryTypeComposition.CreateFromDocuments(DirectoryInfo, String[, IEngineeringObject], LibraryImportOptions)` auf `Types` des Ordners | `targetEnvironment` ist dokumentiert als "Target test environment for instance creation of Step7 library types"; welches Objekt TIA Portal dort erwartet, ist nicht dokumentiert, daher wird die PLC-Software übergeben und TIA Portal entscheidet (eine Ablehnung ist `engineering_rejected`). Ergebnis: Zustand und Meldungen von `TypeCreateTransferResults` in `result.transfer`, `CreatedType` in `created` und mit `FindType` wiedergefunden; unlesbar: `notVerified` |
| `create_version_from_documents` | `library`, Typ, `documentDirectory`, `documentName`, optional `createOption` (`None` Default: neue Version; `Override`: die In-Work-/In-Test-Version überschreiben, Guard `library_replaces_objects`), `plcName`, `libraryImportOptions` | `LibraryTypeVersionComposition.CreateFromDocuments(DirectoryInfo, String[, IEngineeringObject], CreateOptions, LibraryImportOptions)` auf `LibraryType.Versions` | `CreatedVersion` wird in `versionAfter` zurückgelesen und mit `FindVersion` wiedergefunden |
| `export_type_version` | `library`, Version, `filePath`; optional `exportOptions` (`None` Default, `WithDefaults`, `WithReadOnly`), `documentInfoOptions` (`None`, `All`, `CreatedTimeStamp`, `ExportSetting`, `InstalledProducts`; wählt die Überladung mit drei Argumenten), `overwrite` | `LibraryTypeVersion.Export(FileInfo, ExportOptions[, DocumentInfoOptions])` | SimaticML in eine Zwischendatei neben dem Ziel, an seinen Platz verschoben (eine vorhandene Datei braucht `overwrite:true`), neu gehasht (`result.transfer.files`); Guard `library_export_overwrites_files` |
| `export_type_version_as_documents` | `library`, Version, `documentDirectory`, `documentName`, `exportFormat` (aus `get_supported_export_formats`), optional `libraryExportOptions` (`None` Default, `WithLibraryVersionInfoFile`, `OnlyLibraryVersionInfoFile`), `overwrite` | `LibraryTypeVersion.ExportAsDocuments(DirectoryInfo, String, String, LibraryExportOptions)` | vorhandene Dateien `documentName.*` brauchen `overwrite:true` (dann entscheidet TIA Portal); Zustand, Meldungen und `ExportedDocuments` von `ExportTransferResult` in `result.transfer`, die geschriebenen Dateien gehasht; keine gefunden: `notVerified` |
| `edit_type_version` mit Instanz | wie oben plus `blockPath` oder `plcTypePath` | `LibraryTypeVersion.Edit(LibraryTypeInstanceInfo)` (V21: für Typen, "that require instance to edit the type") | übergeben wird die `LibraryTypeInstanceInfo` der Instanz (`GetService`); ein Objekt, das keine Typinstanz ist, ist `target_kind_unsupported` |

Kopiervorlagen-Items (`create_master_copy`, `copy_master_copy`, `delete_master_copy`, `rename_master_copy`, `instantiate_master_copy`,
`compare_master_copies`) nehmen `masterCopyFolderPath` (`/` oder `/A/B`, Segmente ohne Groß-/Kleinschreibung und eindeutig über
`MasterCopyFolder.Folders`; ohne Angabe: der Wurzel-Kopiervorlagenordner); `copy_master_copy` nimmt zusätzlich
`targetMasterCopyFolderPath` (Zielordner in `targetLibrary`).

Neue `library_read`-Operationen:

| Operation | Felder | Ergebnis |
| --- | --- | --- |
| `get_supported_export_formats` | `library`, Typ | `{ library, path, name, guid, formats[] }` aus `LibraryType.GetSupportedExportFormats()` |
| `compare_types` | `library`, Typ, `targetTypePath`/`targetTypeGuid`, optional `targetLibrary` (ohne Angabe: `library`), `maxElements` | `LibraryType.CompareTo(LibraryType)` |
| `compare_type_versions` | `library`, Version, `targetTypePath`/`targetTypeGuid` mit `targetVersion` oder `targetVersionGuid`; optional `targetLibrary`, `maxElements` | `LibraryTypeVersion.CompareTo(LibraryTypeVersion)` |
| `compare_master_copies` | `library`, `masterCopyName`, `masterCopyFolderPath`, `targetMasterCopyName`, `targetMasterCopyFolderPath`, optional `targetLibrary`, `maxElements` | `MasterCopy.CompareTo(MasterCopy)` |

Jeder Vergleich liefert `{ library, left, compareTo, right, properties[{ description, status, leftValue, rightValue }], complete, unreadable }`
aus `DetailedCompareResult.Properties` (`DetailedCompareResultElement.Description`, `.DetailCompareStatus` — `ObjectsIdentical`,
`ObjectsDifferent`, `NotCompared` —, `.LeftValue`, `.RightValue`; höchstens `maxElements`, Default 300). Ein Objekt mit sich selbst
zu vergleichen ist ein Argumentfehler. `get_type` meldet zusätzlich `doNotUse`, `setForUpdate`, `status`, `namespace`, `author` und
`comment` (Laufzeit-Lookups, null und in `unreadable` aufgeführt, wenn nicht vorhanden). `get_library_tree` führt Benutzerordner für
Kopiervorlagen (`kind: masterCopyFolder`) und die Kopiervorlagen darin auf (`masterCopyScope: "all"`); `masterCopyFolderPath` beginnt
diesen Durchlauf unter einem Ordner. `update_check` akzeptiert `updateCheckMode` `ReportOutOfDateAndUpToDate` und `release_type_version`
`dependenciesMode` `AutomaticallyCreateOrReleaseDependenciesIfRequired` (beide Enums sind jetzt vollständig dokumentiert).

### Sekundäre (schreibgeschützte Referenz-)Projekte (Issue #20)

> **Nicht live getestet.** Evidenz (V21 `Siemens.Engineering.Base.xml`): `ProjectComposition.Open(FileInfo, UmacDelegate, ProjectOpenMode)`,
> `OpenWithUpgrade(FileInfo, UmacDelegate, ProjectOpenMode)`, `Retrieve(FileInfo, DirectoryInfo, UmacDelegate, ProjectOpenMode)`,
> `RetrieveWithUpgrade(FileInfo, DirectoryInfo, UmacDelegate, ProjectOpenMode)`, `ProjectOpenMode.Secondary` ("Opens file as a secondary
> project"), `ProjectBase.IsPrimary`, `Project.Close()`; der Siemens-Skill session-and-project beschreibt das Sekundärprojekt als
> schreibgeschützt und versteckt, zum Kopieren und Vergleichen.

TIA Portal hält ein Projekt zum Bearbeiten offen (K30/K51); ein Sekundärprojekt öffnet schreibgeschützt daneben. Die Tools behalten
ihre Ein-Projekt-Bindung: ein Sekundärprojekt ist nie ein Bindungskandidat (die Liste offener Kontexte im Worker bietet nur
Projekte an, deren `IsPrimary` true liest; false oder nicht lesbar wird übersprungen, nicht lesbar gemeldet, Sicherheits-Review U3), Inhalts-Tools wirken nur auf das gebundene Projekt, und die Bindung ändert sich nicht,
wenn ein Sekundärprojekt öffnet oder schließt.

| Operation | Felder | Siemens-Aufruf | Hinweise |
| --- | --- | --- | --- |
| `open_secondary_project` (Lebenszyklus) | `path` (Projektdatei), `upgrade` | `Open` / `OpenWithUpgrade` mit `ProjectOpenMode.Secondary` und einem UMAC-Delegate null | der Pfad des gebundenen Projekts wird abgewiesen; Evidenz: das zurückgegebene Projekt liest `IsPrimary == false` (true: `postcondition_failed`; unlesbar: `notVerified`); `result.secondaryProject { path, name, isPrimary }` |
| `retrieve_secondary_project` (Lebenszyklus) | `path` (Projektarchiv), `targetDirectory`, `upgrade` | `Retrieve` / `RetrieveWithUpgrade` mit `ProjectOpenMode.Secondary` | entpackt nach `targetDirectory` (Guard `library_writes_files`) |
| `close_secondary_project` (Lebenszyklus) | `secondaryProject` (Pfad wie aufgeführt) | `Project.Close()` auf diesem Sekundärprojekt | Zurücklesen: nicht mehr aufgeführt |
| `library_read` `list_secondary_projects` | keine | `TiaPortal.Projects` gefiltert nach `IsPrimary == false` | `{ projects[{ path, name, isPrimary }], unreadable }` |
| `create_master_copy` mit `source.project` | wie `create_master_copy`, plus der Pfad des Sekundärprojekts | die Quelle wird im Sekundärprojekt aufgelöst, dann `MasterCopies.Create(IMasterCopySource)` in der Bibliothek des gebundenen Projekts (oder einer offenen globalen) | Objekte kopieren: aus dem Sekundärprojekt erfassen, dann mit `instantiate_master_copy` ins gebundene Projekt; ein Erfassen hängt von einem früheren `open_secondary_project` desselben Pfads im Aufruf ab |
| `plc_read` `compare_software` `compareTarget: secondary_plc` | `targetName` (PLC des Sekundärprojekts), `targetProject` (sein Pfad) | `PlcSoftware.CompareTo(<PlcSoftware des Sekundärprojekts>)` | das TIA Portal ist der Parent des gebundenen Projekts (`IEngineeringInstance.Parent`) |

Die drei Lebenszyklus-Items werden einmal pro tatsächlichem read-write-Aufruf bestätigt wie die Lebenszyklus-Items globaler
Bibliotheken (Guard `library_secondary_project`, Info; `library_writes_files` beim Dearchivieren oder Upgrade). Das UMAC-Delegate
wird als null übergeben: ein UMAC-geschütztes Projekt weist TIA Portal ab (`engineering_rejected`), Anmeldedaten gehören nicht zum
Umfang. **Ausgelassen:** den Inhalt eines Sekundärprojekts direkt mit `plc_read`, `browse_project_tree`, `network_read` oder
`hmi_read` lesen (die Inhalts-Tools wirken nach der Bindungsregel auf das gebundene Projekt; stattdessen in die Bibliothek erfassen
oder vergleichen); ein Sekundärprojekt einer lokalen Multiuser-Session öffnen. **Live-Abnahme noch nötig:** dass `IsPrimary` für das
geöffnete Projekt false liest, dass TIA Portal für ein ungeschütztes Projekt ein UMAC-Delegate null annimmt und dass das Erfassen
aus einem Sekundärprojekt funktioniert.

### TIA-Portal-Einstellungen (Issue #27)

> **Nicht live getestet.** Evidenz (V21 `Siemens.Engineering.Base.xml`): `TiaPortal.SettingsFolders` (`TiaPortalSettingsFolderComposition`
> mit `Find(String)`), `TiaPortalSettingsFolder.Name`, `.Folders`, `.Settings` (`TiaPortalSettingComposition.Find(String)`),
> `TiaPortalSetting.Name`, `.Value`. Keine Einstellung ist benannt und kein Werttyp dokumentiert.

Die Einstellungen gehören zur TIA-Portal-Instanz, nicht zum Projekt; sie liegen in den Bibliotheks-Tools, weil diese bereits auf
TIA-Portal-weite Objekte wirken (globale Bibliotheken), so dass kein Tool hinzukommt. Wie jede Bibliotheksoperation brauchen sie ein
gebundenes Einzelplatzprojekt (Capability-Zeile der Bibliotheks-Tools).

- `library_read` `list_portal_settings` (optional `settingsFolderPath` `A/B`, `maxDepth`): die Ordner und Einstellungen in
  Tiefensuche: `{ folderPath, folders[], settings[{ folderPath, name, value, valueType, enumMembers, writable, securityRelevant,
  unreadable }], complete, omission }`; `value` ist invarianter Text, `valueType` der Laufzeittyp, `writable`, ob die Live-Eigenschaft
  `Value` einen öffentlichen Setter hat, höchstens 5.000 Einträge.
- `library_write` `set_portal_setting` (`settingsFolderPath`, `settingName`, `settingValue` als Text): der Ordner wird mit dem
  dokumentierten `Find(String)` je Segment gefunden, die Einstellung mit `TiaPortalSettingComposition.Find(String)`; der Text wird in
  den Laufzeittyp des aktuellen Werts umgewandelt (`true`/`false`, eine Zahl, ein Enum-Member über den exakten Namen oder Text; ein
  anderer Typ ist `unsupported_capability`); der Wert wird nur über den öffentlichen Setter der Live-Eigenschaft `Value` gesetzt (keiner:
  `unsupported_capability`, nichts geändert) und zurückgelesen (abweichend: `postcondition_failed`). Ergebnis
  `setting { folderPath, name, before, after, valueType, securityRelevant }`.
- Guards und Bestätigung (Sicherheits-Review 2026-11-03, F2; nicht live getestet): jeder Aufruf mit einem `set_portal_setting`
  wird in read-write bestätigt (`ConfirmsEveryCall`, unsichere operationIds werden wie bei jedem bestätigenden Aufruf abgewiesen);
  `library_portal_setting` (Info) bei jeder Änderung. Eine Einstellung ist sicherheitsrelevant, wenn der Ordnerpfad oder
  Einstellungsname, **den TIA Portal in der Planung aufgelöst hat** (der `Name` jedes von `Find(String)` gelieferten Ordners und
  der Einstellung; der Plan trägt ihn als bedingtes `setting` von `library_plan`, nie den Text des Aufrufers), `secur`, `certif`,
  `communicat`, `encrypt`, `password`, `trust`, `tls`, `ssl`, `legacy`, `authent`, `protect`, `umac`, `signing`, `signature`,
  `accept`, `allow`, `verif`, `cipher`, `access`, `credential`, `openness` oder `firewall` enthält (ohne Groß-/Kleinschreibung;
  die API benennt keine Einstellung, daher die Einordnung über den Namen; ein nicht lesbarer Name oder ein Plan ohne aufgelöste
  Einstellung zählt als sicherheitsrelevant). Eine solche Einstellung löst zusätzlich den **Bestätigungs**-Guard
  `library_portal_security_setting` aus; full erfüllt ihn per Policy. Der Host schickt die geplante Einordnung mit der Änderung
  (`plannedSecurityRelevant`); der Worker löst die Einstellung erneut auf und lässt das Item mit `state_changed` scheitern
  (nichts geändert), wenn sie anders eingeordnet wird. Einen harten Block gibt es nicht.
- **Ausgelassen:** Werte, deren Typ weder Text, boolesch, Zahl noch Enum ist; Einstellungen außerhalb von `SettingsFolders` (keine
  dokumentiert). **Live-Abnahme noch nötig:** welche Einstellungen es gibt, welche einen Setter haben und ihre Werttypen.

### Planung, Reihenfolge, Guards, Verifikation

Die Planung schickt jedes Item an das nicht mutierende `library_plan` des Workers, das genau auflöst, was die Mutation
verwenden wird (Bibliothek, Ordner, Typ, Version, Kopiervorlage, Quelle, PLC-Gruppe, Geräteelement; Dateien und Verzeichnisse),
und die Konsequenzen meldet; eine Abweisung dort lässt den Aufruf fehlschlagen, bevor etwas läuft (`target_not_found`,
`target_ambiguous`, `target_kind_unsupported`, ...), und nennt das Item. Ein Item auf einer Bibliothek, die ein früheres
Item desselben Aufrufs berührt (`library`, `targetLibrary`, der `path` eines Öffnen-Items; Instanziierung und
`update_project` berühren außerdem die Projektbibliothek), hängt von diesem Item ab: es wird unmittelbar vor seiner eigenen
Mutation erneut geplant (`dependsOn`), sodass ein Aufruf eine Bibliothek öffnen und eine Kopiervorlage hineinkopieren kann. Bibliotheken, die in einem Aufruf angelegt oder
dearchiviert werden, werden in einem späteren Aufruf adressiert (ihr Pfad steht im Ergebnis und in `list_libraries`).

Guards (alle `info` außer einem `block`; keiner braucht eine Quittierung):

| Guard | Ausgelöst durch |
| --- | --- |
| `library_close_unsaved_changes` | `close_global_library` einer Bibliothek mit einer von diesem Server vorgenommenen Änderung, die kein `save_global_library` abdeckt ([unten](#schließen-einer-globalen-bibliothek)) |
| `library_writes_files` | `open_global_library` mit `ReadWrite` oder `upgrade`, `retrieve_global_library`, `create_global_library`, `save_global_library`, `archive_global_library`, `save_global_library_as` |
| `library_upgrades_in_place` | `open_global_library` mit `upgrade:true` |
| `library_already_open` | `open_global_library` einer geöffneten Datei |
| `library_close_unsaved` | `close_global_library` |
| `library_deletes` | `delete_master_copy`, `delete_type_version`, `discard_type_version`, `delete_type`, `delete_type_folder`, `delete_master_copy_folder`, `clean_up_library`, `update_project`/`update_library` mit `AutomaticallyDelete` (mit den geplanten Konsequenzen) |
| `library_changes_project` | `harmonize_project` |
| `library_export_overwrites_files` | `export_type_version`, `export_type_version_as_documents` |
| `library_replaces_objects` | `instantiate_master_copy` mit `masterCopyMode: Replace`, `create_version_from_documents` mit `createOption: Override` |
| `library_syncs_project_library` | `instantiate_type_version` aus einer globalen Bibliothek (die Version wird zuerst in die Projektbibliothek synchronisiert) |
| `library_update_defaults` | `update_project`, `update_library` ohne Modi |
| `library_global_unsaved` | eine Inhaltsänderung in einer globalen Bibliothek (`library` oder das Ziel von `copy_master_copy`) |

Die Verifikation ist `trusted` (oder `superseded` durch ein späteres Item des Aufrufs auf demselben Objekt oder durch ein späteres
`close_global_library` derselben Bibliothek, vor dem kein `save_global_library` dieser Bibliothek liegt; K47: Objekt eines
`copy_master_copy` ist die Kopie in der Zielbibliothek, nie die Quelle, und ein Speichern wird nie überholt, ein Schließen macht es
nicht rückgängig) für jedes abgeschlossene Item: die Worker-Mutation selbst führt die
oben aufgeführten Zurücklesevorgänge durch und schlägt mit `postcondition_failed` fehl, wenn sie nicht übereinstimmen, daher gibt es keinen separaten Verifikations-Lesezugriff. Ein Zurücklesen, das nicht durchgeführt werden kann, fällt sicher auf `notVerified` zurück, nie auf `trusted`: wenn ein Member, den es braucht, ein
Laufzeit-Lookup ist, der nicht gelesen werden kann (`VersionNumber`, `IsDefault`), die Version nicht erneut gelesen werden kann (weder über das Objekt
noch über `ILibrary.FindVersion`), das Live-`LibraryTypeVersionState` keinen Member des erwarteten Namens hat (über den exakten
Namen mit `LibraryRuntimeBinding.EnumValue` gesucht; das Stub-Enum deklariert keinen) oder ein `CreateFrom` ohne Rückgabewert gebunden ist, setzt der Worker das bedingte `unverified` des Ergebnisses (warum), das Item ist dennoch erfolgreich (der Aufruf kehrte ohne
Ablehnung zurück), und sein Verifikationseintrag ist `{ check: "notVerified", success: false }`, sodass der Aufruf
`success:false` mit der Warnung "could not be verified" meldet. K61: Die Lebenszyklus- und Kopiervorlagen-Items werden nach dem
Aufruf zusätzlich mit den Lesezugriffen von `library_read` zurückgelesen: `open_global_library`, `retrieve_global_library` und
`create_global_library` müssen in `list_libraries` stehen (der Pfad aus dem Ergebnis), `close_global_library` darf dort nicht mehr
stehen (Prüfung `openLibraries`); `create_master_copy` und `copy_master_copy` müssen im Wurzelordner der Kopiervorlagen ihrer
Bibliothek liegen (`copy_master_copy`: die Zielbibliothek), `delete_master_copy` muss dort fehlen (`get_library_tree` mit content
`masterCopies`, Prüfung `masterCopy`). Ein späteres Öffnen, Dearchivieren oder Anlegen einer Bibliothek überholt ein früheres
Schließen; ein späteres Schließen, das ein Inhalts-Item überholt, sagt, dass es die Änderung verworfen haben kann.
`save_global_library` bleibt `trusted` und nennt den Grund: kein dokumentierter Member meldet, ob eine globale Bibliothek
ungespeicherte Änderungen hat. Warnungen dieser Leseantworten (z. B. ein TIA-Portal-Dialog nach der Rückkehr eines Schließens)
erscheinen in den `warnings` des Aufrufs. Fehlgeschlagene Items ohne Rücklesung haben keinen Verifikationseintrag; ein
fehlgeschlagenes Item mit Rücklesung ist nie `success: true` (K58). Eine Siemens-`EngineeringTargetInvocationException` in einer Mutation
ist `engineering_rejected` mit `engineeringError`; andere Siemens-Fehler sind `worker_operation_failed`;
`portal_lost` hat Vorrang vor beiden. Jeder `library_write`-Aufruf hängt einen Audit-v2-Datensatz an (`tool: library_write`).

Ergebniswert jedes Schreib-Items `LibraryWriteResultInfo`: `operation`, `library`, `target`, `created[{ name,
runtimeType }]`, `nameAfter`, `versionAfter { versionNumber, guid, state, isDefault }`, `alreadyOpen`,
`returnedType` (Laufzeittyp eines Werts, dessen Typ die Quellen nicht dokumentieren, z. B. der von `UpdateProject`), `note`,
`unverified` (bedingt: nur vorhanden, wenn ein Zurücklesen nicht durchgeführt werden konnte, siehe oben) und die Bestätigungsevidenz `portalConfirmationPolicy`, `portalConfirmations`, `portalConfirmationsObserved`,
`portalConfirmationsComplete`.

### Schließen einer globalen Bibliothek

Die Quellen dokumentieren nur das parameterlose `Close()` und nur auf globalen Benutzerbibliotheken (A: `globalLibrary.Close()`
auf einer `UserGlobalLibrary`, Schlüsseltypen `UserGlobalLibrary.Close()`; L: "A `UserGlobalLibrary` ... closed deterministically
with `Close()`"), daher weist `close_global_library` globale System- und Corporate-Bibliotheken bei der Planung ab
(`target_kind_unsupported`, nichts läuft). Es gibt kein Argument `saveChanges`, und kein dokumentierter Member meldet, ob eine globale Bibliothek
ungespeicherte Änderungen hat (`IsModified` ist nur für Projekte dokumentiert). Daher wählt `close_global_library` nie
zwischen Speichern und Verwerfen:

- Der Host führt ein Ledger (`LibraryUnsavedChanges`, eines pro Worker-Client, sodass es Worker-Neustarts übersteht) der
  globalen Bibliotheken, die dieser Server geändert und nicht gespeichert hat: ein abgeschlossenes Inhalts-Item auf einer globalen Bibliothek (Kopiervorlage
  anlegen/löschen/umbenennen, das Ziel von `copy_master_copy`, Umbenennungen von Typen und Ordnern, Bearbeiten/Freigeben/Verwerfen/Default/
  Löschen von Versionen; Instanziierungen und `update_project` ändern das Projekt, nicht die Bibliothek) oder ein fehlgeschlagenes,
  dessen Fehler auf eine Änderung folgen kann (alles außer Abweisungen wegen Argumenten, Binding, Ziel und Capability;
  `engineering_rejected` zählt als mögliche Änderung, weil das Mutations-Flag die ganze Worker-Methode umfasst und
  daher nicht beweist, dass kein Setter lief). Ein erfolgreiches `save_global_library` oder `close_global_library` oder ein frisches
  Öffnen des Pfads vergisst die Bibliothek.
- Einträge sind nach dem TIA-Portal-Prozess, in dem die Änderung vorgenommen wurde (`PortalProcessId` des fixierten Bindings), und
  dem normalisierten Pfad geschlüsselt: globale Bibliotheken gehören zu einer Portal-Sitzung, daher blockiert eine in Portal A vorgenommene Änderung nie ein
  Schließen derselben Datei in Portal B, und ein Speichern, Schließen oder Öffnen in B vergisst nie die Änderung von A. Ein verifiziertes Binding
  trägt immer eine Portal-PID; wäre sie unbekannt, würde die Änderung auf jedes Portal passen, und ein Speichern, Schließen oder Öffnen
  würde nichts vergessen (das Schließen bleibt abgewiesen, statt fälschlich erlaubt zu werden). Eine wiederverwendete PID eines späteren Portal-Prozesses erbt veraltete Einträge; `save_global_library` oder ein frisches Öffnen löst das abgewiesene Schließen auf.
- Ein Öffnen ist nur dann frisch, wenn die Datei nicht bereits geöffnet war. Der Worker vergleicht `new FileInfo(path).FullName` mit
  `Path.FullName` jeder geöffneten Bibliothek (G: Prüfung auf eine bereits geöffnete Bibliothek "by comparing
  `GlobalLibrary.Path.FullName`", ordinal ohne Beachtung der Groß-/Kleinschreibung; `.`-Segmente und doppelte oder Vorwärts-Trennzeichen werden
  aufgelöst), und wenn `Open`, `OpenWithUpgrade`, `Retrieve` oder `Create` eine Bibliothek zurückgibt, die bereits vor
  dem Aufruf geöffnet war (dasselbe Live-Objekt per `Equals` oder derselbe vollständige Pfad), sagt das Ergebnis `alreadyOpen:true` mit
  einem Hinweis, und das Ledger behält seinen Eintrag.
- Ein Schließen löst den Info-Guard `library_close_unsaved_changes` aus, wenn das Ledger oder ein früheres Item desselben Aufrufs
  eine solche Änderung enthält, die kein `save_global_library` dazwischen abdeckt. Der Guard listet die Änderungen auf, die das Schließen
  verwirft; das Schließen läuft (read-write bestätigt es, wie jedes Lebenszyklus-Item). Zuerst speichern, um sie zu behalten. Ein Probelauf (dry run)
  meldet den Guard.
- Änderungen, die das Ledger nicht kennen kann (in der TIA-Portal-Oberfläche, von einem anderen Openness-Client oder vor dem Start des Servers
  vorgenommen), werden nicht erkannt. Wenn `Close()` dann eine TIA-Portal-Bestätigung auslöst, wird sie über
  `portalConfirmations` beantwortet und protokolliert; mit dem Default `decline` schlägt das Item fehl, und die Bibliothek kann bereits
  mit verworfenen Änderungen geschlossen sein. Vor einer Wiederholung `list_libraries` prüfen.

## Obergrenzen und Budgets

| Obergrenze | Wert |
| --- | --- |
| Items pro Aufruf | 1-50 |
| Baumtiefe / Seite | 1-32 (Default 16) / 1-500 (Default 200); der Durchlauf stoppt bei 5.000 Einträgen |
| Vergleichstiefe / Elemente | 1-64 (Default 16) / 1-5000 (Default 300) |
| Instanzen pro Version | 1.000 |
| Nachrichten der Aktualisierungsprüfung | 8 Ebenen, 500 Nachrichten |
| Dependencies, Dependents, Kopiervorlagen mit Instanzen | je 200 |
| Attribute pro Vergleichselement oder Detail | 20 |
| Namen / Pfade | 256 / 1.024 Zeichen; Texte bei 1.000 abgeschnitten |

Das Worker-Request-Timeout (`--worker-timeout-minutes`, Default 5) begrenzt jeden Aufruf. Dokumente verwenden die effektiven
Wert-/Dokumentbudgets (`--max-item-chars`, `--max-document-chars`); ein zu großer Wert wird als Ganzes mit
Hinweisen ausgelassen, nie abgeschnitten.

## Herkunft jedes verwendeten Siemens-Members

Quellen (schreibgeschützte Referenzdaten, nie ausgeführt): **(L)** `totally-integrated-claude`
`skills/tia-project-general/references/library-operations.md` (gibt an, gegen die installierte
V21 `Siemens.Engineering.Base.dll`/`.xml` geprüft zu sein); **(G)** Siemens `tia-portal-ai-extensions`
`openness_development/skills/global-library/SKILL.md`; **(A)** `.../libraries-and-alarms/SKILL.md`; **(E)**
`.../engineering-objects/SKILL.md`; **(S)** `.../session-and-project/SKILL.md`; **(B)** die V21-Typnamen-Baseline,
aus echten DLL-Metadaten extrahiert (`api-baselines/v21/base.json`, `step7.json`; nur Typnamen und Assemblies).

Status: **compiled** (deklarierender Typ, Rückgabetyp und Parameter werden alle von der Quelle gezeigt; in den
Stubs deklariert und durch die Probe fixiert); **runtime** (die Quelle nennt den Member und seine Argumente, aber nicht seinen Rückgabe-,
deklarierenden oder Collection-Parametertyp: auf dem Live-Objekt über den exakten Namen und die dokumentierten Argumenttypen mit `LibraryRuntimeBinding` gebunden, genau ein anwendbarer öffentlicher Member, sonst Abbruch (fail closed)); **runtime,
parameterless** (die Quelle nennt eine Selbstaktion auf dem Objekt ohne Parameterliste; als parameterlose Methode gebunden,
Abbruch (fail closed)); **runtime lookup** (eine benannte Eigenschaft, über den Namen gelesen; informative Member
fallen auf `unreadable` zurück); **generic** (die dokumentierte `IEngineeringObject`-Attribut-API); **existing** (ein
unveränderter bestehender Stub). Jeder deklarierte Typ befindet sich in der Baseline-Assembly, in der die Stubs ihn deklarieren (B), fixiert durch
`LibraryReferenceStubTests`.

| Siemens-Member | Assembly | Quelle | Status |
| --- | --- | --- | --- |
| `Project.ProjectLibrary` → `ProjectLibrary` | Base | L | existing |
| `TiaPortal.GlobalLibraries` → `GlobalLibraryComposition` | Base | L | compiled |
| `ProjectLibrary`, `GlobalLibrary` implementieren `ILibrary`; `ILibrary.TypeFolder` → `LibraryTypeSystemFolder`, `.MasterCopyFolder` → `MasterCopySystemFolder` | Base | L | compiled |
| `ILibrary.FindType(Guid)` → `LibraryType`, `ILibrary.FindVersion(Guid)` → `LibraryTypeVersion` | Base | L | compiled |
| `LibraryType.Guid` (`Guid`), `LibraryType.Name` (`string`) | Base | L (Guid an `FindType` übergeben), G (`parentType.Name` als String verglichen) | compiled |
| `LibraryTypeVersion.Guid` (`Guid`), `.State` (`LibraryTypeVersionState`), `.Edit()` → `LibraryTypeVersion` | Base | L (`selected.State == LibraryTypeVersionState.Committed`; der Katalog: "`InWork` or `Committed`") | compiled; Zustandsmember (`InWork`, `Committed`) über den Namen verglichen (Auflistung) und für die Freigabe-/Bearbeitungsevidenz gegen den Live-Enum-Member genau dieses Namens (`LibraryRuntimeBinding.EnumValue`; fehlt er → `notVerified`) |
| `MasterCopy.Name` (`string`) | Base | L (`target.Find(created.Name)`) | compiled |
| `MasterCopyComposition.Create(IMasterCopySource)` → `MasterCopy` | Base | L ("installed signature"); A zeigt den Aufruf | compiled |
| `MasterCopyComposition.CreateFrom(MasterCopy)` → `MasterCopy` | Base | L | compiled |
| `MasterCopyFolder.MasterCopies`, `MasterCopyComposition.Find(string)` und Enumeration | Base | bestehend (Vergleichsfeature) | existing |
| `GetService<LibraryTypeInstanceInfo>()` auf `PlcBlock`/`PlcType` (über `IEngineeringServiceProvider`) | Base / Step7 | G | compiled (bestehendes `GetService<T>`) |
| In `is`/`as`/`typeof` verwendete Typen: `GlobalLibrary`, `UserGlobalLibrary`, `SystemGlobalLibrary`, `CorporateGlobalLibrary`, `LibraryTypeFolder`, `LibraryTypeSystemFolder`, `LibraryTypeUserFolder`, `LibraryTypeInstanceInfo`, `IMasterCopySource`, `IInstanceSearchScope`, `IUpdateProjectScope`, `ILibraryTypeOrFolderSelection` | Base | L, G, B | compiled (Typen; Spezialisierungen aus dem Katalog von L, Ordnerhierarchie aus G, Interfaces aus L/G) |
| `CodeBlockLibraryType`, `CodeBlockLibraryTypeVersion` (`SW.Blocks`), `PlcTypeLibraryType`, `PlcTypeLibraryTypeVersion` (`SW.Types`) | Step7 | G §4-§5, B | compiled (Typen) |
| Enums `OpenMode`, `MasterCopyMode`, `UpdatePathsMode`, `CreateOrReleaseDependenciesMode`, `UpdateCheckMode` | Base | L, G, S, B | compiled ohne Member; Werte zur Laufzeit über den exakten Namen geparst |
| Member aus Issue #17 (V21): `UserGlobalLibrary.Archive(DirectoryInfo, String, LibraryArchivationMode)`, `.SaveAs(DirectoryInfo)`, `.CleanUpLibrary(IEnumerable<ILibraryTypeOrFolderSelection>)`; `ProjectLibrary.CleanUpLibrary(…, CleanUpMode)`, `.UpdateProject(…, DeleteUnusedVersionsMode)`; `ProjectLibrary`/`GlobalLibrary` `.HarmonizeProject(…, HarmonizeProjectOptions)`, `.UpdateLibrary(…, ILibrary[, ForceUpdateMode, DeleteUnusedVersionsMode, StructureConflictResolutionMode])`; `GlobalLibrary.UpdateProject(…, ForceUpdateMode, DeleteUnusedVersionsMode, StructureConflictResolutionMode)`; `GlobalLibraryComposition.RetrieveWithUpgrade(FileInfo, DirectoryInfo, OpenMode)`; `LibraryType.Delete()`, `.DoNotUse` (get/set), `.GetSupportedExportFormats()`, `.CompareTo(LibraryType)`, `.Author`, `.Comment`, `.Namespace`, `.SetForUpdate`, `.Status`; `LibraryTypeVersion.Export(FileInfo, ExportOptions[, DocumentInfoOptions])`, `.ExportAsDocuments(DirectoryInfo, String, String, LibraryExportOptions)`, `.CompareTo(LibraryTypeVersion)`, `.Edit(LibraryTypeInstanceInfo)`; `LibraryTypeComposition.CreateFromDocuments(DirectoryInfo, String[, IEngineeringObject], LibraryImportOptions)`; `LibraryTypeVersionComposition.CreateFromDocuments(DirectoryInfo, String[, IEngineeringObject], CreateOptions, LibraryImportOptions)`; `LibraryTypeUserFolderComposition.Create(String)`, `LibraryTypeUserFolder.Delete()`; `MasterCopyFolder.Folders`, `MasterCopyUserFolderComposition.Create(String)`, `MasterCopyUserFolder.Delete()`, `.Name`; `MasterCopy.CompareTo(MasterCopy)`; `DetailedCompareResult.Properties`, `DetailedCompareResultElement.Description/.DetailCompareStatus/.LeftValue/.RightValue`; `TypeCreateTransferResults.CreatedType/.Messages/.TransferResultState`, `VersionCreateTransferResults.CreatedVersion/…`, `ExportTransferResult.ExportedDocuments/…`, `TransferResultMessage.Message`; enums `LibraryArchivationMode`, `CleanUpMode`, `HarmonizeProjectOptions`, `ForceUpdateMode`, `DeleteUnusedVersionsMode`, `StructureConflictResolutionMode`, `LibraryImportOptions`, `LibraryExportOptions`, `CreateOptions`, `ExportOptions`, `DocumentInfoOptions` (alle Member), `CreateOrReleaseDependenciesMode` and `UpdateCheckMode` (beide Member) | Base | V21 | runtime (`LibrarySignatureBinding`: Enums über den vollständigen Typnamen, Collections über den Elementtyp; Eigenschaften und Ergebnisse Laufzeit-Lookups); kein Stub |
| Enumeration von `GlobalLibraryComposition` | Base | G (`OfType<GlobalLibrary>()`) | runtime (nicht generisches `IEnumerable`) |
| `GlobalLibrary.Path` | Base | G (`Path.FullName`) | runtime lookup; eine Bibliothek ohne lesbaren Pfad ist nicht adressierbar |
| `GlobalLibrary.Name` | Base | keine (informativ) | runtime lookup (Eigenschaft, dann Attribut); null, wenn nicht vorhanden |
| `GlobalLibraryComposition.GetGlobalLibraryInfos()`; `GlobalLibraryInfo.LibraryType`, `.Name`, `.Path` | Base | L | runtime; runtime lookup |
| `GlobalLibraryComposition.Open(GlobalLibraryInfo)` („The global library info associated with a global library to be opened“) | Base | L | runtime (K80: System- und Corporate-Bibliotheken, gefunden über `GlobalLibraryInfo.Path`/`.LibraryType`) |
| `GlobalLibraryComposition.Open(FileInfo, OpenMode)` | Base | L | runtime (Rückgabetyp nicht dokumentiert; L castet ihn) |
| `GlobalLibraryComposition.OpenWithUpgrade(FileInfo)` | Base | G, S | runtime |
| `GlobalLibraryComposition.Retrieve(FileInfo, DirectoryInfo, OpenMode)` | Base | G | runtime |
| `GlobalLibraryComposition.Create<UserGlobalLibrary>(DirectoryInfo, string)` | Base | A | runtime (generisch, geschlossen über `UserGlobalLibrary`) |
| `UserGlobalLibrary.Save()` | Base | L (nennt `Save`) | runtime, parameterless |
| `UserGlobalLibrary.Close()` | Base | A (`globalLibrary.Close()` auf einer angelegten `UserGlobalLibrary`; Schlüsseltypen: `UserGlobalLibrary.Close()`), L ("A `UserGlobalLibrary` opened by the application must be saved when intended and closed deterministically with `Close()`"; Katalog: `UserGlobalLibrary` "save, archive, close") | runtime, parameterless, nur auf globalen Benutzerbibliotheken (keine Quelle zeigt es auf `SystemGlobalLibrary` oder `CorporateGlobalLibrary`) |
| `LibraryTypeFolder.Folders` (untergeordnete `LibraryTypeUserFolder`s), `LibraryTypeFolder.Types` (direkte untergeordnete Typen) | Base | G (auf der Basisklasse gelesen, nie auf dem abgeleiteten Shadow) | runtime (auf dem deklarierenden Basistyp) |
| Name eines Typordners | Base | G (Ordner bilden `GlobalLib://`-Pfade; kein Member genannt) | runtime lookup (Eigenschaft `Name`, dann Attribut); unlesbare Ordner können nicht adressiert werden |
| `LibraryType.Versions` | Base | L (`foreach (LibraryTypeVersion version in type.Versions)`) | runtime |
| `LibraryTypeVersion.VersionNumber`, `.IsDefault`, `.Dependencies`, `.Dependents`, `.MasterCopiesContainingInstances`, `.OriginalLibrary` | Base | L (Namen), G (Ordnung nach `VersionNumber`) | runtime lookup (informativ; `unreadable`, wenn nicht vorhanden) |
| `LibraryTypeVersion.FindInstances(IInstanceSearchScope)` mit `PlcSoftware` als Scope | Base | L | runtime |
| `LibraryTypeInstanceInfo.LibraryTypeVersion` | Base | G | runtime lookup |
| `MasterCopy.ContentDescriptions[].ContentType` | Base | G | runtime lookup |
| `UpdateCheck(Project, UpdateCheckMode)`; `UpdateCheckResult.Messages`, `UpdateCheckResultMessage.Description`, `.MessageParts`, `.Messages` | Base | L | runtime (deklarierender Typ nicht dokumentiert: auf der Live-Bibliothek gebunden); runtime lookup |
| `CompareToLibrary(ISoftwareCompareTarget)`; `LibraryCompareResult.RootElement`, `.Elements`, `.ComparisonResult`, `.DetailedInformation.Properties` | Base | L | runtime; runtime lookup; Element- und Detailattribute generic (E) |
| `UpdateProject(<collection of ILibraryTypeOrFolderSelection>, <collection of IUpdateProjectScope>)` | Base | G (`lib.UpdateProject(typeSelections, new List<IUpdateProjectScope> { plcSoftware })`), L (Rollen im Katalog) | runtime: die einzige Überladung mit zwei Parametern, die eine `List<T>` (oder `T[]`) jedes dokumentierten Elementtyps akzeptiert |
| `LibraryTypeVersion.Release(CreateOrReleaseDependenciesMode, Version, string, string)` | Base | L ("the exact installed method") | runtime (Rückgabe nicht dokumentiert) |
| `LibraryTypeVersion.Discard()`, `.SetAsDefault()`, `.Delete()` | Base | L (nennt `Discard`, `SetAsDefault`, `Delete`) | runtime, parameterless |
| `MasterCopy.Delete()` | Base | L (`Delete()`) | runtime |
| `CreateFrom(MasterCopy, MasterCopyMode)` auf `PlcBlockGroup.Blocks`, `PlcTypeGroup.Types`, `PlcTagTableGroup.TagTables`, `TechnologicalObjectGroup.TechnologicalObjects`, `PlcUnitProvider.UnitGroup.Units` | Step7 | G §7 (A für `GetService<PlcUnitProvider>()`) | runtime (Rückgabe nicht dokumentiert; Gruppen und Compositions bestehend) |
| `PlcExternalSourceComposition.CreateFrom(MasterCopy, MasterCopyMode)` auf `PlcSoftware.ExternalSourceGroup.ExternalSources` (Zielart `externalSources`, hinzugefügt durch `feature/external-sources-folders`) | Step7 | `totally-integrated-claude` `tia-plc-operations/references/external-sources.md` V21-API-Liste | runtime (Rückgabe nicht dokumentiert; Gruppe und Composition bestehend) |
| `CreateFrom(MasterCopy)` auf `Project.Devices` und auf den `DeviceItems` eines Geräteelements | Base | G §9 | runtime (deklarierender Typ nicht dokumentiert) |
| `PlcAlarmUserTextlistComposition.CreateFrom(MasterCopy, MasterCopyMode)` auf `PlcSoftware.PlcAlarmTextlistGroup.PlcAlarmUserTextlists` | Step7 | `tia-plc-operations/references/alarms.md` (beide `CreateFrom`-Überladungen), A (`PlcAlarmTextlistGroup.PlcAlarmUserTextlists`) | runtime (Rückgabe nicht dokumentiert; Gruppe und Composition compiled, siehe die [Meldungs-Herkunft](PLC_ALARMS_TEXTLISTS_SUMMARY.md#provenance-of-every-siemens-member-used)) |
| Eine Anwender-Textliste als Quelle von `MasterCopies.Create` | Step7 | A (`MasterCopies.Create(alarmTextList)`) | Laufzeitprüfung `as IMasterCopySource` (nicht auf dem Stub deklariert) |
| `Blocks.CreateFrom(CodeBlockLibraryTypeVersion, UpdatePathsMode)`, `Types.CreateFrom(PlcTypeLibraryTypeVersion, UpdatePathsMode)` | Step7 | G §6 | runtime (Rückgabe nicht dokumentiert) |
| Umbenennungen: `IEngineeringObject.GetAttributeInfos()`, `EngineeringAttributeInfo.Name/.AccessMode`, `SetAttribute("Name", value)`, `GetAttribute("Name")` | Base | E (generische API, zuerst `GetAttributeInfos` prüfen), G (Umbenennen über `SetAttribute("Name", …)`) | generic (bestehend) |
| `TiaPortal.Confirmation` / `ConfirmationEventArgs.Result` (`ConfirmationResult.Yes`/`No`) und das Lesen des Dialogtexts | Base | bestehend (`TiaPortalSession`, Scope und Textleser von plc_download) | existing; kein neuer Member |
| PLC-Gruppen, `PlcSoftware.BlockGroup/TypeGroup/TagTableGroup/TechnologicalObjectGroup`, `Groups`, `PlcTagTableComposition.Find`, `ProjectBase.Devices`, `HardwareObject.DeviceItems`, Auflösung von Geräteelement-Selektoren, Resolver für `PlcBlock`/`PlcType` | Base / Step7 | bestehend | existing |

Stub-Konventionen, die keine Siemens-Fakten sind: die neuen Enums deklarieren keine Member, die Interface-Listen von `IEngineeringObject`/
`IEngineeringInstance` folgen dem bestehenden Stub-Stil, Parameternamen sind Platzhalter, und
`GlobalLibraryComposition` deklariert kein Enumerations-Interface (der Worker zählt es als `IEnumerable` auf).

### Ausgelassen, und warum

| Angefragte / erwogene Operation | Grund |
| --- | --- |
| Die Überladungen pro Typ `LibraryType.UpdateLibrary(...)` und `LibraryType.UpdateProject(...)` | die Überladungen auf Bibliotheksebene mit einer Typauswahl (`update_library`, `update_project`) rufen dieselbe Funktion; die pro Typ nehmen ihre Modi in anderer Reihenfolge und bringen nichts hinzu |
| Kombinierte `ExportOptions` (z. B. `WithDefaults` und `WithReadOnly`) | V21 sagt nicht, dass `ExportOptions` ein Flags-Enum ist; ein Member pro Aufruf |
| `Author`/`Comment` eines Typs oder einer Version, `SetForUpdate` eines Typs setzen | V21 dokumentiert diese Eigenschaften ohne "Gets or sets"; sie werden gelesen (`get_type`), nicht geschrieben |
| Eigenschaften globaler Bibliotheken (`Author`, `Comment`, `IsModified`, `IsReadOnly`, `HistoryEntries`, ...) | von Issue #17 nicht verlangt; `IsModified` könnte das Journal ungespeicherter Änderungen von `close_global_library` ersetzen und ist ein offener Folgepunkt |
| Vergleichsfilter (`differencesOnly`) und Elementnamen in `compare_libraries` | V21 nennt `LibraryCompareResultState` (`ObjectsIdentical`, `ContainerContentsIdentical`, ...) und `LeftName`/`RightName`; der Baum bleibt wie bisher schemafrei (Folgepunkt) |
| Platzieren in die eigenen Baustein-/Typ-/Variablentabellen-Gruppen einer Software Unit; Geräte-Benutzergruppen als Ziele | in dieser Iteration nicht implementiert (nur Gruppen der PLC-Wurzel, die Unit-Composition der PLC, Projektgeräte und Geräteelemente) |
| Automatische Zuordnung einer Kopiervorlage nach Inhaltstyp (G §7) | bewusst: der Aufrufer wählt `target.kind` anhand von `contentTypes`, nichts wird abgeleitet |
| Eine globale Bibliothek neu laden, Glob-Suche, einen Typ als Multiinstanz hinzufügen (Ideen aus tiacommander) | kein Reload-Member dokumentiert; die Suche erfolgt clientseitig über `get_library_tree`; Multiinstanz-Member gehören zur Bearbeitung von Bausteinschnittstellen, nicht zu einer Bibliotheks-API |
| Transaktionen / `ExclusiveAccess` um Bibliotheks-Schreibzugriffe | L: "where the participating API supports it", ohne zu sagen, welche das tun; keine wird verwendet |
| Lokale Multiuser-Sitzungen | die Projektbibliothek ist nur auf `Project` dokumentiert; nur Standalone-Projekte |
| Schließen einer globalen System- oder Corporate-Bibliothek | `Close()` ist nur für `UserGlobalLibrary` belegt (A, L); `close_global_library` weist andere Arten ab (`target_kind_unsupported`) |
| `close_global_library` mit einer Wahl zwischen Speichern und Verwerfen (`saveChanges`); Erkennen ungespeicherter Änderungen, die außerhalb dieses Servers vorgenommen wurden | nur das parameterlose `Close()` ist dokumentiert; das Journal nennt die eigenen ungespeicherten Änderungen dieses Servers ([Schließen einer globalen Bibliothek](#schließen-einer-globalen-bibliothek)) |

## Bekannte Grenzen und offene Risiken

- Nicht live getestet. Die meisten Member sind zur Laufzeit gebunden: jeder Name, Argumenttyp oder jede Rückgabeform, die sich im
  installierten V21 unterscheidet, bricht mit `unsupported_capability` ab (fail closed, nichts aufgerufen) – aber ein Member, der mit einer
  anderen Bedeutung als angenommen existiert (insbesondere `Save()`, `Discard()`, `SetAsDefault()`, `Delete()`, als
  parameterlos gebunden, und das dritte/vierte String-Argument von `Release`), würde sich erst in den Live-Prüfungen zeigen.
- `UpdateProject` ist über seine dokumentierte Aufrufform gebunden: wenn mehrere Überladungen mit zwei Parametern die
  Collections akzeptieren, wird nichts gewählt (`unsupported_capability` mit Angabe der Anzahl).
- Welche Items TIA-Portal-`Confirmation`-Dialoge auslösen, ist nicht dokumentiert. Jeder wird durch die
  `portalConfirmations` des Items beantwortet und protokolliert; mit dem Default `decline` schlägt ein solches Item fehl, auch wenn der Siemens-Aufruf
  abgeschlossen wurde. Ein Dialog, der nach der Rückkehr des Items ausgelöst wird, behält die Antwort des Items bis zur nächsten Worker-Anfrage und
  ist nur eine Warnung auf dieser nächsten Antwort (die zu einem anderen Tool gehören kann, wenn keine Bibliotheksanfrage folgt).
- Das Ledger ungespeicherter Änderungen kennt nur die eigenen Änderungen dieses Servers (siehe [Schließen einer globalen
  Bibliothek](#schließen-einer-globalen-bibliothek)); es kann auch veraltet sein (eine in der Oberfläche gespeicherte oder geschlossene Bibliothek): dann wird das abgewiesene
  Schließen durch `save_global_library` oder ein frisches Öffnen aufgelöst.
- `create_global_library` und `retrieve_global_library` liefern die Bibliothek, die TIA Portal geöffnet hat; ihr Pfad wird
  aus dem zurückgegebenen Objekt zurückgelesen und muss unter den geöffneten Bibliotheken aufgeführt sein (sonst `postcondition_failed`,
  nachdem die Dateien geschrieben wurden).
- `instantiate_*` mit `newName` benennt nach dem Anlegen um: eine Ablehnung des Umbenennens lässt das Item fehlschlagen, nachdem das Objekt
  unter seinem Bibliotheksnamen angelegt wurde (keine Bereinigung).
- Die Typinstanziierung aus einer globalen Bibliothek synchronisiert die Version zuerst in die Projektbibliothek (L); diese zusätzliche
  Mutation wird als Guard gemeldet, aber nicht separat verifiziert.
- Pfade globaler Bibliotheken und Ordner-/Typnamen werden ohne Beachtung der Groß-/Kleinschreibung abgeglichen und müssen eindeutig sein; die eigenen
  Eindeutigkeitsregeln von TIA Portal werden nicht verifiziert. Bibliotheken ohne lesbaren `Path` werden in `unreadable` aufgeführt und
  können nicht adressiert werden.
- Die Liste der geöffneten Bibliotheken und der Baum werden per Enumeration gelesen; ein Siemens-Fehler während der Enumeration beendet die
  Enumeration (als Warnung auf stderr gemeldet), daher ist bei einer fehlerhaften
  Installation eine fehlende Bibliothek oder ein fehlender Eintrag möglich.

## Live-Abnahmeprüfungen

Zuerst auf einem Wegwerf-Standalone-Projekt und auf Kopien von Bibliotheken ausführen:

1. `library_read list_libraries`, `list_global_library_infos`: Pfade und Arten stimmen mit dem TIA-Portal-Bereich "Global
   libraries" überein; die Projektbibliothek ist aufgeführt; keine `unreadable`-Einträge.
2. `get_library_tree` auf der Projektbibliothek und auf einer geöffneten globalen Bibliothek: Ordner, Typen, Versionen (`state`,
   `isDefault`, `versionNumber`-Texte) und Wurzel-Kopiervorlagen (`contentTypes`) stimmen mit der Task Card Bibliotheken überein;
   mit `offset`/`limit` blättern; `maxDepth` prüfen.
3. `get_type`, `find_type_instances` (`FindInstances` mit der PLC), `get_instance_info` auf einer Instanz und auf einem
   aus einer Kopiervorlage platzierten Baustein (`linked:false`).
4. `update_check` gegen ein veraltetes Projekt (Nachrichten und Verschachtelung) und `compare_libraries` Projekt vs.
   global (protokollieren, welche Attribute die Elemente bereitstellen).
5. `open_global_library` read-only, read-write und mit `upgrade` (auf einer Kopie einer älteren Bibliothek); die
   Rückfrage in read-write und keine in full bestätigen; `retrieve_global_library` einer `.zal21`; `create_global_library`;
   `save_global_library`; `close_global_library` mit und ohne ungespeicherte Änderungen: die Abweisung nach einer Änderung über
   `library_write`, dann (nach einer Änderung in der Oberfläche) der protokollierte Dialog mit `decline` und mit `accept`, und
   ob die Änderungen gespeichert oder verworfen wurden; jeden Dialog-`type`/`text` protokollieren, den die Items auslösen. Dieselbe
   Datei erneut unter einer anderen Schreibweise öffnen (`.`-Segment, Vorwärts-Schrägstriche): `alreadyOpen:true`, nichts aufgerufen;
   `close_global_library` einer geöffneten Systembibliothek: `target_kind_unsupported`.
6. `create_master_copy` aus einem Baustein, einem UDT, einer Variablentabelle und einem Gerät; `copy_master_copy` in eine globale
   Bibliothek; `rename_master_copy`; `delete_master_copy`.
7. `instantiate_master_copy` in jede Zielart (`ThrowIfExists` auf einen vorhandenen Namen → `engineering_rejected`;
   `Rename`; `Replace`), mit `newName`; danach übersetzen; den Laufzeit-Rückgabetyp jedes `CreateFrom` protokollieren
   (`returnedType`) und dass keines `notVerified` ist.
8. `instantiate_type_version` eines Codebausteins und einer UDT-Version aus der Projektbibliothek und aus einer globalen
   Bibliothek (die Synchronisierung der Projektbibliothek prüfen); jeder `updatePathsMode`.
9. `update_project` für einen Typ und für einen Ordner; mit der Aktualisierungsprüfung vergleichen.
10. Typ-Workflow: `edit_type_version` → die Testinstanz mit `plc_write` bearbeiten → `compile_check` →
    `release_type_version` (protokollieren, was das dritte und vierte Argument in TIA Portal zeigen und dass die freigegebene
    Version ihre GUID behält, `Committed` und die angeforderte Nummer liest, also `trusted` ist, nicht `notVerified`) →
    `set_default_type_version` → `discard_type_version` auf einer anderen in Bearbeitung befindlichen Version (protokollieren, ob sie
    aus `FindVersion` verschwindet oder ihren Zustand ändert) → `delete_type_version`.
11. `rename_type`, `rename_type_folder`; bestätigen, dass `GetAttributeInfos` `Name` auf Typen, Ordnern
    und Kopiervorlagen als schreibbar aufführt.
12. Ein Fehlerpfad der Laufzeitbindung: eine Member-Abweichung der Installation muss `unsupported_capability` ohne
    Änderung melden (mit den Lesezugriffen prüfen).
