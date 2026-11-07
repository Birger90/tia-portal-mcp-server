# Installation

**Sprache:** [English](installation.md) · Deutsch

Wie der TIA-Portal-MCP-Server installiert, überprüft und bei einem MCP-Client registriert wird.
Einen kompakten Schnellstart enthält die [README](../../README.de.md).

## Voraussetzungen

- Windows
- Siemens TIA Portal V21 installiert
- TIA Portal Openness installiert und aktiviert
- Der aktuelle Windows-Benutzer ist Mitglied der Benutzergruppe `Siemens TIA Openness`
- Stabiles .NET 10 SDK (`10.0.400` oder neuer in den .NET-10-Feature-Bands) für `dotnet tool install`
- .NET Framework 4.8 Runtime für den Openness-Worker

Builds aus dem Quellcode benötigen zusätzlich:

- Stabiles .NET SDK 10.0.400 oder neuer in den .NET-10-Feature-Bands. Die `global.json` des Repos
  verlangt 10.0.400, rollt über die stabilen .NET-10-Feature-Bands vorwärts und schließt Prerelease-SDKs aus.
- .NET Framework 4.8 Developer Pack oder Targeting Pack

Standardmäßig erwarten Builds aus dem Quellcode die Openness-DLLs hier:

```text
C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48
```

Lokale Entwickler-Builds bevorzugen echte TIA-Portal-V21-Assemblies aus `TiaPortalV21Dir`. Du kannst diesen Pfad mit der MSBuild-Eigenschaft oder Umgebungsvariablen `TiaPortalV21Dir` überschreiben. Er muss auf den Ordner zeigen, der `Siemens.Engineering.Base.dll` und `Siemens.Engineering.Step7.dll` enthält.

Das Repo enthält im Repo gepflegte, generierte Referenz-Stubs für die Übersetzungszeit in `ref/`, deklariert unter `reference-stubs/`, damit CI den MCP-Server ohne installiertes TIA Portal bauen und paketieren kann. Sie bewahren die V21-Assembly-Identität mit einem rein öffentlichen Schlüssel und sind niemals Ersatz zur Laufzeit. Siehe [Aus dem Quellcode bauen](../development/building.md#generated-openness-references) für die Standardverifikation und die explizite Neuerzeugung der Artefakte. Builds mit Stubs und mit installierten Referenzen sind Belege für die Übersetzbarkeit; das Live-Verhalten von TIA erfordert eine eigene Abnahme. Diese Stubs werden nur als Rückfallebene verwendet, wenn keine lokale TIA-Installation gefunden wird. Um Stub-Referenzen für CI-/Paket-Builds zu erzwingen:

```powershell
dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true
```

Um lokale TIA-Referenzen zu erzwingen:

```powershell
dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=false /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
```

Während des Builds gibt der Worker das gewählte Referenzverzeichnis aus:

```text
TIA Openness compile references: C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48 (UseTiaPortalReferenceStubs=false)
```

Lokale Sitzungen unterstützen die Übernahme eines exakt beobachteten `.amc21`, Startzusicherungen für bereits geöffnete Projekte und
einen Basisstatus; ein schreibfähiges `open_project` akzeptiert eine exakte, existierende `.als21`-Datei. Lokaler Inhalt,
Übersetzen, Speichern, Schließen/Verwerfen/Einchecken und Änderungen am Server bleiben zurückgestellt. Die
[Multiuser-Referenz](../SupportedOperations/MULTIUSER_OPERATIONS_SUMMARY.de.md) dokumentiert eine eingegrenzte
Live-Abnahme: Das tatsächliche Öffnen offline in read-write erforderte in beiden getesteten Fixtures das Wegklicken
von Siemens-Dialogen; eine nicht interaktive bzw. vollständig offline funktionierende Unterstützung ist durch diesen Lauf nicht belegt.

## Installieren

```powershell
dotnet tool install -g TiaMcpServer
```

Den installierten Server ausführen:

```powershell
tia-mcp
```

Um einen MCP-Serverprozess an ein bestimmtes Projekt zu binden, `--project` übergeben oder `TIA_MCP_PROJECT_PATH` setzen:

```powershell
tia-mcp --project C:\Projects\Line.ap21
$env:TIA_MCP_PROJECT_PATH = 'C:\Projects\Line.ap21'
tia-mcp
# Synthetic example: assert the engineering path of an already-open local session
tia-mcp --project 'C:\Disposable\A\Engineering\A.amc21'
```

Der Server verweigert den Start (Exit-Code 1), wenn der Wert von `--project` oder `TIA_MCP_PROJECT_PATH` kein
absoluter `.ap21`- oder `.amc21`-Pfad ist.

`--project` beginnt als konfigurierte, aber unverifizierte Zusicherung. Vor jeder Vorschau eines abgesicherten Schreibvorgangs führt der
Host eine nur lesende Statusprüfung durch und akzeptiert die Bindung nur, wenn der Worker eine vollständige,
übereinstimmende Identität meldet: Worker-Prozess, PID von TIA Portal, Projektgeneration und kanonischer Projektpfad.
`bind_project` übernimmt explizit ein bereits geöffnetes Projekt; `open_project` öffnet eines, und
`create_project` legt eines an. Nur open/create können ein Projekt öffnen. Gewöhnliche ungebundene Lesevorgänge
binden die Sitzung nicht stillschweigend, und keine Anfrage öffnet implizit ein Projekt.

Der Worker wählt nie das erste laufende Portal oder das erste geöffnete Projekt. Er wählt einen exakten
Projektpfad, wenn einer angegeben wurde, oder einen einzigen Kandidaten, wenn es tatsächlich nur einen gibt. Mehrere mögliche
Portale/Projekte schlagen vor Attach oder Änderung mit `target_ambiguous` fehl. Eine spätere Abweichung von Pfad, PID, Worker oder
Projektgeneration schlägt mit `binding_conflict` fehl und invalidiert die Bindung; rufe
`bind_project` mit `forceRebind=true` für ein bereits geöffnetes Projekt auf oder starte eine neue MCP-Sitzung. `get_project_status`
ist nur lesend, nimmt keine Argumente und bindet nicht: Verwende es nicht, um Projekte zu wechseln. Inhaltswerkzeuge wirken nur auf das gebundene Projekt; ohne verifizierte Bindung (ein konfiguriertes `--project` wird zuerst durch den Status verankert) schlagen sie geschlossen mit `binding_conflict` fehl; rufe zuerst `bind_project` auf. Verwende `bind_project`, um ein bereits geöffnetes Projekt zu übernehmen oder
zu ihm zu wechseln; verwende `open_project` nur, um eines zu öffnen.

Rufe `bind_project` ohne Pfad auf, um das einzige geöffnete Projekt zu übernehmen oder die Kandidatenliste zu erhalten;
bei mehreren geöffneten Projekten gib einen angebotenen absoluten `.ap21`- oder Typed-Owner-`.amc21`-Pfad
und optional eine exakte `portalProcessId` an. `.als21` ist ausschließlich eine explizite Eingabe zum schreibfähigen Öffnen;
ein Inventarverzeichnis ist weder ein Öffner noch ein Bindungsselektor. Ein anderer konfigurierter,
verifizierter oder zuletzt gebundener Pfad erfordert `forceRebind:true`. Das erneute Anhängen kann den
Openness-Zugriffsdialog von TIA anzeigen, den ein Mensch beantworten muss. Die Worker-Besitzerschaft wird über ein Detach nicht übertragen; nach dem
erneuten Anhängen wird das Projekt als UI-eigen behandelt. Das Binden selbst öffnet, erstellt, speichert oder schließt nie.

### Versions-Flag

Führe `tia-mcp --version` (oder `tia-mcp -v`) aus, um die Host-Version auszugeben und zu beenden, ohne den MCP-Server zu starten.

### Doctor-Befehl

Führe `tia-mcp doctor` aus, um die Laufzeitumgebung zu prüfen, bevor du den MCP-Server verwendest. Er prüft das Betriebssystem, die .NET-Runtimes, die TIA-Portal-Installation, die Openness-Assemblies, die Mitgliedschaft in der Benutzergruppe, die ausführbare Worker-Datei, die Versionskompatibilität von Host und Worker, laufende TIA-Portal-Prozesse und die Konfiguration der Projektbindung.

Doctor greift nicht ein: Er startet nicht den MCP-Host, hängt sich nicht an TIA Portal an, öffnet kein Projekt und untersucht keinen Projektinhalt. Bei einer expliziten Bindung prüft er, dass der Wert ein absoluter Pfad zu einer existierenden `.ap21`-Datei ist. Die Prozesserkennung verwendet die Windows-Prozessliste, daher kann Doctor bei mehreren TIA-Portal-Prozessen nicht beweisen, welcher Prozess diese Datei geöffnet hat; er meldet diese Unsicherheit, statt stillschweigend zu bestehen.

```powershell
tia-mcp doctor
tia-mcp doctor --json
tia-mcp doctor --verbose
tia-mcp doctor --project C:\Projects\Line.ap21
```

Optionen:

- `--json` - ein einzelnes JSON-Dokument auf stdout ausgeben.
- `--verbose` - diagnostische Belege für jede Prüfung einschließen.
- `--project` - den exakten absoluten Pfad eines existierenden `.ap21`-Projekts prüfen, ohne TIA Portal zu öffnen oder sich daran anzuhängen.
- `--plc-webapi-config` - eine PLC-Web-API-Zieldatei parsen (Standard: `TIA_MCP_PLC_WEBAPI_CONFIG`) und ihre Ziele auflisten, wobei je Ziel nur gemeldet wird, ob seine Credential-Variablen gesetzt sind. Doctor kontaktiert nie eine PLC; eine ungültige Datei schlägt fehl, weil der Server mit ihr den Start verweigert.

Die Diagnose der Projektauswahl berücksichtigt den Zugriffsmodus:

- keine Projektbindung ist in jedem Modus eine Warnung (Warning), mit einer Abhilfe, die `bind_project` nennt;
- ein ungültiger, relativer, nicht-`.ap21`- oder fehlender Projektpfad ist ein Fehler;
- mehrere TIA-Portal-Prozesse sind eine Warnung, wenn eine explizite Bindung konfiguriert ist, weil Doctor die Live-Übereinstimmung nicht ohne Anhängen verifizieren kann;
- mehrere TIA-Portal-Prozesse ohne Bindung sind in jedem Modus eine Warnung (Warning); wähle das gewünschte geöffnete Projekt mit `bind_project`.

Selbst ein existierender lokaler Projektpfad bleibt eine Doctor-Warnung, weil Doctor sich absichtlich nicht
anhängt (Attach) und nicht beweisen kann, welches Portal ihn geöffnet hat. Bevor du Projektwerkzeuge verwendest, öffne genau dieses Projekt
im vorgesehenen TIA-Portal-Prozess. Die Identitätsprüfungen zur Laufzeit bleiben maßgeblich dafür, ob eine Anfrage angenommen oder
abgelehnt wird.

Die Datei-Bindungsdiagnose von Doctor akzeptiert weiterhin nur existierende `.ap21`-Dateien. Die Laufzeit unterstützt
Zusicherungen für bereits geöffnete `.amc21`, aber Doctor validiert sie nicht und hängt sich nicht an, um einen lokalen
Besitzer zu beweisen. Untersuche das Laufzeitergebnis von `bind_project`/Status auf die Identität der lokalen Sitzung.

Exit-Codes: `0` (keine blockierenden Fehler), `1` (eine oder mehrere Prüfungen fehlgeschlagen), `2` (ungültige Argumente).

### Bei einem MCP-Client registrieren

Der Befehl `tia-mcp install` registriert den TIA-Portal-MCP-Server bei einem unterstützten MCP-Client, indem er die native CLI des Clients aufruft. Er bearbeitet Konfigurationsdateien nicht direkt.

Unterstützte Clients: Claude Code, Codex, OpenCode, MiMoCode.

```powershell
tia-mcp install claude-code
tia-mcp install codex
tia-mcp install opencode
tia-mcp install mimocode
```

Aliasse: `claude` (Claude Code), `mimo` (MiMoCode).

Optionen:

- `--name <name>` - Registrierungsname des Servers (Standard: `tia-portal`).
- `--access-mode <mode>` - Zugriffsmodus: `read-only`, `read-write` oder `full` (Standard: `read-only`).
- `--tia-project <path>` - an ein bestimmtes TIA-Portal-Projekt binden; muss ein absoluter `.ap21`- oder `.amc21`-Pfad sein.
- `--server-path <path>` - expliziter Pfad zur ausführbaren Datei `tia-mcp`.
- `--dry-run` - den Installationsbefehl ausgeben, ohne ihn auszuführen.
- `--json` - JSON-Ausgabe erzeugen (mit MiMoCode nicht unterstützt).

Beispiele:

```powershell
# Register with Codex using read-write mode
tia-mcp install codex --access-mode read-write

# Register with Claude Code bound to a specific project
tia-mcp install claude-code --tia-project C:\Projects\Line.ap21

# Preview the install command without running it
tia-mcp install codex --dry-run

# JSON output for automation
tia-mcp install codex --json
```

MiMoCode verwendet den interaktiven Modus und fragt die Werte ab. Das Flag `--json` wird mit MiMoCode nicht unterstützt.

Exit-Codes: `0` (Erfolg), `1` (allgemeiner Fehler), `2` (ungültige Argumente), `3` (nicht unterstützter Client), `4` (Client nicht gefunden), `5` (ausführbare Datei tia-mcp nicht gefunden), `6` (nativer Befehl fehlgeschlagen), `7` (Verifikation fehlgeschlagen), `8` (nicht unterstützte Optionskombination).

### Zugriffsmodi

Der Server unterstützt drei Zugriffsmodi, durchgesetzt bei der Discovery, beim Host-Dispatch und beim Worker-Dispatch:

- **read-only** - dreizehn Werkzeuge: elf Beobachtungswerkzeuge (einschließlich `plcsim_read`, `plc_online_read`, `library_read` und `siemens_docs`, der Leser der Siemens-Dokumentation, der kein TIA Portal braucht), `tia_inbox` („Claude in TIA“) plus `bind_project`; kein
  Übersetzen, keine Änderungen, keine Simulationssteuerung, kein Laden, keine Online-Änderungen, kein Online-Gehen, kein Station-Upload und kein Lifecycle. `plc_read`
  `compare_to_online` (ein Openness-Online-Vergleich, der nichts ändert) und `scan_accessible_devices` (Live-Netzwerkerkennung
  über eine explizit benannte PG/PC-Schnittstelle, ändert nichts) sind Beobachtung und funktionieren in jedem Modus.
- **read-write** (Standard beim Serverstart) - zweiunddreißig Werkzeuge: die Read-only-Oberfläche plus
  `compile_check`, `network_write`, `plc_write`, `hmi_write`, `vci_write`, `library_write`, `plcsim_write`, `plcsim_test`, alle sieben Lifecycle-Werkzeuge, `plc_download`
  (in PLCSIM oder in echte Hardware), `plc_online_write` (konfigurierte S7-1500-CPUs), `plc_tia_online` (TIA Portal online/offline gehen)
  und `plc_station_upload`. Jeder tatsächliche Lifecycle-, Lade-, Online-Schreib-, Online-Verbindungs- oder Upload-Aufruf und jeder
  `library_write`-Aufruf, der eine globale Bibliothek öffnet, dearchiviert, anlegt, speichert oder schließt, erfordert ein Bestätigungsformular; die Rückfrage zum Laden nennt, wenn
  das Ziel eine echte Steuerung ist. Die übrigen Schreibaufrufe der Domänen (einschließlich Bibliotheks-Inhaltselementen) und die PLCSIM-Werkzeuge fragen nie nach.
- **full** - dieselben zweiunddreißig Werkzeuge und Operationen; Lifecycle, Laden, Online-Schreibvorgänge, Online-Verbindungen, Uploads und der Lifecycle globaler Bibliotheken laufen ohne
  Server-Elicitation, sodass die KI ohne Nachfrage in eine echte Steuerung laden (und deren Download-Konfigurationen beantworten) oder eine konfigurierte CPU
  online ändern kann. Unbekannte Operationen bleiben verweigert.

Der Zugriffsmodus ist die Kontrolle über Laden und Online-Schreibvorgänge: Es gibt keine separate Hardware-Sperre. Starte in
`read-only`, um sie zu verbieten, in `read-write`, um jeden Aufruf zu genehmigen, und in `full` nur, wenn die KI unbeaufsichtigt
laden oder PLCs ändern darf. Passwörter, die ein Download braucht (zum Beispiel ein Schreibschutz-Passwort der CPU),
werden als Umgebungsvariablen in der Client-Konfiguration (`env`) übergeben, nie in einem Werkzeugaufruf; siehe
[MCP-Client-Konfiguration](mcp-client-configuration.md#plc-download-passwords). Dasselbe gilt für die
Web-API-Anmeldedaten von `plc_online_*`-Zielen ([PLC-Web-API-Ziele](mcp-client-configuration.md#plc-web-api-targets))
und für die Online- und Upload-Passwörter von `plc_tia_online` und `plc_station_upload`.

**Migration:** Read-write-Clients können mit Bestätigung speichern, schließen und alle Lifecycle-Werkzeuge verwenden.
Wähle `--access-mode full` für Lifecycle ohne Server-Elicitation. Das Openness-PLC-Run/Stop
(`start_plc`/`stop_plc`) und die generischen Batch-Werkzeuge (`preview_write_batch`, `apply_write_batch`)
wurden entfernt; PLC-Schreibvorgänge verwenden `plc_write`, und der Betriebszustand einer konfigurierten echten CPU wird mit
`plc_online_write change_operating_mode` über die S7-1500 Web API geändert (siehe
[PLC-Web-API-Ziele](mcp-client-configuration.md#plc-web-api-targets)).
Der Installationsbefehl verwendet weiterhin standardmäßig
read-only. Um die Registrierung in der Vorschau anzuzeigen, ohne die Client-Konfiguration zu ändern:

```powershell
tia-mcp install codex --access-mode full --dry-run
```

`--project` konfiguriert eine Zusicherung; Inhaltswerkzeuge nehmen kein `projectPath` und öffnen oder wechseln Projekte in
keinem Modus. Binde ein bereits geöffnetes Projekt explizit mit `bind_project`, bevor du schreibende Operationen ausführst.

Read-only-Modus aktivieren:

```powershell
tia-mcp --access-mode read-only
tia-mcp --read-only
$env:TIA_MCP_ACCESS_MODE = 'read-only'
tia-mcp
```

Vorrang der Konfiguration: CLI-Argument (`--access-mode`, `--read-only`, `--read-write`) > Umgebungsvariable
`TIA_MCP_ACCESS_MODE` > Einstellungsdatei des Servers > Standard (read-write).

Der Modus wird einmal beim Start aufgelöst und kann während der Lebensdauer des Prozesses nicht geändert werden. Es gibt kein MCP-Werkzeug, das den Zugriffsmodus zur Laufzeit ändert.

### Zugriffsmodus in der Passwortverwaltung wählen

Statt `--access-mode` in der Client-Konfiguration kann der Besitzer den Modus im Windows-Programm
„TiaMcp Passwortverwaltung“ wählen (`tia-mcp passwords`, Reiter **Zugriffsmodus**): drei Optionsfelder („Nur lesen (read-only)“,
„Lesen und schreiben, mit Rückfrage (read-write)“, „Voll, ohne Rückfrage (full)“), der gespeicherte Wert (oder
„nicht gesetzt – Standard read-write“), **Speichern** und **Einstellung entfernen**. Es schreibt die Einstellungsdatei des Servers:

- Ort: `%APPDATA%\TiaMcpServer\settings.json` oder der Pfad in `TIA_MCP_SETTINGS` (im `env` des Clients setzen
  und das Programm mit `tia-mcp passwords --settings <same path>` starten).
- Inhalt (geschlossenes Schema, Version 1, nur Namen): `{ "version": 1, "accessMode": "read-only" }`; `accessMode` ist optional
  (weggelassen: nicht gesetzt) und genau `read-only`, `read-write` oder `full`. Unbekannte Member, eine andere Version, ein falsch
  geschriebener Modus, eine unlesbare Datei oder ein `TIA_MCP_SETTINGS`-Pfad, der nicht existiert, verhindern den Start mit einer Meldung,
  die die Datei nennt, selbst wenn ein CLI-Argument oder `TIA_MCP_ACCESS_MODE` den Modus bestimmt. Eine fehlende Standarddatei ist keine
  Einstellung.
- Der Server liest sie einmal beim Start und schreibt sie nie; das tut nur die Passwortverwaltung (über eine temporäre Datei im
  selben Ordner, nie über eine Datei, die der Server ablehnen würde). Kein MCP-Werkzeug liest oder ändert sie.
- Ein Argument `--access-mode` oder `TIA_MCP_ACCESS_MODE` in der Client-Konfiguration hat Vorrang, daher entferne sie, wenn
  das Programm entscheiden soll; `tia-mcp install` schreibt weiterhin `--access-mode` (Standard `read-only`). Eine Änderung wird wirksam,
  wenn der MCP-Server neu startet (Claude Desktop beenden und neu starten).
- Beim Start wird `TIA MCP access mode source: cli | environment (TIA_MCP_ACCESS_MODE) | settingsFile (<path>) | default`
  und `TIA MCP settings file: <path> | none` protokolliert; `tia-mcp doctor` gibt `Access mode: <MODE> (<source>)` und
  `Settings file: <path> | none` aus, und `doctor --json` ergänzt `accessModeSource` und `settingsFile` nach `accessMode`.

Mit der Einstellungsdatei braucht ein Eintrag in Claude Desktop überhaupt keine `args`:

```json
{
  "mcpServers": {
    "tia-portal": {
      "command": "tia-mcp"
    }
  }
}
```

Im Read-only-Modus stellt der Server genau acht MCP-Werkzeuge bereit:

- `bind_project` — ein bereits geöffnetes Projekt übernehmen oder zu ihm wechseln, ohne das Projekt zu ändern.
- `get_project_status` — Metadaten des aktiven Projekts lesen, ohne Projekte zu öffnen oder zu wechseln.
- `browse_project_tree` — einen kanonischen, seitenweisen v3-Snapshot mit `startSelector`, `depth` und `pageSize` durchsuchen; mit `cursor` fortsetzen.
- `plc_read` — Lesevorgänge für PLC-Bausteine, -Typen und -Variablentabellen in einem Batch ausführen.
- `read_cross_references` — die Querverweise eines Ziels im Projektbaum lesen.
- `network_read` — dedizierte Netzwerk-Lesevorgänge in einem Batch ausführen.
- `hmi_read` — nur lesende WinCC-Unified-HMI-Lesevorgänge in einem Batch ausführen.
- `vci_read` — Standalone-VCI-Workspaces, Mappings und unterstützte Formate untersuchen.

Die folgenden Operationen sind im Read-only-Modus **nicht verfügbar**:

- `compile_check` (ruft die Siemens-Übersetzungs-API auf)
- Alle Lifecycle-Operationen für Projekte (`open_project`, `create_project`, `save_project`, `save_project_as`, `archive_project`, `close_project`)
- Alle Datenänderungen (`plc_write`, `network_write`, `hmi_write` und `vci_write`)

Im Read-only-Modus arbeitet der Server nur auf einem bereits geöffneten Projekt. Er öffnet, erstellt, speichert oder schließt nie ein Projekt. Ein explizites `bind_project` kann die gewählte Sitzung wechseln; gewöhnliche Lesevorgänge können das nicht. Inhaltswerkzeuge wirken nur auf das gebundene Projekt und schlagen ungebunden geschlossen mit `binding_conflict` fehl. Cursor des Projektbaums lehnen Bindungsänderungen mit `cursor_binding_mismatch` ab, selbst nach dem Zurückwechseln auf den ursprünglichen Pfad.

Der Read-only-Modus ist eine Sicherheitsgrenze, die auf drei Ebenen durchgesetzt wird:

1. Werkzeuge für Projektänderungen, Engineering und Lifecycle werden in der MCP-Discovery nicht registriert; die Sitzungsauswahl bleibt verfügbar.
2. Die hostseitige `OperationAccessPolicy` lehnt verbotene Operationen ab, bevor der Worker-Prozess gestartet wird.
3. Die workerseitige `WorkerOperationAuthorization` lehnt verbotene Operationen unabhängig davon ab, selbst wenn eine rohe Worker-Anfrage den Host umgeht.

Beispiel für eine MCP-Client-Konfiguration:

```json
{
  "mcpServers": {
    "tia-portal-read-only": {
      "command": "tia-mcp",
      "args": ["--access-mode", "read-only"]
    }
  }
}
```

Der Befehl `tia-mcp doctor` meldet den aktiven Zugriffsmodus und seine Quelle (`cli`, `environment`, `settingsFile`,
`default`).

### Benutzerbestätigung je Zugriffsmodus

Der entfernte Startschalter schlägt mit genau dieser Migrationsmeldung fehl:

```text
--confirm-with-user was removed. Confirmation follows the access mode: read-write asks for every lifecycle call; use --access-mode full to run lifecycle tools without prompts.
```

Jeder tatsächliche Lifecycle-, `plc_download`- und `plc_online_write`-Aufruf in read-write erfordert ein Form-Elicitation-`accept` mit booleschem
`confirm:true`, auch bei Aufrufen ohne Guards oder nur mit Info-Guards. Fehlende Capability, Ablehnen,
Abbrechen, Timeout oder Transportfehler verweigern mit `access_denied`. Full verwendet die Bestätigung per Richtlinie (policy)
ohne Server-Elicitation. Block-Guards stoppen einen Aufruf in jedem Modus, und `dryRun:true` löst nie eine Elicitation aus.
Lifecycle akzeptiert `dryRun` mit seinen Operationseingaben; das frühere Bestätigungs-Array des Agenten ist entfernt.
Nach einer akzeptierten Elicitation löst der Server unter derselben Bindungs-Lease einen frischen Zustand auf, bevor er
dispatcht. Audit v2 erfasst die Bestätigung als `user`, `policy` oder `none`; die Erfüllung von Acknowledge-Guards
ist `user`, `policy` oder null. `plc_write` und `network_write` verwenden abgesicherte Schreibvorgänge ohne
Server-Elicitation: Vorschau mit `dryRun:true`; ein weggelassenes `dryRun` führt aus. Kein Werkzeug verwendet Safety-Tokens.

Die akzeptierte Antwort eines Elicitation-Clients beweist nicht, dass eine Person einen Dialog gesehen hat. Halte
destruktive Werkzeuge aus den Auto-Approve-Listen des Clients heraus, um bei jedem Aufruf Berechtigungsabfragen des Clients
zu erzwingen. Siehe die [Lifecycle-Referenz](../SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md#lifecycle-operations)
für Dry-Run-Beispiele, Lifecycle-/Local-Source-Guards und die gestufte Migration von Ein- und Ausgaben im Major-Release.

Das Paket enthält die Ordner `openness-worker` und `plcsim-worker` und die erforderlichen Nicht-Siemens-Abhängigkeiten sowie den MIT-lizenzierten S7-1500-Web-API-Client des Hosts, `Siemens.Simatic.S7.Webserver.API.dll`, von nuget.org. Es schließt `Siemens.Engineering*.dll` und jede S7-PLCSIM-Advanced-Assembly absichtlich aus; diese werden zur Laufzeit aus den lokalen Installationen von TIA Portal und (optional) S7-PLCSIM Advanced geladen. `tia-mcp doctor` meldet, woher die PLCSIM-Advanced-API geladen würde (`TIA_MCP_PLCSIM_API_DIR` oder die Registry), ohne sie zu laden.

### Optional: Claude in TIA (TIA-Portal-Add-In)

Das Add-In „Claude in TIA“ fügt in TIA Portal ein Kontextmenü **Claude** hinzu, das die markierten Objekte über das
MCP-Werkzeug `tia_inbox` an Claude übergibt; die optionale Claude-Brücke startet Claude ohne Chat. Nicht live getestet.
Einmalige Einrichtung: `scripts\install-claude-in-tia.ps1` aus einem Quellstand ausführen (für die Brücke zusätzlich
`-InstallBridge -BridgeAutostart`), das Add-In in der Task Card **Add-Ins** von TIA Portal aktivieren und für die Brücke
den Server bei Claude Code eintragen (`claude mcp add -s user tia-portal -- "<Pfad>\TiaMcpServer.exe"`) und die Brücke in
der TiaMcp Passwortverwaltung, Reiter **Zugriffsmodus**, einschalten. Einzelheiten, ungeprüfte Punkte und das
Anfrageformat: [Claude in TIA](../SupportedOperations/CLAUDE_IN_TIA.de.md).
