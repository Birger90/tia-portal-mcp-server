# Claude in TIA: Arbeit aus TIA Portal heraus an Claude übergeben

**Sprache:** [English](CLAUDE_IN_TIA.md) · Deutsch

> **Status: nicht live getestet.** Alles auf dieser Seite ist nur offline geprüft: Das Add-In wird gegen eine reine
> Kompilier-Attrappe (Stub) der V21-Add-In-API übersetzt; der Ein-/Ausgangsvertrag, das MCP-Werkzeug `tia_inbox` und die
> Logik der Brücke sind unter Linux unit-getestet. Nichts lief bisher in TIA Portal oder mit einer installierten Claude-Code-CLI.
> Die [Liste der ungeprüften Punkte](#ungeprüft) nennt jede Annahme; die [einmalige Einrichtung](#einmalige-einrichtung-am-engineering-pc)
> ist die erste Live-Prüfung.

## Was es tut

Das TIA-Portal-V21-Add-In „Claude in TIA“ fügt der Projektnavigation, dem Editor „Geräte & Netze“, der Projektbibliothek
und der globalen Bibliothek ein Kontextmenü **Claude** hinzu. Seine Einträge übergeben die markierten Objekte an Claude:

| Menüeintrag | Aktion (`action`) | Auftrag an Claude |
| --- | --- | --- |
| An Claude übergeben… | `handover` | Objekte ansehen und tun, was der Anfragetext sagt, oder in der Antwort nachfragen |
| Erklären | `explain` | erklären, was die Objekte tun; nur lesen |
| Prüfen | `review` | Fehler, Risiken und Stilprobleme auflisten; nur lesen |
| Überarbeiten | `revise` | die Objekte im Rahmen des Zugriffsmodus überarbeiten und jede Änderung nennen |
| Dokumentieren | `document` | Dokumentation schreiben; Kommentare nur auf Wunsch ändern |
| Claude-Antworten anzeigen | — | ungelesene Antworten in einem TIA-Portal-Meldungsfenster zeigen und als gelesen markieren |

Das Add-In kann Claude nicht aufrufen. Claude handelt nur über den MCP-Server, deshalb läuft die Übergabe über Dateien:

1. Ein Klick schreibt eine **Anfrage** nach `%APPDATA%\TiaMcpServer\inbox\<id>.json`: Projektpfad, Herkunft, Aktion, die
   markierten Objekte (Art, Name, MCP-Adresshinweise, die rohe Kette der V21-Typnamen und Namen bis zum Projekt) und die
   Zeit. **Keine Projektdaten** (Bausteininhalt, Werte, Passwörter) werden kopiert.
2. Claude liest sie mit dem MCP-Werkzeug **`tia_inbox`** – wenn der Nutzer „schau in TIA“ sagt (MCP kann Claude nichts
   zuschieben) oder automatisch über die optionale **Claude-Brücke** (unten).
3. Claude arbeitet mit den übrigen Werkzeugen des Servers (der Zugriffsmodus entscheidet weiterhin, was geändert werden darf)
   und schließt mit `tia_inbox reply_request` ab; das schreibt `%APPDATA%\TiaMcpServer\outbox\<id>.json`.
4. In TIA Portal zeigt **Claude → Claude-Antworten anzeigen** die Antworten und markiert sie als gelesen.

Ein Freitextfeld gibt es in TIA Portal nicht: Die V21-Add-In-API dokumentiert keinen Eingabedialog (nur
`MessageBoxProvider.ShowNotification`/`ShowConfirmation`), daher bietet das Menü feste Aktionen. Zusätzliche Anweisungen
gehen in den Chat („schau in TIA und mach X“); das Feld `text` der Anfrage ist für eine spätere Fassung vorgesehen.

## Anfrage-, Antwort- und Statusdateien

Gemeinsamer, Siemens-freier Quelltext: `TiaMcpServer.Contracts/Shared/ClaudeInbox` (wird auch in das Add-In übersetzt; ein
abhängigkeitsfreier JSON-Schreiber/-Leser, weil das Add-In in der Add-In-Sandbox von TIA Portal läuft). Geschlossene
Schemata: Jedes Feld wird geschrieben (`null`, wenn nicht gesetzt), unbekannte Felder werden abgelehnt.

| Datei | Schema | Felder |
| --- | --- | --- |
| `inbox\<id>.json` | `tia-mcp-claude-request/1` | `id` (`yyyyMMddTHHmmssZ-xxxxxxxx`), `createdUtc`, `source` (`projectTree`, `devicesAndNetworks`, `projectLibrary`, `globalLibrary`), `action`, `text` (≤ 4000), `projectPath`, `selection[]` (≤ 50: `kind`, `name`, `chain[]` (≤ 32: `typeNames[]`, `name`), `address`), `addInVersion` |
| `outbox\<id>.json` | `tia-mcp-claude-reply/1` | `id`, `repliedUtc`, `status` (`done`/`failed`), `text` (≤ 60000), `from` (`claude`/`bridge`), `readUtc` |
| `outbox\<id>.status.json` | `tia-mcp-claude-status/1` | `id`, `state` (`queued`, `running`, `finished`, `failed`, `timedOut`, `skipped`), `updatedUtc`, `message` |

Jede Datei wird über eine temporäre Datei geschrieben und dann an ihren Platz verschoben. Eine Anfrage hat höchstens eine
Antwort: Ein zweites `reply_request` wird abgelehnt (`already_answered`). Wurzel ist `%APPDATA%\TiaMcpServer`; Server und
Brücke nehmen stattdessen `TIA_MCP_INBOX_ROOT` an (das Add-In nutzt immer `%APPDATA%`).

`address` enthält die Hinweise, die die MCP-Werkzeuge erwarten, gebildet von `ClaudeSelectionAddressing` aus der Kette:
`plcName`, `unitName`, `blockPath` (`PLC_1/Blocks/Ordner/Baustein`, `PLC_1/Units/U/Blocks/...`; bei einer Bausteingruppe deren
Pfad), `typePath` (`PLC_1/Types/Ordner/Typ`), `tableName` + `folderPath` (`/`, `/Linie/Zelle`), `deviceName`,
`deviceItemName`, `library` (`project` oder der Pfad der globalen Bibliothek), `libraryTypePath` (`/Ordner/Typ`),
`masterCopyName` + `masterCopyFolderPath`. Es sind Hinweise: Claude bestätigt sie vor der Verwendung mit
`browse_project_tree`, `plc_read`, `network_read` oder `library_read`. Ein Objekt anderer Art (HMI-Bilder, Technologieobjekte
…) ist `other` ohne Adresse; seine Kette nennt es trotzdem.

## MCP-Werkzeug `tia_inbox`

In **jedem** Zugriffsmodus verfügbar (13/32/32 Werkzeuge). Es berührt weder TIA Portal noch das Projekt noch die Bindung;
sein einziger Schreibvorgang ist die Antwortdatei (Annotationen: nicht read-only, nicht destruktiv, geschlossene Welt).
Batch mit bis zu 50 Operationen, Vertrag `1.0`, Hülle `OperationBatchReadResponse`:

| Operation | Felder | Ergebnis |
| --- | --- | --- |
| `list_requests` | optional `includeAnswered` | `inboxDirectory`, `outboxDirectory`, `pendingCount`, `requests[]` (`id`, `createdUtc`, `source`, `action`, `text`, `projectPath`, `selectionCount`, `answered`, `bridgeState`, `error`) |
| `get_request` | `id` | die ganze Anfrage plus `instruction` (was die Aktion verlangt), `reply`, `bridgeState` |
| `reply_request` | `id`, `text`, optional `status` (`done`, `failed`) | `id`, `status`, `repliedUtc`, `replyFile` |

Fehler: unbekannte Id `target_not_found`, eine zweite Antwort `state_changed`, eine unbrauchbare Anfragedatei
`validation_error`. Die Werkzeugbeschreibung weist Claude an, bei „schau in TIA“ / „look in TIA“ in den Eingang zu sehen.

## Claude-Brücke (optional, standardmäßig aus)

`TiaMcpClaudeBridge.exe` (Projekt `TiaMcpServer.ClaudeBridge`, net10.0, Siemens-frei) startet Claude **ohne Chat** für jede
neue Anfrage:

- beobachtet den Eingang (FileSystemWatcher plus Neueinlesen alle 30 s), eine Anfrage nach der anderen, eine Instanz je Benutzer;
- liest die Einstellungen vor jedem Durchgang neu: Ein- und Ausschalten braucht keinen Neustart;
- trägt jede Id **vor** dem Lauf in `%APPDATA%\TiaMcpServer\bridge\state.json` ein, sodass keine Anfrage je zweimal läuft
  (auch nach einem Absturz); eine unlesbare Zustandsdatei hält die Brücke an, statt eine Wiederholung zu riskieren;
- überspringt (Status `skipped`) Anfragen, die älter als 24 Stunden sind, ungültige Anfragedateien und inzwischen
  beantwortete Anfragen;
- startet den eingestellten Befehl ohne Shell (Argumentliste), den Prompt über die Standardeingabe, mit dem Zeitlimit
  (Status `timedOut`, Prozessbaum beendet);
- endet der Lauf ohne Antwort über `tia_inbox`, schreibt sie die letzte Ausgabe der CLI (Feld `result` der JSON-Ausgabe,
  sonst die Rohausgabe; `failed` mit der Fehlerausgabe, wenn der Lauf fehlschlug) als Antwort (`from: bridge`);
- protokolliert nach `%LOCALAPPDATA%\TiaMcpServer\claude-bridge\bridge.log` (Rotation bei 1 MB).

Der Prompt (`ClaudeBridgePrompt`) enthält die Anfrage und weist Claude an, nur die Werkzeuge des TIA-MCP-Servers zu nutzen,
`tia_inbox get_request` aufzurufen, nie ein Projekt zu öffnen/schließen/speichern und nie zu laden oder online zu gehen,
außer die Anfrage verlangt genau das, und mit `tia_inbox reply_request` auf Deutsch abzuschließen.

### Einstellungen (`settings.json` Version 2, Feld `claudeBridge`)

Bearbeitet in **TiaMcp Passwortverwaltung → Zugriffsmodus → Claude-Brücke** (Brücke an/aus, Befehl, Befehlsvorlage,
Zeitlimit, erlaubte Werkzeuge, Autostart). Die Einstellungsdatei ist Version 2, wenn der Brückenteil gesetzt ist (sonst
Version 1); der Server liest beide Versionen und ignoriert `claudeBridge`.

```json
{ "version": 2, "accessMode": "read-write",
  "claudeBridge": { "enabled": false, "command": "claude",
    "arguments": ["-p", "--output-format", "json", "--tools", "", "--allowedTools", "{allowedTools}", "--permission-prompts", "none"],
    "timeoutMinutes": 20, "allowedTools": ["mcp__tia-portal__bind_project", "...", "mcp__tia-portal__vci_write"] } }
```

Platzhalter in `arguments`: `{allowedTools}` (die Liste mit Kommas verbunden), `{prompt}` (dann ist der Prompt ein Argument
statt Standardeingabe), `{requestId}`, `{inboxRoot}`. Standard: aus; die Freigabeliste enthält die 32 Werkzeuge dieses
Servers als `mcp__tia-portal__<werkzeug>` und **kein eingebautes Claude-Code-Werkzeug** (`--tools ""` entfernt sie: keine
Shell, keine Dateibearbeitung, kein Web). Die Schalter sind die, die `claude --help` von Claude Code **2.1.317** auflistet
(`-p/--print`, `--output-format json`, `--tools`, `--allowedTools`, `--permission-prompts none`: „alles, was nachfragen
würde, wird automatisch abgelehnt“); auf dem PC sind sie **nicht geprüft** – dort `claude --help` ansehen und die Vorlage in
der Passwortverwaltung anpassen.

Autostart: Das Kästchen schreibt `TiaMcp Claude-Bruecke.cmd` (`start "" /min "<Brücke>"`) in den Autostart-Ordner des
Benutzers (eine `.cmd`, keine `.lnk`: K44) und entfernt sie wieder, wenn es abgewählt wird.

## Einmalige Einrichtung am Engineering-PC

1. **Add-In installieren** aus einem Quellstand (PowerShell, .NET SDK 10, TIA Portal V21 installiert):
   `scripts\install-claude-in-tia.ps1` (für die Brücke zusätzlich `-InstallBridge -BridgeAutostart`). Das Skript übersetzt
   `TiaMcpServer.AddIn` gegen die installierten V21-Assemblies, prüft `AddInPublisherConfig.xml` gegen die installierte
   `Siemens.Engineering.AddIn.Publisher.xsd`, ruft `Siemens.Engineering.AddIn.Publisher.exe --configuration …
   --outfile ClaudeInTia.addin --logfile … --verbose` auf und kopiert die `.addin` nach
   `%APPDATA%\Siemens\Automation\Portal V21\UserAddIns` (Ordnername ungeprüft; die Task Card kann die Datei auch von
   anderswo hinzufügen). Von Hand: mit `/p:UseTiaPortalReferenceStubs=false /p:TiaAddInReferenceDir=<Ordner mit
   Siemens.Engineering.Base.dll und Siemens.Engineering.AddIn(.Base).dll>` übersetzen, dann den Publisher mit der
   Konfiguration neben der DLL aufrufen.
2. **Aktivieren** in TIA Portal: Task Card **Add-Ins** → „Claude in TIA“ → aktivieren; TIA Portal zeigt die angeforderten
   Berechtigungen (TIA-Lesezugriff, Dateizugriff) und fragt nach Bestätigung.
3. **MCP-Server in Claude**: Claude Desktop wie in der [Installationsanleitung](../guides/installation.de.md); für die Brücke
   zusätzlich Claude Code: `claude mcp add -s user tia-portal -- "<Pfad>\TiaMcpServer.exe"` (Benutzerbereich, weil die Brücke
   in einem eigenen Arbeitsordner läuft; die Argumente des Servers hinter den Pfad), dann einmal bei Claude Code anmelden
   (`claude`).
4. **Brücke** (optional): TiaMcp Passwortverwaltung → Zugriffsmodus → Claude-Brücke → „Brücke an“, die Befehlsvorlage mit
   `claude --help` abgleichen, speichern; Autostart ankreuzen oder `TiaMcpClaudeBridge.exe` einmal starten.

Der Windows-CI-Workflow lädt das gegen die Stubs übersetzte Add-In (`claude-in-tia-addin-<sha>`) mit der
Publisher-Konfiguration und dem Installationsskript hoch. Diese DLL ist nur ein Kompiliernachweis: Installiert wird die, die
das Skript gegen das installierte TIA Portal übersetzt (der Publisher braucht TIA Portal und läuft nicht in der CI).

## Herkunft jedes verwendeten Siemens-Members

Quellen: **(X)** die V21-IntelliSense-Datei `Siemens.Engineering.AddIn.Base.xml` (Namen, Namensräume, Parametertypen; keine
Rückgabetypen); **(H)** die V21-Online-Hilfe (docs.tia.siemens.cloud, it-it/v21: „Programmazione del View Provider“,
„Programmazione dei menu di scelta rapida“, „API Messagebox“, „API di feedback“, „File di configurazione“, „Generazione
dell'Add-In dal file DLL“); **(B)** `Siemens.Engineering.Base.xml`. Status: **übersetzt** (im Stub
`reference-stubs/Siemens.Engineering.AddIn.Base` deklariert), **angenommen** (die übersetzte Signatur enthält ein Detail,
das weder X noch H zeigt; der erste Build gegen die echten DLLs prüft es), **Laufzeit** (am Live-Objekt per Name gebunden,
kein Stub-Member), **vorhanden** (bereits im Base-Stub).

Stub-Typen (jeder festgehalten durch `ClaudeInTiaAddInContractTests`): `MenuItem`, `ContextMenuAddIn`, `ContextMenuAddInRoot`,
`ChildItemFactory`, `ActionItem`, `ActionItem<T>`, `MenuSelectionProvider`, `MenuSelectionProvider<T>`,
`ProjectTreeAddInProvider`, `DevicesAndNetworksAddInProvider`, `ProjectLibraryTreeAddInProvider`,
`GlobalLibraryTreeAddInProvider`, `MessageBoxProvider`, `FeedbackProvider`.

| Siemens-Member | Quelle | Status |
| --- | --- | --- |
| `ContextMenuAddIn` `.ctor(string)`, `BuildContextMenuItems(ContextMenuAddInRoot)` (protected, überschrieben) | X, H | übersetzt; abstract/protected angenommen |
| `ContextMenuAddInRoot.Items` → `ChildItemFactory` | X (Eigenschaft), H (`addInRootSubmenu.Items.AddActionItem`) | übersetzt; Typ angenommen |
| `ChildItemFactory.AddActionItem<T>(string, ActionItem<T>.OnClickDelegate)` | X, H | übersetzt; Rückgabetyp `void` angenommen (unbenutzt) |
| `ActionItem`, `ActionItem<T>`, `ActionItem<T>.OnClickDelegate` = `void (MenuSelectionProvider<T>)` | X (Typen), H (`Entry1_OnClick(MenuSelectionProvider<IEngineeringObject>)`) | übersetzt; Delegatsignatur aus H |
| `MenuItem` (Basis von `ActionItem`) | X | übersetzt; Vererbung angenommen |
| `MenuSelectionProvider`, `MenuSelectionProvider<T>` | X | übersetzt (ohne Member) |
| `MenuSelectionProvider.GetSelection()` | X (ohne Rückgabetyp) | Laufzeit: das parameterlose, nicht generische `GetSelection`, Ergebnis als `IEngineeringObject` durchlaufen |
| `ProjectTreeAddInProvider`, `DevicesAndNetworksAddInProvider`, `ProjectLibraryTreeAddInProvider`, `GlobalLibraryTreeAddInProvider`: `.ctor()`, `GetContextMenuAddIns()` → `IEnumerable<ContextMenuAddIn>` | X, H (`protected override IEnumerable<ContextMenuAddIn> GetContextMenuAddIns()`) | übersetzt; die abgeleiteten Provider erhalten `TiaPortal` im Konstruktor (angenommen: TIA Portal übergibt es; H zeigt `TiaPortal`, das an ein Kontextmenü weitergereicht wird) |
| `MessageBoxProvider` (ein `IEngineeringService`) über `TiaPortal.GetService<MessageBoxProvider>()` | X, H (`m_TiaPortal.GetService<MessageBoxProvider>`), B (`TiaPortal.GetService``1`) | übersetzt; `IEngineeringService` aus der `GetService`-Einschränkung angenommen |
| `MessageBoxProvider.ShowNotification(NotificationIcon, string, string, string)` | X, H (liefert void) | Laufzeit (Enum-Member per Name: `Information`, `Success`, `Warning`, `Error`, alle in B) |
| `FeedbackProvider` über `TiaPortal.GetService<FeedbackProvider>()`, `FeedbackProvider.Log(NotificationIcon, string)` | X, H | Typ übersetzt; `Log` Laufzeit (Rückfall ohne Meldungsfenster) |
| `IEngineeringObject.GetAttribute("Name")`, `IEngineeringInstance.Parent`, `ProjectBase.Path`, `TiaPortal.Projects` | B | vorhanden |
| `GlobalLibrary.Path` | B (`P:...GlobalLibrary.Path`, Typ nicht gezeigt) | Laufzeit (Eigenschaft `Path`, `FileSystemInfo` oder `string`) |
| Typnamen als Text verglichen (`...SW.Blocks.PlcBlock`, `...PlcBlockUserGroup`, `...SW.PlcSoftware`, `...SW.Units.PlcUnit`, `...SW.Types.PlcType`, `...SW.Tags.PlcTagTable`, `...HW.Device`, `...HW.DeviceItem`, `...Library.Types.LibraryType`, `...Library.MasterCopies.MasterCopy`, …) | B, Step7-XML (jeder Name ist ein dokumentierter `T:`-Eintrag) | Laufzeit-Typnamen (`GetType()` und Basistypen), kein Member-Aufruf |

Nicht verwendet, obwohl dokumentiert: die Status-Delegaten von `ActionItem` (Signatur nicht gezeigt), Symbole
(`AddActionItemWithIcon`), Untermenüs, Kontrollkästchen-/Optionsfeld-Einträge, `ProgressProvider`,
`Siemens.Engineering.AddIn.Utilities.Process` und `ProcessStartPermission` (das Add-In startet keinen Prozess: die Brücke ist
ein eigenes Programm).

## Berechtigungen

`AddInPublisherConfig.xml` fordert `TIA.ReadOnly` an (das Add-In liest nur Namen und Eltern der markierten Objekte) und
`System.Security.Permissions.FileIOPermission` für Ein- und Ausgang. Die Schreibweise `FileIOPermission-UNRESTRICTED` ist
eine Annahme (H nennt die Berechtigungsklasse, das Schema lag nicht vor); eine auf Pfade beschränkte Dateiberechtigung wäre
kleiner, ihre XML-Form ist aber unbekannt. Der Code berührt nur `%APPDATA%\TiaMcpServer\inbox` und `...\outbox`
(festgehalten durch `ClaudeInTiaAddInContractTests`). Keine `UnrestrictedPermissions`, kein Prozessstart, kein Netzwerk.

## Ungeprüft

- Datei und Identität der Add-In-API-Assembly: Die XML nennt `Siemens.Engineering.AddIn.Base`, die Hilfe
  `Siemens.Engineering.AddIn.dll`; das Projekt nimmt die vorhandene; der Stub nimmt Version 21.0.0.0 und das
  Openness-Public-Key-Token an. Die Namensräume folgen der XML (`Siemens.Engineering.AddIn`, `.Menu`); die Hilfe zeigt
  `Siemens.Engineering.AddIn.Base(.Menu)`.
- Die Provider-Konstruktoren mit `TiaPortal`, die angenommenen Signaturen der Herkunftstabelle und ob die Add-In-Sandbox
  die Laufzeitbindung per Reflection erlaubt (nur öffentliche Member).
- `AddInPublisherConfig.xml`: Namensraum `http://www.siemens.com/automation/Openness/AddIn/Publisher/V21` (die Hilfe zeigt
  `.../Vxx` in zwei Schreibweisen), die Schreibweise des Berechtigungselements und ob `FeedbackProvider`/`MessageBoxProvider`
  weitere Berechtigungen brauchen.
- Der Benutzer-Add-In-Ordner `%APPDATA%\Siemens\Automation\Portal V21\UserAddIns`.
- Claude Code: auf dem PC installiert und angemeldet, die Schalter der Standardvorlage, die JSON-Felder `result`/`is_error`,
  dass `--tools ""` jedes eingebaute Werkzeug entfernt und `--permission-prompts none` ablehnt, was die Freigabeliste nicht
  abdeckt, und dass `mcp__tia-portal__<werkzeug>` zum in `claude mcp add` gewählten Servernamen passt.

## Ausgelassen

| Punkt | Grund |
| --- | --- |
| Freitexteingabe in TIA Portal | V21 dokumentiert keinen Eingabedialog für Add-Ins; ein WinForms-Dialog in der Sandbox ist nicht dokumentiert |
| Claude reagiert ohne Brücke von selbst | MCP hat keinen Server-an-Client-Push, der ein Gespräch startet |
| Bausteininhalt in die Anfrage kopieren | Absicht: Die Anfrage trägt nur Namen; Claude liest Inhalte über die Werkzeuge unter dem Zugriffsmodus |
| Signieren der `.addin` | braucht das Zertifikat des Inhabers (`Certificates` in der Konfiguration, `--certificatepassword`); ein unsigniertes Add-In zeigt die Warnung von TIA Portal |

## Live-Abnahmeprüfungen

1. `install-claude-in-tia.ps1` übersetzt gegen die echten V21-Assemblies (jeder Übersetzungsfehler ist eine angenommene
   Signatur) und die Konfiguration besteht die Prüfung gegen die installierte XSD; der Publisher erzeugt `ClaudeInTia.addin`.
2. TIA Portal listet und aktiviert das Add-In; das Menü **Claude** erscheint an allen vier Stellen.
3. „Prüfen“ auf einem Baustein schreibt eine Anfrage, deren `blockPath` `plc_read` annimmt; „Prüfen“ auf einem Gerät,
   einem Bibliothekstyp und einer Kopiervorlage liefert brauchbare `deviceName`/`libraryTypePath`/`masterCopyName`.
4. `tia_inbox list_requests` zeigt sie; `reply_request` antwortet; „Claude-Antworten anzeigen“ zeigt die Antwort im
   Meldungsfenster, ein zweiter Klick meldet „Keine neuen Antworten“.
5. Brücke an: Eine neue Anfrage startet `claude -p` genau einmal (Protokoll, Status `running` → `finished`), die Antwort
   kommt an; ein Neustart der Brücke führt sie nicht erneut aus; ist Claude Code nicht angemeldet, sagt die Antwort warum
   (`failed`).
