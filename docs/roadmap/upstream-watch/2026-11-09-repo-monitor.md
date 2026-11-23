# Upstream-Watch 2026-11-09

Wöchentlicher Bericht des Repo-Monitors für diesen Fork (Ziel: TIA Portal V21). Zeitraum: **2026-11-02 bis 2026-11-09**
(seit dem letzten Bericht [2026-11-02](2026-11-02.md)). Grundlage sind schreibgeschützte Klone der genannten Repositories
(als Daten behandelt, nichts gebaut oder ausgeführt, ihre `AGENTS.md`/`CLAUDE.md` ignoriert) und Websuche.

Alles, was andere Projekte über das Verhalten von TIA Portal berichten, ist **nicht verifiziert**, bis ein Live-Test auf V21
es bestätigt.

## Entwicklungslinie des Forks

- `main` steht auf `da50519` (2026-11-08). `feature/plcsim-download` ist 2 Commits hinter `main` und hat keinen eigenen
  Commit. Sync und Übernahmen bauen deshalb auf `main` auf.
- Die PRs der letzten Runde (#1 Sync, #2 Watch, #3 Adopt) sind gemergt. Upstream ist damit bis `6cc3e91` übernommen.

## Beobachtete Repositories

Lizenz jeweils in der Lizenzdatei des Repositorys geprüft.

| Repository | Lizenz | Neu seit 2026-11-02 | Stand |
| --- | --- | --- | --- |
| [Czarnak/tia-portal-mcp](https://github.com/Czarnak/tia-portal-mcp) (Upstream) | MIT | 128 Commits, 14 PRs (#210–#229), Release `v4.3.0` | `fd96c54` (2026-11-08) |
| [heilingbrunner/tiaportal-mcp](https://github.com/heilingbrunner/tiaportal-mcp) | MIT | 14 Commits, v0.4.0 | `4d7fe30` (2026-11-06) |
| [1748477582/tia-portal-openness-mcp](https://github.com/1748477582/tia-portal-openness-mcp) | MIT | nichts | `8e7ca42` (2026-10-29), `v2.6.3` |
| [rickgaiser/TiaMcp](https://github.com/rickgaiser/TiaMcp) | AGPL-3.0 | 12 Commits (PRs #16, #17, #19) | `9b84864` (2026-11-05) |
| [asckye/TIA_Portal_Openness_MCP](https://github.com/asckye/TIA_Portal_Openness_MCP) | MIT (`Copyright (c) 2026 bulaofen0036-coder`) | 181 Commits, `v4.0.0-beta.3` und `beta.4` | `4a0592e8` (2026-11-05) |
| [BetterTIA](https://github.com/BetterTIA) (watch, writer, reader, editor) | MIT | nichts; keine neuen Repositories | writer `fc8179c` (2026-10-17) |
| [a4webdev/tiacommander-mcp](https://github.com/a4webdev/tiacommander-mcp) | proprietäre EULA (nur Ideen) | nichts | `3016289` (2026-09-07) |
| [siemens/tia-portal-ai-extensions](https://github.com/siemens/tia-portal-ai-extensions) | MIT | 1 Commit (`.gitignore`) | `4fd79b9` (2026-11-02) |
| [siemens/simatic-s7-webserver-api](https://github.com/siemens/simatic-s7-webserver-api) | MIT | nichts; neuestes Release weiter `v3.3.92` (= im Fork) | `3f334fa` (2026-10-26) |
| [Czarnak/totally-integrated-claude](https://github.com/Czarnak/totally-integrated-claude) | MIT | nichts | `7b805e1` (2026-10-17) |
| [jessenic/TIA-MCP-Server](https://github.com/jessenic/TIA-MCP-Server) (seit letzter Runde beobachtet) | MIT | nichts | `1e3a8a6` (2026-10-27) |

## Was neu ist

### Czarnak/tia-portal-mcp (Upstream)

- **Refactorings #210–#217** (2026-11-02): Worker-Client in Teildateien aufgeteilt (`Binding`, `Invocation`, `Lifecycle`,
  `Network`, `Plc`, `Domains`), Worker-Dispatch für Netzwerk und PLC in `Program.NetworkDispatch.cs`/`Program.PlcDispatch.cs`,
  FakeWorker-Szenariofamilien in eigene Dateien, Legacy-Null-Omission entfernt, Session-Binding in den Host, gemeinsame
  Testinfrastruktur (`RepositoryFiles`, `CanonicalToolDocument`, `FakeWorkerClients`), Install-Parse-Exits, Host-Helfer.
  → übernommen (siehe Upstream-Sync).
- **`library_read`/`library_write`** (#220, #225, #226, 2026-11-06 bis 08): eigene Bibliothekswerkzeuge des Upstreams mit
  Live-Bericht. → **nicht übernommen**: Der Fork hat unter denselben Toolnamen eine eigene, größere Umsetzung
  (14 Lese-, 39 Schreiboperationen). Der Drei-Wege-Patch ergab Konflikte in 60 Dateien. Das ist eine Entscheidung für den
  Owner, siehe [Offene Entscheidungen](#offene-entscheidungen).
- **Read-only als Startstandard** (#227, 2026-11-08): Ohne Angabe startet Upstream jetzt nur mit den Lesewerkzeugen
  (Breaking Change). → **nicht übernommen**: Im Fork wählt der Owner den Modus in der Passwortverwaltung
  (`settings.json`); Standard ohne Datei ist read-write, der Installer setzt read-only. Die Quellenangabe des Modus
  (`accessModeSource`) hat der Fork schon.
- **GlobalDB in Software-Units** (#228, Issue #221): `MemoryLayout` wird nicht mehr ins erzeugte XML geschrieben; Upstream
  hat das auf V21 qualifiziert. → übernommen.
- **Import-Erfolg getrennt von der Übersetzung** (#229, Issue #221): Ein abgeschlossener, verifizierter Import scheitert
  nicht mehr an Übersetzungsfehlern; spätere Items laufen weiter. → übernommen.

### heilingbrunner/tiaportal-mcp (v0.4.0)

- **`ExportHardware`/`ImportHardware`** (`3a5bfa2`): Hardware des Geräts einer PLC-Software als AutomationML über
  `CaxProvider`. → im Fork **vorhanden** (`network_write export_cax`/`import_cax`, Gerät oder ganzes Projekt, Log-Datei,
  Hash-Prüfung). Gemeldeter Fallstrick: CAx-Import in Exclusive Access wird abgelehnt (A9 unten).
- **Format `code-blocks-LAD-STL`** (`3083d2b`): KOP/FUP mit AWL-Netzwerken wird als XML statt SD exportiert (A6 unten).
- `tia init`, verbindliche Kommandozeilen-Presets, `keep-structure`: Komfort der Git-Spiegelung (W2 aus der letzten Runde).

### rickgaiser/TiaMcp

- **Automatisches Wiederanbinden** (`38e63db`): Wenn TIA Portal das Projekt schließt und wieder öffnet (Multiuser-Check-in,
  anderer Openness-Client), bindet sich der Server vor jedem Tool neu an dasselbe Projekt; ein Lesen wird wiederholt, ein
  Schreiben nicht. → im Fork **nicht vorhanden**: Nach `portal_lost` muss `bind_project` aufgerufen werden (bewusst, keine
  Wiederholung). Siehe W13.
- **„Was hat sich seit dem letzten Lesen geändert“** (`d79168a`) im Quellbaum: Teil von W2.
- HMI-Alarmtexte im Roundtrip, interne HMI-Variablen, Fortschrittsfenster mit Abbrechen (`4849f44`), einheitliches `compile`.
- Fallstricke A6, A7, A8 unten.

### asckye/TIA_Portal_Openness_MCP (4.0.0-beta.3/beta.4)

- **Dienstsuche über mehrere Besitzer** (`93eeb016`, VMFIX-5b): `LoadableProvider`, Fingerprint- und Monitor-Dienste werden an
  der PLC-Software, an den CPU-Vorfahren und am Gerät gesucht. → **übernommen** für `generate_loadable` (siehe unten).
- **TO-Absturzschutz** (`93eeb016`): Auf V21 wird das Anlegen eines Technologieobjekts nur für TO_PositioningAxis 5.0,
  TO_SpeedAxis 5.0 und PID_Compact 2.3 erlaubt, weil ein anderes Paar TIA Portal beendet habe. → als **Info-Guard** übernommen
  (keine Ablehnung, Owner-Prinzip).
- **Rollback-Journal für Schreibvorgänge** (`1012d0b7`): rückgängig machen, was nachweisbar ist. → im Fork teilweise
  (`PlcRoundTrip`-Transaktionen und Recovery-Store bei Verschiebungen). Siehe W14.
- Vorschau aller Schreibwerkzeuge unter einem Plan-Hash, Freigabe-Oberfläche der Workbench, Relay für Remote-MCP: deren
  eigene Infrastruktur, für den Fork ohne Nutzen (der Fork hat `dryRun` und die Access-Modi).

### BetterTIA, 1748477582, tiacommander, totally-integrated-claude, Siemens-Repos, jessenic

Keine inhaltlichen Änderungen. Die Organisation BetterTIA hat keine neuen Repositories.

## Neue oder geänderte MCP-Server und KI-Integrationen (Websuche, GitHub-Suche)

| Projekt | Lizenz | Funktionen | Bewertung |
| --- | --- | --- | --- |
| [Sparkz400/TIA_MCP](https://github.com/Sparkz400/TIA_MCP) (neu, ab 2026-11-07) | Apache-2.0 | V21 und PLCSIM Advanced V8; .NET-10-Host mit net48-Workern (wie hier); `call_graph`, `change_impact`, `change_prepare`/`change_apply`/`change_restore` mit Checkpoint, `project_health_report`, `sim_test`, Project Explorer (Web-UI) | Architektur wie der Fork. Neu für den Fork: Checkpoint/Restore vor einer Änderung (W14) und Gesundheitsbericht (W15). Live-Qualifikation dort laut README unvollständig |
| [m1rk0z/mcp-server-tia-portal-v21](https://github.com/m1rk0z/mcp-server-tia-portal-v21) (2026-09 bis 10-29) | MIT | WinCC classic und Unified; Attach einmal pro Sitzung; `details:true` liest klassische Bild-/Variablenwerte aus einem Export | Im Fork vorhanden (klassische Objekte, Bildnummern-Fallback). Fallstrick A10 |
| [castillobenjamin/tia-portal-openness-mcpserver](https://github.com/castillobenjamin/tia-portal-openness-mcpserver) | **keine Lizenzdatei** | V20; SD- und XML-Austausch von Bausteinen und PLC-Datentypen mit Projektbaum-Spiegel, Software-Units, Unified-Werkzeuge | Ohne Lizenz nur Ideen. Nichts, was der Fork nicht schon hat (außer W2) |
| [renanlido/tia-harness](https://github.com/renanlido/tia-harness) | MIT (Plugin) | Claude-Code-Plugin, das einen kommerziellen „TIA Openness serve“ anspricht | Engine proprietär; nichts übernehmbar |
| [Wayan123/multi-plc-agent](https://github.com/Wayan123/multi-plc-agent) | Apache-2.0 | Ein MCP für viele Hersteller; Siemens-Teil TIA V20, S7-1200, KOP schreiben | Außerhalb des V21-Umfangs |
| [heilingbrunner/vscode-tiaportal-mcp](https://github.com/heilingbrunner/vscode-tiaportal-mcp) | MIT | VS-Code-Erweiterung für Copilot | Gleicher Autor wie tiaportal-mcp; nichts Eigenes für den Fork |
| [tia-forge/dsh-tiaforge](https://github.com/tia-forge/dsh-tiaforge) | MIT | DeepSeek-Harness-Plugin (Doku, KOP-Generierung, Simulation) | Lizenz von `tialink` nicht geprüfbar; nicht verifiziert |
| [bulaofen0036-coder/TIA_Portal_Openness_MCP](https://github.com/bulaofen0036-coder/TIA_Portal_Openness_MCP) | MIT | Quelle von asckye (gleicher Copyright-Inhaber) | siehe asckye |
| Siemens Industrial Copilot / Engineering Copilot | kommerziell | Websuche fand keine neue MCP-Schnittstelle | nicht verifiziert |

## Vergleich mit dem Fork

Priorität aus Sicht eines SPS-Programmierers. Nichts davon ist live auf V21 getestet. W1 bis W12 stehen im Bericht vom
2026-11-02; hier stehen nur neue oder geänderte Zeilen.

| # | Fähigkeit (gesehen in) | Status im Fork | Benötigte Member | Primärquelle | Lizenz | Priorität |
| --- | --- | --- | --- | --- | --- | --- |
| W13 | Neu anbinden nach Schließen und Wiederöffnen des Projekts (rickgaiser `38e63db`) | fehlt: nach `portal_lost` ist `bind_project` nötig | keine neuen (`TiaPortal.GetProcesses()`, `Attach`, `Projects`) | `totally-integrated-claude` `tia-project-general` | AGPL-3.0 | mittel |
| W14 | Checkpoint und Rücknahme vor einer Änderung (Sparkz400 `change_restore`, asckye Rollback-Journal) | teilweise: Transaktionen und Recovery-Store nur bei Verschiebungen | Export/Import vorhandener Wege | `tia-import-export/references/overview.md` | Apache-2.0 / MIT | mittel |
| W15 | Projekt-Gesundheitsbericht (Sparkz400, 1748477582) | teilweise: `find_unused_blocks`, `find_orphaned_instance_dbs` | keine neuen | – | Apache-2.0 / MIT | niedrig |
| W16 | `LoadableProvider` auch an CPU-Vorfahren und Gerät suchen (asckye `93eeb016`) | **war besser bei asckye → übernommen** | `IEngineeringServiceProvider.GetServiceInfos()`, `IEngineeringInstance.Parent` | `Siemens.Engineering.Step7.xml` (V21) nennt den Besitzer nicht | MIT (Idee) | mittel |
| W17 | Warnung vor TO-Anlage mit unbestätigtem Typ/Versions-Paar (asckye) | **fehlte → als Info-Guard übernommen** | keine neuen | – (Beobachtung von asckye) | MIT (Idee) | mittel |
| W2 | Git-basierter Teilimport geänderter Exportdateien (heilingbrunner, rickgaiser) | fehlt weiter | vorhandene Import-/Exportwege | `tia-import-export/references/overview.md` | MIT / AGPL-3.0 | hoch |
| W4 | Download-Bereitschaft mit Konsistenz- und Compile-Stand (1748477582, tiacommander) | fehlt weiter | `ICompilable.Compile`, `PlcBlock.IsConsistent`, `PlcChecksumProvider` | `tia-plc-operations/references/download-upload.md` | MIT / Idee | hoch |

## TIA Portal V21 Fallstricke

Alle **von anderen Repositories berichtet, nicht verifiziert**. Aufgenommen in
[V21_PITFALLS.md](../../SupportedOperations/V21_PITFALLS.de.md) als A5–A10 (Branch `adopt/2026-11-09`).

- **A5, von asckye berichtet, nicht verifiziert:** Ein TO-Anlegen mit einem anderen Paar als TO_PositioningAxis 5.0,
  TO_SpeedAxis 5.0 oder PID_Compact 2.3 hat TIA Portal V21 beendet und ungespeicherte Arbeit verloren.
- **A6, von heilingbrunner und rickgaiser berichtet, nicht verifiziert:** KOP/FUP-Bausteine mit AWL-Netzwerken lassen sich
  nicht als SIMATIC SD exportieren oder importieren; ein inkonsistenter solcher Baustein scheitert beim Export.
- **A7, von rickgaiser berichtet, nicht verifiziert:** Das Lesen von `SystemDiagnosisControlScriptBlock` der Skriptspalten
  einer Unified-Systemdiagnoseanzeige beendet TIA Portal V21.
- **A8, von rickgaiser berichtet, nicht verifiziert:** Gruppen von Unified-Bildobjekten gibt es in Openness V21 nicht;
  Positionen gruppierter Objekte sind relativ zur Gruppe.
- **A9, von heilingbrunner berichtet, nicht verifiziert:** Ein CAx-Import innerhalb von Exclusive Access wird abgelehnt.
- **A10, von m1rk0z berichtet, nicht verifiziert:** `GetAttributeInfos()` klassischer WinCC-Objekte liefert nur `Name`.
- **Von heilingbrunner berichtet, nicht verifiziert:** SIMATIC-SD-Dokumente brauchen ein in TIA Portal vorbereitetes Objekt
  („Tools > Prepare for Simatic SD“), was Openness nicht kann.

## Upstream-Sync (`upstream-sync/2026-11-09`)

- Basis `main` (`da50519`); übernommen Upstream `6cc3e91..0f35a77` (#210–#217) sowie #228 und #229, als Drei-Wege-Patch
  (die Historien haben keinen gemeinsamen Vorfahren).
- **Konflikte:** 8 Dateien in den Refactorings, alle aufgelöst. Die Fork-Änderungen an aufgeteilten Dateien sind in die neuen
  Teildateien übertragen (K37-Hinweis, `PlcDownloadOutcome`, `CreateIoSystem`, SD-Format für Bausteine, Route für den
  Main-Safety-Baustein, Compile-Scope, Variablenkommentare, `retrieve_project`, Fork-Szenarien im FakeWorker).
- **Ausgelassen:** #220/#225/#226 (Bibliotheken) und #227 (read-only als Standard), siehe oben.
- **Build:** `dotnet build TiaMcpServer.slnx -m:1 --configuration Release /p:UseTiaPortalReferenceStubs=true` erfolgreich
  (Linux, SDK 10.0.403).
- **Tests** (`Category!=Packaging`, Linux): Basis 11.495 Tests, 2.235 fehlgeschlagen (plattformbedingt:
  `TiaMcpServer.FakeWorker.exe`, PowerShell, Windows-Pfade). Sync 11.539 Tests, 2.245 fehlgeschlagen. Keine echte Regression:
  9 neue Upstream-Tests scheitern an derselben Linux-Ursache; `CreateQueryHash_ExcludesPageSizeButBindsQueryFields` hat einen
  neuen festen Hash über einen mit `ProjectPathNormalization.Canonicalize` gebildeten Windows-Pfad, der auf Linux anders
  ausfällt. Auf Windows bitte vollständig laufen lassen.

## Übernommen in dieser Runde (`adopt/2026-11-09`)

1. **`generate_loadable` sucht `LoadableProvider` auch an CPU-Geräteelement, Vorfahren und Gerät** (W16; Idee aus
   asckye/TIA_Portal_Openness_MCP, MIT, `93eeb016`; neu implementiert): neuer Siemens-freier `ServiceOwnerChain`
   (Contracts), Tests, Doku en/de, `THIRD_PARTY_NOTICES.md`.
2. **Info-Guard `plc_technology_object_create_may_terminate`** (W17; Beobachtung von asckye): feuert bei
   `create_technology_object` außerhalb der drei gemeldeten Paare, rät zum Speichern, lehnt nie ab. Tests, Doku en/de.
3. **V21-Fallstricke A5–A10** in `V21_PITFALLS.md`/`.de.md`; `HmiPitfallSourceTests` hält zusätzlich
   `SystemDiagnosisControlScriptBlock` aus dem HMI-Worker-Code heraus.
- Tests (Linux): 11.509 Tests, 2.235 fehlgeschlagen, **0 Regressionen** gegen die Basis.

## Offene Entscheidungen

- **Upstream-Bibliothekswerkzeuge (#220/#225/#226):** Der Fork behält seine eigene Umsetzung. Zu entscheiden ist, ob einzelne
  Upstream-Korrekturen gezielt übernommen werden sollen (z. B. exakte Version für `set_default_type_version`,
  Instanzsuche der Lösch-Vorschau auf den Umfang begrenzt, PLC-Gruppen-Elternteil über die Engineering-Schnittstelle).
- **Read-only als Startstandard (#227):** Upstream startet ohne Angabe read-only. Der Fork folgt der Owner-Entscheidung
  (Modus aus `settings.json`, sonst read-write).

## Offen für spätere Runden

- W2 Git-basierter Teilimport (hoch, groß)
- W4 Download-Bereitschaft (hoch)
- W13 automatisches Neuanbinden (mittel; nur Lesen wiederholen)
- W14 Checkpoint/Rücknahme (mittel)
- W3 TestSuite, W5 exportbasierte Querverweise, W6 SD-Import von PLC-Datentypen, W7 Unified-JSON-Roundtrip (mittel)
- `RhSystemProviders` auf `ServiceOwnerChain` umstellen (Aufräumen)
