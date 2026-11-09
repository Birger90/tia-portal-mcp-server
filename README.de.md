# TIA-Portal-MCP-Server

**Sprache:** [English](https://github.com/Birger90/tia-portal-mcp-server/blob/main/README.md) · Deutsch

[![Build Status](https://img.shields.io/github/actions/workflow/status/Czarnak/tia-portal-mcp/ci.yml?branch=main&style=flat-square)](https://github.com/Czarnak/tia-portal-mcp/actions)
[![Codecov](https://img.shields.io/codecov/c/github/Czarnak/tia-portal-mcp?style=flat-square)](https://codecov.io/gh/Czarnak/tia-portal-mcp)
[![GitHub Release](https://img.shields.io/github/v/release/Czarnak/tia-portal-mcp?style=flat-square)](https://github.com/Czarnak/tia-portal-mcp/releases)
[![.NET SDK](https://img.shields.io/badge/.NET-10.0-512BD4.svg?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![.NET Framework](https://img.shields.io/badge/.NET_Framework-4.8-512BD4.svg?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![TIA Portal](https://img.shields.io/badge/TIA_Portal-V21-009999.svg?style=flat-square&logo=siemens)](https://new.siemens.com/global/en/products/automation/industry-software/automation-software/tia-portal.html)
[![MCP](https://img.shields.io/badge/MCP-Ready-000000.svg?style=flat-square)](https://modelcontextprotocol.io/)
[![License: AGPL-3.0](https://img.shields.io/badge/License-AGPL--3.0-blue.svg?style=flat-square)](https://github.com/Birger90/tia-portal-mcp-server/blob/main/LICENSE)
![NuGet Downloads](https://img.shields.io/nuget/dt/TiaMcpServer)

> Die Badges zu Build, Codecov, Release und NuGet zeigen den Stand des Originalprojekts
> Czarnak/tia-portal-mcp, nicht den dieses Forks. Dieser Fork steht unter AGPL-3.0, siehe
> [Lizenzhinweise für diesen Fork](#lizenzhinweise-für-diesen-fork).

MCP-Server für Siemens SIMATIC TIA Portal V21. Er erlaubt MCP-Clients und KI-Agenten, ein laufendes TIA-Portal-Projekt über die Siemens-Openness-API zu untersuchen und zu bearbeiten.

Der aktuelle Stand umfasst:

- Projektsuche und Projekt-Lebenszyklus (öffnen, anlegen, speichern, archivieren, schließen),
- Export und Import von PLC-Bausteinen, Lesen von Variablentabellen und abgesicherte Schreibzugriffe auf die PLC-Software,
- Hardware- und Netzwerkanalyse, Suche im Hardwarekatalog und abgesichertes Anlegen von Netzwerkgeräten,
- WinCC-Unified-HMI: Lesen sowie abgesichertes Schreiben von Variablentabellen, Variablen und Meldungen,
- VCI-Arbeitsbereiche (Version Control Interface) für eigenständige Projekte,
- Querverweise sowie Übersetzen und Prüfen mit Compiler-Diagnose,
- abgesicherten PLC-Download auf PLCSIM oder echte Hardware (gesteuert über den Zugriffsmodus),
- über Openness: Vergleich offline/online, Online-Verbindung herstellen und trennen, Station hochladen sowie Beobachtungs- und Forcetabellen bearbeiten,
- S7-PLCSIM Advanced: Steuerung virtueller CPUs und Testszenarien im Worker,
- echte S7-1500-CPUs: Beobachten und Steuern über die SIMATIC-S7-1500-Web-API.

Download-, Simulations- und Online-Werkzeuge sind noch nicht live getestet.

## Werkzeuge

Der Server stellt im Modus `read-only` 13 Werkzeuge bereit, in `read-write` (Standard beim Start) 33 und in `full` dieselben 33.

`read-write` erlaubt:
- Änderungen im Projekt und Übersetzen,
- Lebenszyklus-Aufrufe für Projekte,
- den PLC-Download auf PLCSIM oder Hardware,
- Online-Änderungen an konfigurierten S7-1500-CPUs,
- Online-Verbindung herstellen und trennen sowie Station hochladen,
- die Steuerung der Simulation in S7-PLCSIM Advanced.

Nachgefragt wird einmal pro tatsächlichem Lebenszyklus-, Download-, Online-Schreib-, Online-Verbindungs- oder Upload-Aufruf. Außerdem einmal pro `library_write`-Aufruf, der eine globale Bibliothek öffnet, anlegt, abruft, speichert oder schließt. Und einmal pro `project_server_write`-Aufruf, der eine lokale Sitzung oder eine Serververbindung löscht oder ein Projekt hochlädt. `full` bringt keine zusätzlichen Werkzeuge oder Operationen mit; es führt diese Aufrufe nur ohne Nachfrage aus.

Fast alle schreibenden Werkzeuge laufen über dieselbe abgesicherte Pipeline mit genau einem Aufruf; Sicherheits-Tokens gibt es nicht. Das gilt für `plc_write`, `network_write`, `hmi_write`, `vci_write`, `library_write`, `plc_download`, `plc_online_write`, `plc_tia_online`, `plc_station_upload`, `project_server_write` und die sechs Lebenszyklus-Werkzeuge. Ausgenommen sind nur die PLCSIM-Werkzeuge.

RUN/STOP über TIA Portal gibt es nicht. Echte Steuerungen werden nur auf zwei Wegen verändert:
- durch `plc_download`,
- durch `plc_online_write`, und zwar nur bei Steuerungen, die der Nutzer in einer Konfigurationsdatei aufgeführt hat.

### PLC lesen und Querverweise

- `plc_read` führt bis zu 50 PLC-Leseoperationen in einem Aufruf aus. Alle Ergebnisse sind strukturiertes JSON mit `contractVersion` `1.0`. `describe_operations` liefert die Felder und vollständigen Regeln jeder `plc_read`/`plc_write`-Operation, ohne gebundenes Projekt ([Operationsreferenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md#operation-reference-describe_operations)).
  - **Allgemein:**
    - `get_block_content`, `get_type_content` und `list_tag_tables`; `get_block_content` mit `format:"sd"` liefert nur die SIMATIC-SD-Dokumente (`.s7dcl`, `.s7res`) mit `sd:sha256`-Hash und `failSafe` für F-Bausteine ([SIMATIC-SD-Referenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_SIMATIC_SD_SUMMARY.de.md); noch nicht live getestet).
    - `list_networks` zeigt die Netzwerke eines Bausteins aus dem SD-Export: Nummer, Sprache (Pragma `S7_Language` oder erkannt wie im Siemens-Skill), alle Pragma-Schlüssel, Titel und Kommentar je Sprache aus der `.s7res`, den Code und den `contentHash` für die Netzwerk-Operationen (noch nicht live getestet).
    - `list_download_targets` liefert den Offline-Downloadpfad für `plc_download`, gelesen ohne online zu gehen.
    - `read_address_occupancy` / `find_free_address` zeigen die E/A/M-Adressbelegung aus deklarierten Variablen und Hardwareadressen.
    - `get_plc_security` zeigt je CPU die Zugriffsstufe, die Zugriffssteuerung und den Dienst für „Vertrauliche PLC-Konfigurationsdaten schützen“ (mit `MasterSecretConfiguration`), um Fehler beim Hardware-Übersetzen und Laden zu klären ([Referenz zum CPU-Schutz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_SECURITY_SUMMARY.de.md); nicht live getestet). Ein Dienst, den die CPU auflistet, den `GetService` aber nicht liefert (live gesehen bei `PlcAccessControlConfigurationProvider`, Defekt D14), lässt nicht mehr die ganze Abfrage scheitern: dieser Teil heißt dann nicht lesbar, der Rest wird geliefert.
  - **Online-Bezug über Openness** ([PLC-Online-Referenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_ONLINE_OPENNESS_SUMMARY.de.md)):
    - `compare_to_online`; eine TLS-Zertifikatsprüfung dabei wird nur über `trustedCertificates` des Eintrags bejaht (wie bei `plc_download`), Online-Passwortabfragen eines geschützten CPUs über `passwords` (Passwortquellen) oder die Zuordnungsdatei; sonst wird der Vergleich mit Belegen verworfen.
    - `compare_software` vergleicht mit einer anderen PLC, der Projektbibliothek oder einer Kopiervorlage; der Vergleichsbaum ist in Tiefe und Anzahl begrenzt.
    - `compare_hardware` und `compare_hardware_to_online` vergleichen die Hardware der Station oder CPU mit der einer anderen PLC, einer Kopiervorlage oder dem Gerät online (Issue #7, nicht live getestet; dieselben TLS- und Passwortregeln wie `compare_to_online`; V21 dokumentiert nicht, welche Objekte Ziel eines Hardwarevergleichs sein können, ein ungeeignetes wird gemeldet, nie geraten).
    - `get_online_state`, `list_station_upload_interfaces` und `list_watch_and_force_tables`.
    - `read_watch_table`, `read_force_table`, `get_watch_table_content` und `get_force_table_content`.
  - **Schnittstellen und Datenbausteine:**
    - `get_block_interface` / `get_type_interface` liefern typisierte Bäume der Schnittstellenelemente mit dem xml-`contentHash`.
    - `get_db_values` liefert Startwerte globaler und Instanz-DBs in beliebiger Tiefe.
    - `get_db_snapshot` liefert den im Offline-Projekt gespeicherten Schnappschuss der Istwerte eines DB (Pfad, Wert, Datentyp, Zeitpunkt; filterbar mit `memberPath`). Übernommen aus BetterTIA/watch `watch_db` ([Referenz zu den BetterTIA-Operationen](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/BETTERTIA_ADOPTED_OPERATIONS_SUMMARY.de.md); nicht live getestet).
  - **Programmanalyse** ([Referenz zur Programmanalyse](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md#program-analysis-plc_read); nicht live getestet):
    - `get_call_tree`, `find_call_paths`, `find_unused_blocks`, `find_orphaned_instance_dbs` und `analyze_change_impact`.
    - Berechnet aus dem Bausteinbestand und dem Querverweis-Leser.
    - Genutzt werden nur Beziehungen, die eine Querverweisstelle ausdrücklich nennt.
    - Die Ergebnisse sind begrenzt, Teilergebnisse sind gekennzeichnet, indirekte Aufrufe sind nicht sichtbar.
  - **Externe Quellen, Suche und Gerätescan** ([Referenz zu externen Quellen und Ordnern](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_SOURCES_FOLDERS_SUMMARY.de.md); nicht live getestet):
    - `list_external_sources` listet die externen Quellen einer PLC.
    - `read_tag_comments` liefert die mehrsprachigen Kommentare von Variablen und Anwenderkonstanten.
    - `find_objects` sucht mit Glob-Mustern in Bausteinen, Gruppen, Typen, Variablen-, Beobachtungs- und Forcetabellen sowie externen Quellen und blättert.
    - `scan_accessible_devices` ruft `GetAccessibleDevices()` an einer ausdrücklich genannten PC-Schnittstelle auf. Die Suche im Netzwerk ist live, ändert aber nichts; sie steht in jedem Zugriffsmodus zur Verfügung, nur für eigenständige Projekte.
  - **Textlisten und Meldungen** ([Referenz zu PLC-Meldungen und Textlisten](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_ALARMS_TEXTLISTS_SUMMARY.de.md); nicht live getestet):
    - `list_text_lists` liefert Anwender- und Systemtextlisten mit ID, Bereich und mehrsprachigem Kommentar sowie die Projektsprachen.
    - `read_text_list_workbook`, `read_alarm_text_workbook` und `read_supervision_workbook` lesen den XLSX-Export von TIA Portal als uninterpretierte Zellen. Sie liefern einen `workbookHash` und blättern über `sheet`/`startRow`/`startColumn` innerhalb des Wertebudgets.
    - `get_prodiag_fb` und `get_prodiag_assignment` liefern ProDiag-Belege und `AssignedProDiagFB`.
  - **Technologieobjekte** ([Referenz zu Technologieobjekten](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/TECHNOLOGY_OBJECTS_SUMMARY.de.md); nicht live getestet):
    - `list_technology_objects`, `get_technology_object_hardware_connection` und `get_technology_object_content`.
    - `get_technology_object_parameters` typisiert jeden Wert nach seinem Laufzeittyp und blättert.
  - **Fehlersichere Steuerungen (F-CPU)** ([Referenz zu F-Programmen](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_SAFETY_SUMMARY.de.md); nicht live getestet):
    - `get_safety_administration` liest je CPU Passwortschutz, Anmeldestatus, Safety-Systemversion, F-Ablaufgruppen (mit `attributeInfos`: jedes Attribut, das `GetAttributeInfos()` meldet, mit Zugriffsart), Sammelsignaturen und Safety-Units; fehlende Openness-Mitglieder meldet es als `api_member_missing`.
    - `list_safety_blocks` listet die F-Bausteine mit ihren Signaturen.
  - **Aufbau einer Anfrage:**
    - Jeder Eintrag hat eine eindeutige `operationId`, einen `operation`-Namen und die Parameter dieser Operation.
    - Die Einträge laufen unabhängig voneinander; ein fehlschlagender Eintrag hält die anderen nicht auf.
    - Ein Wert über dem Wertebudget (Standard 60.000 Zeichen) wird mit Hinweisen zum Eingrenzen ganz weggelassen, nie abgeschnitten.
- `read_cross_references` liest die Querverweise eines Ziels im Projektbaum: einen Baustein, Typ, eine Variable oder Konstante, oder eine PLC, Software Unit oder einen Ordner. Bei den letzten dreien werden alle Besitzer darunter durchlaufen.
  - Argumente: `target`, `filter`, `maxResults`.
  - Beide Leser stehen in jedem Zugriffsmodus zur Verfügung und arbeiten nur auf dem gebundenen Projekt.
  - Sie öffnen oder wechseln nie ein Projekt. Ohne geprüfte Bindung schlagen sie sicher mit `binding_conflict` fehl; dann zuerst `bind_project` aufrufen.

### HMI lesen und schreiben

- `hmi_read` führt bis zu 50 Leseoperationen auf WinCC-Unified-HMIs in einem Aufruf aus.
  - Abgedeckt sind Geräte, Variablen, Verbindungen, Meldungen, Archive, Bilder und Bildobjekte, Faceplates, Navigation, Runtime-Einstellungen, Skriptmodule, Text- und Grafiklisten, Sprachen und `validate`.
  - In jedem Zugriffsmodus registriert; geblättert wird mit `offset`/`limit` (Standard 100).
  - Ergebnisse sind strukturiertes JSON mit `contractVersion` `1.1`.
  - Klassische WinCC-HMIs (Comfort/Advanced Panels) liest `list_classic_objects` (Issue #28, nicht live getestet; [klassisches WinCC und Unified-Erweiterungen](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/HMI_CLASSIC_UNIFIED_EXTENSIONS_SUMMARY.de.md)); die anderen Operationen lehnen sie ab.
  - Siehe die [HMI-Übersicht](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/HMI_OPERATIONS_SUMMARY.de.md).
- `hmi_write(operations, dryRun=false)` legt WinCC-Unified-Variablentabellen, Variablen sowie Bit- und Analogmeldungen an, ändert oder löscht sie.
  - Neun Operationen für Tabellen, Variablen und Meldungen (dazu neun für Bilder, Skripte und Umbenennungen, siehe unten) in read-write/full, bis zu 50 Einträge in der Reihenfolge des Aufrufers.
  - Ziel ist ein geprüftes eigenständiges Projekt oder eine lokale Sitzung; Schreibvertrag `2.0`.
  - `dryRun:true` zeigt eine Vorschau.
  - Die Ausführung stoppt beim ersten Fehler, ohne Rücknahme des Stapels und ohne Server-Nachfrage.
  - Eine `EngineeringTargetInvocationException` während der Änderung wird zu `engineering_rejected`, außer `portal_lost` hat Vorrang.
  - Typisierte Siemens-Fehler tragen `engineeringError`, außer bei `portal_lost`. Teilergebnisse gibt es nur, wenn ein Writer zugeteilt wurde.
  - Nach Schreibzugriffen den tatsächlichen Zustand prüfen, das öffentliche `validate` ausführen und das HMI-Gerät in TIA Portal übersetzen.
  - Felder, drei Wächter und gemeinsame Verifikation beschreibt die [HMI-Referenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/HMI_OPERATIONS_SUMMARY.md#writes-hmi_write).
  - Der [Qualifizierungsbericht](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/superpowers/acceptance/reports/2026-10-30-hmi-write-native-rejections-live-verification.md) hält die abgeschlossene Qualifizierung im festgehaltenen Umfang R1/R2/R3 fest.
  - **Bilder, Skripte und Sammel-Umbenennungen, aus BetterTIA übernommen** (nicht live getestet; [Referenz zu den BetterTIA-Operationen](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/BETTERTIA_ADOPTED_OPERATIONS_SUMMARY.de.md)): `create_screen` / `delete_screen`, `rename_screens` und `rename_tags` (mehrere Bilder bzw. HMI-Variablen mit Tausch in einer Transaktion), `create_screen_item` (Openness-Typname, z. B. `HmiButton`) / `delete_screen_item`, `set_screen_item_property` (Wert im Typ des aktuellen Werts, Farben als `#RRGGBB`), `set_dynamization_tag` (Variablendynamisierung setzen oder anlegen) und `replace_in_scripts` (`{ find, replace }` in allen Skripten). Fehlt ein Openness-Mitglied, schlägt der Eintrag mit `api_member_missing` fehl, ohne etwas zu ändern. Die PLC-Variable einer HMI-Variable setzt weiterhin `update_tag` (`plcTag`).
  - **Unified-Ereignisse und -Dynamisierungen** (Issue #29, nicht live getestet; [klassisches WinCC und Unified-Erweiterungen](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/HMI_CLASSIC_UNIFIED_EXTENSIONS_SUMMARY.de.md)): `set_event_script` / `delete_event_script` (Ereignis- und Eigenschaftsereignis-Handler mit ihrem Skript), `set_dynamization_script` (mit Trigger), `set_dynamization_flashing`, `delete_dynamization`; Lesezugriffe `list_screen_item_events` und `list_screen_item_dynamizations`. Formen und Widgets legt `create_screen_item` über ihren Openness-Typnamen an.
  - **Unified-Audit-Klassen, Runtime-Einstellungen und Anlagensichten** (Issue #29, nicht live getestet; [dieselbe Referenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/HMI_CLASSIC_UNIFIED_EXTENSIONS_SUMMARY.de.md#wincc-unified-audit-klassen-runtime-einstellungen-anlagensichten-issue-29)): `create_audit_class` / `delete_audit_class` / `set_audit_class_property`, `set_runtime_setting`, `create_plant_view` / `delete_plant_view`, `create_plant_view_node` / `delete_plant_view_node`, `set_plant_object_interface_property`; Lesezugriff `list_plant_views`.
  - **Klassische WinCC-HMIs** (Issue #28, nicht live getestet; [klassisches WinCC und Unified-Erweiterungen](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/HMI_CLASSIC_UNIFIED_EXTENSIONS_SUMMARY.de.md)): `export_classic_objects` / `import_classic_objects` (SimaticML-Dateien von Variablentabellen, Variablen, Bildern, Vorlagen, Pop-ups, Slide-ins, VB-Skripten, Text- und Grafiklisten, Verbindungen, Zyklen, globalen Elementen und der Bildübersicht), `delete_classic_object`, `create_classic_folder` / `delete_classic_folder`. Jedes Member wird zur Laufzeit gebunden (der Worker referenziert `Siemens.Engineering.WinCC.dll` weiterhin nicht); Grafiklisten werden nie exportiert, und ein Bildimport mit doppelter Bildnummer wird abgelehnt (V21-Fallstricke 3 und 4).
  - **Textlisten von WinCC Unified als Datei** (nicht live getestet): `export_text_lists` schreibt je Anwender-Textliste eine `<Name>.xml` in ein vorhandenes Verzeichnis (alle oder `textListNames`), `import_text_lists` importiert eine Datei (mit `overwrite:true` über `ImportOptions.Override`). Siemens dokumentiert diese Mitglieder für Unified-Textlisten nicht; sie werden zur Laufzeit gebunden, fehlen sie, schlägt der Eintrag mit `api_member_missing` fehl, ohne etwas zu schreiben oder zu ändern (Info-Wächter `hmi_text_list_file_unverified`). Grafiklisten und Systemtextlisten werden nie exportiert (V21-Fallstrick 3).

### Version Control Interface

- `vci_read` untersucht in jedem Modus die Arbeitsbereiche eigenständiger Projekte, geblätterte Zuordnungen, optional den Rohstatus und die unterstützten Blattformate.
- `vci_write(operations, dryRun=false)` legt in read-write/full Arbeitsbereiche an und löscht sie, exportiert Blattbereiche, synchronisiert in beide Richtungen und löscht Zuordnungen.
  - Schutz vor einem bekannten Absturz: Ein ausdrücklich verlangter Export im Format SIMATIC SD wird für F-Bausteine und Bausteine in Systemordnern abgelehnt, weil TIA Portal V21 dabei in Tests von Czarnak dreimal abgestürzt ist. SimaticML geht für diese Bausteine weiterhin.
  - Führt standardmäßig aus; `dryRun:true` zeigt eine Vorschau.
  - Stoppt nach dem ersten fehlgeschlagenen Eintrag, ohne Rücknahme über Einträge hinweg und ohne Server-Nachfrage.
  - Das Projekt speichern, damit Arbeitsbereiche und Zuordnungen erhalten bleiben; vor einer Wiederholung den aktuellen Zustand prüfen.
  - Die [VCI-Referenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/VCI_OPERATIONS_SUMMARY.de.md) beschreibt Formate, Pfadwächter, Synchronisierungsentscheidungen, die Wiederherstellung nach Portal-Verlust und die Grenzen der Abnahme.

### Bibliotheken (noch nicht live getestet)

- `library_read` führt in jedem Modus bis zu 50 unabhängige Leseoperationen aus. Gelesen werden die Bibliothek des gebundenen eigenständigen Projekts und die geöffneten globalen Bibliotheken:
  - `list_libraries`, `list_global_library_infos`,
  - `get_library_tree` (Typordner, Typen mit Versionen und Zustand, Kopiervorlagenordner und Kopiervorlagen; mit offset/limit geblättert),
  - `get_type` (auch DoNotUse, Zustand, Namespace), `find_type_instances`, `get_instance_info`, `update_check`, `compare_libraries`,
  - `get_supported_export_formats`, `compare_types`, `compare_type_versions`, `compare_master_copies` (Detailvergleiche).
- `library_write(operations, dryRun=false)` in read-write/full:
  - **Globale Bibliotheken:** `open_global_library`, `retrieve_global_library` (auch mit Upgrade), `create_global_library`, `save_global_library`, `close_global_library`, `archive_global_library`, `save_global_library_as`. In read-write wird einmal pro Aufruf nachgefragt, der eine davon enthält.
  - **Kopiervorlagen:** `create_master_copy`, `copy_master_copy`, `delete_master_copy`, `rename_master_copy`, `instantiate_master_copy` (PLC-Gruppen, Technologieobjekte, Software Units, externe Quellen, Anwendertextlisten, Geräte, Geräteelemente).
  - **Kopiervorlagenordner:** `create_master_copy_folder`, `delete_master_copy_folder`, `rename_master_copy_folder`; jedes Kopiervorlagen-Item nimmt `masterCopyFolderPath`.
  - **Typen:** `instantiate_type_version`, `update_project` (auch mit den Aktualisierungsmodi), `rename_type`, `rename_type_folder`, `edit_type_version`, `release_type_version`, `discard_type_version`, `set_default_type_version`, `delete_type_version`, `delete_type`, `set_type_do_not_use`, `create_type_folder`, `delete_type_folder`.
  - **Sekundärprojekte (Issue #20):** `open_secondary_project`, `retrieve_secondary_project`, `close_secondary_project` öffnen ein Projekt schreibgeschützt neben dem gebundenen (nie gebunden); daraus kopieren mit `create_master_copy` `source.project`, vergleichen mit `plc_read` `compare_software` `compareTarget: secondary_plc`, auflisten mit `library_read` `list_secondary_projects`.
  - **TIA-Portal-Einstellungen (Issue #27):** `library_read` `list_portal_settings`, `library_write` `set_portal_setting` (TiaPortal.SettingsFolders; die ganze TIA-Portal-Instanz; jede Änderung fragt in read-write nach, eine sicherheitsrelevante löst zusätzlich einen Bestätigungs-Guard aus).
  - **Bibliothekspflege (Issue #17):** `clean_up_library`, `harmonize_project`, `update_library`, `create_type_from_documents`, `create_version_from_documents`, `export_type_version`, `export_type_version_as_documents`.
  - Die Wächter sind informativ. Ausnahme: Ein Schließen wird verweigert, solange die Bibliothek Änderungen hat, die dieser Server nicht gespeichert hat.
  - Bestätigungsdialoge von TIA Portal werden über `portalConfirmations` beantwortet (Standard `decline`) und festgehalten.
  - Die meisten Siemens-Elemente werden erst zur Laufzeit gebunden und schlagen sicher fehl, wenn sie fehlen.
  - Siehe die [Bibliotheksreferenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/LIBRARY_OPERATIONS_SUMMARY.de.md).

### S7-PLCSIM Advanced (noch nicht live getestet)

- `plcsim_read` führt in jedem Modus bis zu 50 unabhängige Leseoperationen auf virtuellen S7-1500-CPUs von S7-PLCSIM Advanced aus:
  - `probe_api` zeigt, wo die PLCSIM-Advanced-API gefunden wurde und wie sich jedes vom Server genutzte API-Element auflöst. Diese Operation zuerst ausführen.
  - `list_instances`, `get_instance`, `list_tags` und typisiertes `read_tags`.
- `plcsim_write(operations, dryRun=false)` in read-write/full: `power_on`, `power_off`, `run`, `stop`, `memory_reset`, `update_tag_list`, typisiertes `write_tags` und `set_scale_factor`.
  - Die Instanz wird über ihren exakten Namen ausgewählt.
  - Instanzen werden im PLCSIM Advanced Control Panel angelegt, nie vom Server.
- `plcsim_test` führt in read-write/full im PLCSIM-Worker ein Szenario mit bis zu 200 Schritten `write`/`wait`/`expect` aus.
  - Pro Schritt gibt es Status, zuletzt beobachteten Wert, Versuche und Zeiten.
  - Die Zeiten sind Wanduhrzeit; es gibt keine Synchronisierung mit virtueller Zeit.

Diese Werkzeuge laufen in einem eigenen PLCSIM-Worker, der beim ersten Aufruf startet. Sie berühren weder TIA Portal noch die Projektbindung.

Die PLCSIM-Advanced-API wird zur Laufzeit aus der Installation geladen und per Reflection gebunden. Ein Element, das in der installierten Version fehlt, schlägt mit `plcsim_api_member_missing` fehl.

Die Steuerung der Simulation ist die Fähigkeit `SimulationControl`; in read-only ist sie verweigert. Jeder Aufruf von `plcsim_write`/`plcsim_test` wird protokolliert. Siehe die [PLCSIM-Referenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLCSIM_OPERATIONS_SUMMARY.de.md) mit den Schritten der Live-Abnahme.

### S7-1500-CPUs online über die Web-API (noch nicht live getestet)

- `plc_online_read` führt in jedem Modus bis zu 50 unabhängige Beobachtungen echter S7-1500-CPUs aus. Das gilt auch für PLCSIM-Advanced-Instanzen mit aktiviertem Webserver.
  - Abgedeckt sind `ping`, `version`, `permissions`, Betriebszustand und Betriebsartenschalter, CPU-Typ, Laufzeitinformationen, Systemzeit, Projektinformationen, `browse_tags`, `read_tags`, Diagnosepuffer, Meldungen, Module (Status, LEDs) und Syslog.
  - Dazu kommt `read_watch_table_live` (nicht live getestet): Eine Beobachtungs- oder Variablentabelle des gebundenen Projekts wird offline gelesen, dann werden ihre Symbole online gelesen. Gelesen werden nur Namen, die sich nach einer dokumentierten Regel abbilden lassen; die übrigen werden als nicht abbildbar aufgeführt.
- `plc_online_write` führt in read-write/full bis zu 50 geordnete Online-Änderungen über die abgesicherte Pipeline aus:
  - `change_operating_mode` (RUN/STOP), `write_tags`, `acknowledge_alarm`, `flash_leds`, `set_system_time`, `create_backup` und `restore_backup`.
  - read-write fragt einmal pro tatsächlichem Aufruf nach; full läuft nach Richtlinie.
  - `dryRun:true` zeigt eine Vorschau, ohne eine Steuerung zu kontaktieren.

Beide nutzen den offiziellen Siemens-Client `Siemens.Simatic.S7.Webserver.API` im Host.

Die Steuerungen, ihre Adressen, Zertifikatsregeln und die Umgebungsvariablen mit den Zugangsdaten legt der Nutzer in einer Datei fest. Sie wird mit `--plc-webapi-config` (oder `TIA_MCP_PLC_WEBAPI_CONFIG`) übergeben.

Die Werkzeuge nennen nur ein konfiguriertes `target`. Zugangsdaten erscheinen nie in Argumenten, Ergebnissen oder im Protokoll. Siehe die [PLC-Online-Referenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_ONLINE_WEBAPI_SUMMARY.de.md) mit den Voraussetzungen in TIA Portal und den Schritten der Live-Abnahme.

### Siemens-Dokumentation (auf dem Windows-PC noch nicht live getestet)

- `siemens_docs` liest in jedem Modus und ohne TIA Portal die offizielle Siemens-Dokumentation auf `docs.tia.siemens.cloud` (TIA Portal, STEP 7, WinCC, Openness, Hardware) und `docs.industrial-operations-x.siemens.cloud` (SIMATIC AX, Industrial Edge):
  - `search` (Volltext, Filter für Produkt, Version und Sprache; jeder Treffer nennt die anderen Versionen desselben Themas),
  - `read_page` (ein Thema als Markdown, in Seiten bis 40.000 Zeichen),
  - `get_toc` (Gliederung einer Publikation oder eines Kapitels) und `list_publications`.
- Nur die freigegebenen Hosts werden kontaktiert (`--docs-hosts`); Referenzen werden in Ids aufgelöst und nie selbst abgerufen; keine Weiterleitungen, Mindestabstand je Host, Zeitlimits und Größengrenzen.
- Es ist eine eigene C#-Portierung von [Czarnak/siemens-docs-mcp](https://github.com/Czarnak/siemens-docs-mcp) (MIT); das Python-Paket wird nicht installiert. Siehe die [Referenz zur Siemens-Dokumentation](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/SIEMENS_DOCS_SUMMARY.de.md) mit der Live-Prüfung der Endpunkte.

### PLC schreiben

`plc_write(operations, dryRun=false)` führt bis zu 50 Schreibzugriffe auf die PLC-Software in der Reihenfolge des Aufrufers aus. Ziel ist genau ein geprüftes, bereits geöffnetes Projekt.

**Operationen:**
- **Bausteine, Typen und Gruppen:** `update_block_logic`, `update_type_content`, `create_block` / `delete_block`, `create_block_group` / `delete_block_group`.
- **Variablen und Konstanten:** `create_tag_table` / `delete_tag_table`, `create_tag` / `update_tag` / `delete_tag` (`create_tag` und `update_tag` setzen mit `comment` und `commentCulture` den Variablenkommentar einer Sprache, zurückgelesen; nicht live getestet), `move_tag` (abgesichertes Neuanlegen in einer anderen Tabelle), `create_user_constant` / `update_user_constant` / `delete_user_constant`.
- **Beobachtungs- und Forcetabellen, Programm:**
  - `create_watch_table` / `delete_watch_table`, `create_watch_table_group` / `delete_watch_table_group`.
  - `update_watch_table_content` / `update_force_table_content` als Offline-Rundlauf über SimaticML; online forcen ist nicht möglich.
  - `update_program`.
- **Feine Bausteinänderungen** (nicht live getestet; know-how-geschützte Bausteine werden für Kopieren/Verschieben abgelehnt, weil ihr SimaticML-Export nur die Schnittstelle enthält; fehlersichere Bausteine folgen der Anmelderegel von TIA Portal, siehe unten):
  - `add_interface_member` / `update_interface_member` / `delete_interface_member`: Das exportierte SimaticML-Dokument wird bearbeitet und mit dem Hash des Exports wieder importiert.
  - `update_db_member`: DB-Startwerte und Elementattribute in beliebiger Tiefe.
  - `create_instance_db`, `rename_block`, `renumber_block`, `copy_block`, `move_block` und `move_tag_table`.
- **KOP/FUP/SCL-Netzwerke als Text über SIMATIC SD** (am 2. 11. 2026 auf V21 live getestet: SD-Export, `update_block_logic` sd, die drei Netzwerk-Operationen und `create_block_from_document`, siehe [Live-Bericht vom 2. 11. 2026](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/superpowers/acceptance/reports/2026-11-02-simatic-sd-and-safety-live.md); [SIMATIC-SD-Referenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_SIMATIC_SD_SUMMARY.de.md)): der Weg, den der Siemens-V21-Skill „simatic-sd“ für LAD/FBD/SCL-Netzwerke in bestehenden Bausteinen nennt.
  - `insert_network` (Code, optional `language` und `position`), `replace_network` (`networkNumber`, Code; Titel, Kommentar und alle übrigen Pragma-Schlüssel bleiben) und `delete_network`: auf dem SD-Export geplant und über `update_block_logic` mit `format:"sd"` hashgeprüft importiert (`ImportFromDocuments` mit `ImportDocumentOptions.Override`); die `.s7res` bleibt unverändert. Mehrere Einträge auf denselben Baustein in einem Aufruf werden nacheinander neu geplant.
  - Code darf ein RUNG-Rumpf, ein ganzes `NETWORK … END_NETWORK` (mit oder ohne Pragma) oder SCL sein; innere Pragmas wie `{ S7_Templates := "…" }` bleiben erhalten. Abgelehnt wird nur, was das Dokument zerstören würde (mehr als ein Netzwerk, unausgeglichenes RUNG/END_RUNG, Titel-Schlüssel); über die LAD-Semantik entscheidet TIA Portal.
  - Geprüft durch erneuten Export (`networkCode`: Anzahl, Position, Sprache). Ob der Export denselben Text zeigt, steht als `codeMatchesExport` in der Meldung – berichtet ist, dass V21 manche Anweisungen anders exportiert, als sie importiert werden; live am 2. 11. 2026 auf V21 für GT/EQ_Contact, Move, P_Trig/N_Trig, TON/TOF und Add nicht nachgestellt (nur Leerzeichen unterschieden sich). Info-Wächter `plc_sd_reimports_export_form` bleibt für nicht getestete Anweisungen; danach mit `compile_check` übersetzen.
  - `create_block_from_document` legt aus `.s7dcl` und optional `.s7res` einen neuen Baustein in `targetGroupPath` an (ein Baustein ohne Texte braucht keine `.s7res`, TIA Portals eigener Export hat dann auch keine); ein gleichnamiger Baustein irgendwo in der PLC wird nur mit `overwrite:true` ersetzt.
  - Jede Sprache in der `.s7res` muss eine Projektsprache sein: Live scheiterte ein Import mit `en-US`-Texten in einem deutschen Projekt („Cannot import multilingual text with culture 'en-US' …“), mit nur `de-DE` gelang er. Die Regel „en-US für jeden Eintrag“ (berichtet von BetterTIA/1748477582) ist damit live widerlegt; das Ergebnis nennt die enthaltenen Sprachen.
  - Ein Kopf-Pragma `S7_Safety := "TRUE"` kennzeichnet einen F-Baustein: Es gelten dieselben Regeln wie unten bei den fehlersicheren Steuerungen.
- **Aus BetterTIA übernommen** (nicht live getestet; [Referenz zu den BetterTIA-Operationen](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/BETTERTIA_ADOPTED_OPERATIONS_SUMMARY.de.md)):
  - `set_network_text` setzt Titel und/oder Kommentar eines Netzwerks (Nummer ab 1, optional `culture`; eine noch fehlende Sprache wird angelegt), `replace_network_text` ersetzt `{ find, replace }`-Paare in einem Durchgang (wörtlich, längster Treffer zuerst, Tausch möglich) in allen Netzwerktiteln und -kommentaren, mit `textScope:"all"` auch im Bausteintitel/-kommentar und in GRAPH-Texten. Openness kennt keinen Setter dafür: Das exportierte SimaticML wird bearbeitet und hashgeprüft über `update_block_logic` wieder importiert; geprüft wird durch erneuten Export (Info-Wächter `plc_text_edit_reimports_block`, danach übersetzen).
  - `rename_members` benennt mehrere Schnittstellenelemente eines Bausteins oder PLC-Datentyps gemeinsam um (Tausch möglich) und passt wie der TIA-Portal-Editor die Zugriffe im eigenen Code an (LAD/FBD/AWL/SCL/GRAPH). Zugriffe in anderen Bausteinen, HMI-Variablen und Beobachtungstabellen ändert es nicht – das tut TIA Portal auch nicht; vorher `analyze_change_impact` lesen, danach übersetzen. Geprüft durch erneutes Lesen der Schnittstelle.
  - `rename_tags` benennt PLC-Variablen über alle Variablentabellen einer PLC, `rename_objects` Bausteine, PLC-Datentypen und Technologieobjekte nach Namen um – mehrere auf einmal, mit Tausch und Ringtausch über temporäre Namen, alles in einer `ExclusiveAccess`-Transaktion: Scheitert ein Schritt, wird alles zurückgenommen. Fehlersichere Variablen und F-Bausteine folgen der Anmelderegel von TIA Portal (Info-Wächter `plc_batch_rename`).
  - `replace_tag_comments` ersetzt `{ find, replace }`-Paare in den mehrsprachigen Kommentaren der PLC-Variablen (eine Tabelle oder alle, optional nur eine Sprache) in einer Transaktion; die Vorschau listet jeden geänderten Text, geprüft wird durch erneutes Lesen mit `read_tag_comments`. Es ändert nur vorhandene Texte; den ersten Kommentar einer Variablen setzt `update_tag` mit `comment` und `commentCulture`.
  - `create_db_snapshot` nimmt bei online verbundener PLC einen Schnappschuss der Istwerte eines DB (`ValueService.CreateSnapshot()`), den TIA Portal im Offline-Projekt speichert, und liefert ihn zurück. In die PLC wird nichts geschrieben, Startwerte bleiben unverändert (Info-Wächter `plc_stores_db_snapshot`).
- **Externe Quellen, Gruppen, Software Units und Dateiexporte** (nicht live getestet; [Referenz zu externen Quellen und Ordnern](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_SOURCES_FOLDERS_SUMMARY.de.md)):
  - Externe Quellen: `import_external_source`, `generate_blocks_from_source`, `delete_external_source` und `export_source_file`.
  - Gruppen umbenennen: `rename_block_group`, `rename_type_group`, `rename_tag_table_group` und `rename_watch_table_group`.
  - Gruppen anlegen und löschen: `create_type_group` / `delete_type_group`, `create_tag_table_group` / `delete_tag_table_group`.
  - Software Units: `create_software_unit`, `delete_software_unit` und `rename_software_unit`.
  - Dateiexporte: `export_tag_tables_csv`, `export_watch_tables_csv`, `export_force_tables_csv` und `export_blocks`.
  - Variablentabellen als SimaticML-Datei: `export_tag_table` (eine Tabelle über `PlcTagTable.Export` in eine Datei, mit Hash) und `import_tag_table` (Import einer privaten Kopie der Datei in einer Transaktion; mit `overwrite:true` über `ImportOptions.Override`, Info-Wächter `plc_import_replaces_tag_table`; mit `tableName` wird die Tabelle vorab und danach geprüft). Beide Siemens-Mitglieder sind im Siemens-Skill „tags-and-tagtables“ dokumentiert; nicht live getestet ([Referenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/BETTERTIA_ADOPTED_OPERATIONS_SUMMARY.md#tag-tables-and-wincc-unified-text-lists-as-files-owner-additions)).
  - OPC-UA-Export: `export_opcua_xml` schreibt die OPC-UA-Daten der CPU einer PLC als XML-Datei (`OpcUaExportProvider.Export`, zur Laufzeit gebunden; Idee aus heilingbrunner/tiaportal-mcp, MIT; nicht live getestet).
  - Für `export_source_file` und die Dateiexporte gilt:
    - absolute Pfade auf einem Laufwerk oder einer UNC-Freigabe (`\\server\freigabe\...`); nur Gerätepfade (`\\?\`, `\\.\`) werden abgelehnt, weil sie die Windows-Pfadnormalisierung umgehen,
    - zugeordnete Netzlaufwerke sind erlaubt und werden mit einer Warnung gemeldet,
    - kein Überschreiben ohne `overwrite:true`,
    - jede geschriebene Datei wird erneut gelesen und über ihren SHA-256-Hash geprüft.
- **PLC-Meldungen** (nicht live getestet; [Referenz zu PLC-Meldungen und Textlisten](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_ALARMS_TEXTLISTS_SUMMARY.de.md)):
  - `update_text_list` / `delete_text_list`; Systemtextlisten werden abgelehnt.
  - `update_text_list_cells` / `update_alarm_text_cells` / `update_supervision_cells`: hashgeprüfte Zellenänderungen am XLSX-Export von TIA Portal. Sie werden unter `ExclusiveAccess` wieder importiert und durch einen erneuten Export geprüft. Jede nicht angeforderte Änderung lässt den Eintrag fehlschlagen.
  - `export_*` / `import_*` für Textlisten, Meldetexte, die gemeinsamen Meldeklassen, ProDiag-Überwachungen und die Systemdiagnose-Einstellungen des Projekts (`export_system_diagnostics_settings` / `import_system_diagnostics_settings`, projektweit, nicht live getestet; der Provider wird zur Laufzeit unter den Diensten des Projekts gefunden):
    - absolute Pfade auf einem Laufwerk oder einer UNC-Freigabe; nur Gerätepfade (`\\?\`, `\\.\`, `\??\`) werden abgelehnt,
    - kein Überschreiben ohne `overwrite:true`,
    - zugeordnete Netzlaufwerke sind erlaubt und werden gemeldet,
    - Importe werden durch einen erneuten Export geprüft, wo einer dokumentiert ist.
  - `set_prodiag_assignment` und `export_prodiag_info`.
  - Dialoge von TIA Portal beantwortet `portalConfirmations`, standardmäßig mit Ablehnen.
- **Technologieobjekte** (nicht live getestet; [Referenz zu Technologieobjekten](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/TECHNOLOGY_OBJECTS_SUMMARY.de.md)):
  - `create_technology_object` / `delete_technology_object` / `rename_technology_object`.
  - Gruppen: `create_technology_object_group` / `delete_technology_object_group` / `rename_technology_object_group`.
  - `set_technology_object_parameter`: in den Typ des aktuellen Werts umgewandelt und zurückgelesen.
  - `connect_technology_object_hardware` / `disconnect_technology_object_hardware`: dokumentierte `Connect`-Überladungen, geprüft durch Zurücklesen des Verbindungszustands und des angeforderten Ziels.
  - `export_technology_object` / `import_technology_object`: SimaticML-Dateien an lokalen Pfaden.
  - `update_technology_object_content`.
- **Ladbare Dateien** (nicht live getestet; [Referenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md#loadable-files-plc_write)): `plc_write` `generate_loadable` schreibt eine ladbare Datei für Bausteine (`blockPaths`) oder Software Units (`unitNames`) einer PLC, Ziel `Plc` oder `PlcSim` (`LoadableProvider.GenerateLoadable`, zur Laufzeit unter den Diensten der PLC-Software gefunden); die Datei folgt den Regeln für Exportdateien (kein Überschreiben ohne `overwrite:true`, neu gehasht).
- **Know-how-Schutz und Schreibschutz von Bausteinen** (nicht live getestet; [Referenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md#block-know-how-and-write-protection-plc_readplc_write)): `plc_read` `get_block_protection`; `plc_write` `set_block_know_how_protection`, `remove_block_know_how_protection`, `define_block_write_protection`, `set_block_write_protection`, `remove_block_write_protection`, `change_block_write_protection_password` (`PlcBlockProtectionProvider`, `PlcBlockWriteProtectionProvider`, zur Laufzeit unter den Diensten des Bausteins gefunden); Passwörter nur aus Passwortquellen (Umgebungsvariable, Windows-Anmeldeinformation, Zwecke `KnowHowProtection`/`BlockWriteProtection` der Passwortzuordnung), gegen die unzulässigen Zeichen von TIA Portal geprüft, verifiziert durch Rücklesen des Schutzzustands; F-Bausteine folgen der Regel der Safety-Anmeldung.
- **Projektsprachen und Projekttexte** (nicht live getestet; [Referenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md#project-languages-and-project-texts-plc_readplc_write)): `plc_read` `get_project_languages`; `plc_write` `set_project_languages` (Sprachen aktivieren und deaktivieren, Bearbeitungs- und Referenzsprache setzen, zurückgelesen), `export_project_texts` (XLSX für Übersetzer: Quell- und Zielsprachen, optional Textkategorien und Geräte) und `import_project_texts` (die übersetzte Datei, verifiziert durch einen erneuten Export, dessen Zeilen jede importierte Zeile zeigen müssen).
- **Weitere CPU-Einstellungen** (Issue #15; nicht live getestet; [Referenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_SETTINGS_SUMMARY.de.md)): `plc_read` `get_cpu_settings` und 20 `plc_write`-Operationen für Kontosperre, PLC-Passwortrichtlinien, Syslog (CPU- und Projektserver), Systemwebseiten, Standard-Webanwendung, Zugriff auf Beobachtungs-/Forcetabellen für Webserver und Display, das Display-Passwort (nur aus einer Passwortquelle), das Logo des Frontdisplays, die Projektsprache je Zeile, die IP-Erreichbarkeit und das Erzeugen der Web-DBs. Änderungen, die den Schutz senken, lösen `plc_lowers_cpu_protection` aus; jede Änderung wird zurückgelesen.
- **Webserver- und OPC-UA-Benutzer** (Issue #13; nicht live getestet; [Referenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_SETTINGS_SUMMARY.de.md#webserver--und-opc-ua-benutzer-issue-13)): `create_webserver_user`, `update_webserver_user`, `delete_webserver_user`, `update_simple_webserver_user`, `create_opcua_user`, `set_opcua_user_password`, `delete_opcua_user`; Passwörter nur aus Passwortquellen (`passwordVariable`), Benutzer und Rechte lesen mit `get_cpu_settings`; wirksam nach einem Laden.
- **UMAC: Rollen, Projektbenutzer, Funktionsrechte, Projektschutz** (Issue #12; nicht live getestet; [Referenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_SETTINGS_SUMMARY.de.md#umac-rollen-projektbenutzer-funktionsrechte-projektschutz-issue-12)): `plc_read` `get_umac` (ob das Projekt geschützt ist, Rollen, Benutzer, Rechte je PLC) und 19 `plc_write`-Operationen für eigene Rollen, Engineering- und Geräte-Funktionsrechte, Projektbenutzer (Passwörter nur aus Passwortquellen), Rollenzuweisungen, den anonymen Benutzer, UMC-Benutzer und die UMAC-Passwortrichtlinie; jeder Eintrag löst den Info-Guard `plc_changes_project_access` aus. Das Öffnen eines UMAC-geschützten Projekts mit Anmeldedaten und Online-Anmeldungen über UMAC gehören nicht dazu (siehe Referenz).
- **CPU-Zertifikate** (Issue #14; nicht live getestet; [Referenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_SETTINGS_SUMMARY.de.md#cpu-zertifikate-und-sichere-kommunikation-issue-14)): `plc_read` `get_cpu_certificates` listet die Zertifikate der CPU mit ihren SHA-256-Fingerabdrücken, den Werten, die die TLS-Abfragen von `plc_download`/`plc_tia_online`/`plc_station_upload` für `trustedCertificates` zeigen; 11 `plc_write`-Operationen erzeugen, importieren, exportieren, löschen und ordnen Zertifikate zu und setzen die dynamische Zertifikatsverwaltung. Nichts wird automatisch vertraut, Kommunikation ohne TLS wird nie eingeschaltet.
- **Safety-Ausdruck** (Issue #21; nicht live getestet; [Referenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_SETTINGS_SUMMARY.de.md#safety-ausdruck-issue-21)): `plc_write` `generate_safety_printout` schreibt den Safety-Ausdruck der F-CPU (`SafetyPrintout.Print`, PDF- oder XPS-Druckertreiber, Layout, `All`/`Compact`) in eine Datei. Er ist eine Unterlage für die Safety-Abnahme durch eine befähigte Person; der MCP ersetzt diese Abnahme nie.
- **Safety Validation Assistant** (Issue #25; nicht live getestet; [Referenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_SETTINGS_SUMMARY.de.md#safety-validation-assistant-aktivierungstests-und-testbericht-issue-25)): `get_safety_validation`, `check_activation_test` und 14 `plc_write`-Operationen bereiten Aktivierungstests, Sicherheitsfunktionen und Bedingungen vor, exportieren Tests und erzeugen den Testbericht. Der MCP trägt nie ein Testergebnis ein (kein `TestState`, keine Importe oder Kopien, die Ergebnisse mitbringen könnten); jede Änderung löst den Bestätigungs-Guard `plc_safety_acceptance_required` aus.
- **OPC-UA-Server-Schnittstellen** (nicht live getestet; [Referenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md#opc-ua-server-interfaces-and-access-rules-plc_read-plc_write)): `plc_read` `get_opcua_configuration` liest Server-Schnittstellen (Companion Specifications, eigene Schnittstellen), SIMATIC-Schnittstellen, Referenz-Namensräume, Zugriffsbeschränkungen der Namensräume und Rollenzuordnungen jeder PLC; `plc_write` legt sie an, löscht, exportiert, importiert und aktiviert sie und pflegt Beschränkungen, Rollen und Namensraum-Berechtigungen (`OpcUaProvider`, zur Laufzeit unter den Diensten der PLC-Software gefunden).
- **Laden in einen Ordner, Master-Secret online, R/H-Download** (nicht live getestet; [Referenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md#download-to-a-folder-and-the-online-master-secret-plc_write)): `plc_write` `download_to_folder` schreibt die Ladedaten einer PLC (oder eines R/H-Systems) ohne Gerät in einen Windows-Ordner (`DownloadProvider.Download(DirectoryInfo, ...)`, Dateien neu gehasht); `set_plc_master_secret_online`/`reset_plc_master_secret_online` setzen oder löschen das Master-Secret der Online-PLC (`OnlineProvider.SetPlcMasterSecret`/`ResetPlcMasterSecret`; Secret nur aus einer Passwortquelle; Acknowledge-Guard, read-write fragt bei jedem Aufruf; nicht zurücklesbar, daher notVerified); `plc_download` `rhTarget` lädt in die Primär- oder Backup-CPU eines R/H-Systems, unter den bestehenden Download-Guards und der Bestätigung. Benutzerverwaltungsdaten beim Laden sind die Entscheidung `UserManagementDownload`. Paket G ergänzt `load_snapshot_as_actual_values` / `load_start_values_as_actual_values` (Istwerte eines DB in der Online-PLC, Acknowledge-Guard, Rückfrage in read-write).
- **Vervollständigung der V21-API (Paket G)** (nicht live getestet; [Referenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/V21_API_COMPLETION_SUMMARY.de.md)): die dokumentierten V21-Openness-Methoden, die ein Namensvergleich mit der API-Beschreibung als ungenutzt fand, in den bestehenden Tools: Multiuser-Markierungen (`get_markings`, `get_mark_state`, `mark_objects`, `unmark_objects`), `protect_project`, UMC-Benutzer und -Gruppen (`create_offline_umc_user(_group)`, `import_umc_user`, `get_umc_server_user`, `check_umc_consistency`), Engineering-Objekte über Pfade (`get_object_identifier`, `find_object_by_identifier`, `get_creation_infos`, `get_linked_tags`), Kurvenscheiben (`save_cam_data`, `load_cam_data`), Istwerte eines Online-DB (`load_snapshot_as_actual_values`, `load_start_values_as_actual_values`), Hardware-Funktionen in `network_read`/`network_write` (Subnetze eines Knotens, IO-Controller, IM-Verbindung, PC-Schnittstellenzuordnung, Baugruppenbeschreibung, Fernwirk-Datenpunkte, Kommunikationsverbindungen) und `hmi_write` `resize_screen`, `set_log_duration`, `set_opcua_alarm_type_node`; die Liste der Auslassungen nennt jede nicht gebaute Methode mit Grund.
- **Offline-Änderungserkennung** (nicht live getestet; [Referenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md#offline-change-detection-plc_read)): `plc_read` `get_plc_checksums` liest die Prüfsummen von Software und Textlisten jeder PLC (`PlcChecksumProvider`; leer heißt nicht übersetzt oder seit dem letzten Übersetzen geändert), `get_fingerprints` die Fingerprints eines Bausteins oder PLC-Datentyps (`FingerprintProvider.GetFingerprints()`). Die Provider werden zur Laufzeit unter den Diensten des Objekts gefunden; Werte werden als Text mit ihrem Laufzeittyp gemeldet.
- **Übersetzungseinstellungen des Projekts** (nicht live getestet; [Referenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md#project-block-compilation-settings)): `set_project_compilation_settings` setzt „Simulation während Bausteinübersetzung unterstützen“ (`simulationDuringBlockCompilation`, laut Siemens nötig vor einem Softbus-Download nach PLCSIM Advanced) und die Unterstützung virtueller PLCs (`virtualPlcDuringBlockCompilation`) über dieselben Dienste, die `get_project_status` liest. Projektweit, ohne `plcName`, nur Einzelplatzprojekte; das Siemens-Member wird zur Laufzeit gebunden (`api_member_missing`, wenn weder Eigenschaft noch beschreibbares Attribut existiert) und zurückgelesen.
- **CPU-Schutz, auch für passwortgeschützte Projekte** (nicht live getestet; [Referenz zum CPU-Schutz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_SECURITY_SUMMARY.de.md)): ändert das Offline-Projekt, wirksam nach `plc_download`. Passwörter kommen nur aus Passwortquellen (Umgebungsvariable, Windows-Anmeldeinformation, Zuordnungsdatei), nie als Werte; der Server erzeugt keine.
  - `set_plc_access_level` setzt jede Stufe des TIA-Portal-Dialogs: `FullAccessIncludingFailsafe`, `FullAccess`, `ReadAccess`, `HMIAccess`, `NoAccess`. Die Passwörter der offeneren Stufen kommen aus `accessLevelPasswords` (zum Beispiel `{ "FullAccess": { "credential": "TiaMcp/Projekt/PLC_1/Vollzugriff" } }`) oder aus der Zuordnungsdatei (Zweck = Name der Stufe); `resetAccessLevelPasswords` entfernt Stufenpasswörter.
  - `protect_plc_confidential_data` schützt „Vertrauliche PLC-Konfigurationsdaten“ mit einem Passwort (`Protect`, mit `allConfiguration` zusätzlich alle ladbaren Konfigurationsdaten), `change_plc_confidential_data_password` ändert es, `disable_plc_confidential_data_protection` schaltet den Schutz ab (`Unprotect` mit oder ohne Passwort). Zweck in der Zuordnungsdatei: `PlcMasterSecretPassword`.
  - Schwächerer Schutz löst den Info-Wächter `plc_lowers_cpu_protection` aus, stärkerer `plc_raises_cpu_protection`. Fehlt ein Siemens-Member, schlägt der Eintrag mit `api_member_missing` fehl, ohne etwas zu ändern.
  - `plc_download`, `plc_tia_online`, `plc_station_upload` und jetzt auch `compare_to_online` (`passwords`) beantworten jede Passwortabfrage von TIA Portal (Modul-Lese-/Schreibzugriff, `PlcMasterSecretPassword`, Online-Lesezugriff) aus Passwortquellen und der Zuordnungsdatei.
- **Fehlersichere Steuerungen (F-CPU)** (teilweise live getestet am 2. 11. 2026, siehe [Live-Bericht vom 2. 11. 2026](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/superpowers/acceptance/reports/2026-11-02-simatic-sd-and-safety-live.md); [Referenz zu F-Programmen](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_SAFETY_SUMMARY.de.md)):
  - `safety_login` / `safety_logoff`, `set_safety_password` / `revoke_safety_password`; `passwordVariable` nennt immer nur die Quelle des Passworts (Umgebungsvariable oder Eintrag der Windows-Anmeldeinformationsverwaltung, siehe [Passwörter](#passwörter-umgebungsvariable-oder-windows-anmeldeinformationsverwaltung)), nie das Passwort.
  - Der Server ist nicht strenger als TIA Portal: F-Bausteine lassen sich mit allen Operationen ändern, die TIA Portal dafür anbietet (`update_block_logic` als SimaticML, Quelle oder SIMATIC SD, die SD-Netzwerk-Operationen und `create_block_from_document`, Schnittstellenbearbeitung, `update_db_member`, `delete_block`, `rename_block`, `renumber_block`, `copy_block`, `move_block`). Eine Anmeldung (`safety_login`) ist nur nötig, wenn das Sicherheitsprogramm ein Passwort hat; ohne Passwort erlaubt TIA Portal die Änderung ohne Anmeldung, der Server ebenso. Was der Server nicht beurteilen kann (keine SafetyAdministration, Zustand nicht lesbar), entscheidet TIA Portal (Info-Wächter `plc_safety_tia_decides`); lehnt TIA Portal ab, scheitert die Operation mit dessen Meldung.
  - `create_safety_runtime_group` legt eine F-Ablaufgruppe an (gleiche Anmelderegel); `set_safety_runtime_group` weist einer bestehenden F-Ablaufgruppe den Main-Safety-Baustein (und bei einem F-FB dessen Instanz-DB) zu. Das Openness-Mitglied dafür ist nicht dokumentiert und wird zur Laufzeit gebunden; live auf V21 (2. 11. 2026) gab es weder die Eigenschaft noch das Attribut `MainSafetyBlock` (`api_member_missing`). Vor dem Aufgeben versucht der Server `SetAttribute("MainSafetyBlockName", Name)`, aber nur, wenn `GetAttributeInfos()` dieses Attribut als beschreibbar meldet, und nennt in der Fehlermeldung alle gemeldeten Attribute mit Zugriffsart (offline geprüft). Reparatur sonst in TIA Portal (Safety Administration).
  - Live am 2. 11. 2026 (zweimal, auch mit `b79b2df`): Jeder SIMATIC-SD-Import des Main-Safety-Bausteins entfernt den Verweis der F-Ablaufgruppe (`MainSafetyBlockName` leer, die PLC übersetzt nicht mehr); der SimaticML-Import behält ihn. Deshalb importiert der Server ein SD-Dokument nie direkt auf den Main-Safety-Baustein (`update_block_logic` sd, `insert_network`/`replace_network`/`delete_network`, `create_block_from_document` mit `overwrite`), sondern in einem Schritt: als temporären F-Baustein `zzMcpTmp_<Baustein>_<8 Hex>` in dessen Gruppe, übersetzt ihn, exportiert ihn als SimaticML, setzt Name, Nummer und AutoNumber des Main-Safety-Bausteins ein, importiert das als SimaticML auf den Main-Safety-Baustein und löscht den temporären Baustein. Übersetzt der temporäre Baustein nicht, bleibt der Main-Safety-Baustein unverändert und die Operation scheitert mit den Compiler-Meldungen. Das Ergebnis nennt den Weg (`mainSafetyBlockRoute`, `route: "sdViaXmlForMainSafetyBlock"`). Live von Hand bewiesen, automatisch nur offline geprüft. Nach jedem Import prüft der Server die F-Ablaufgruppen weiterhin (`safetyRuntimeGroup`); Info-Wächter `plc_import_may_unlink_main_safety_block`.
  - `list_safety_blocks` scheitert nicht mehr, wenn ein Baustein `SafetySignatureProvider` anbietet, `GetService` ihn aber nicht liefert (live gesehen, Defekt D13): die Signaturen dieses Bausteins heißen dann `unreadable`.
  - Jede F-Änderung trägt den Wächter `plc_safety_acceptance_required`: in read-write fragt der Server einmal nach, in full gilt er per Richtlinie. Ein Sicherheitsprogramm muss von einer befähigten Person validiert und abgenommen werden; der MCP-Server ersetzt die Sicherheitsabnahme nie.
  - `plc_download` beantwortet die F-Programm-Abfrage `SafetyProgram` für PLCSIM und für echte F-CPUs. Zu echter Hardware gilt sie wie jeder Hardware-Download (Wächter `download_to_hardware`) und trägt zusätzlich den Wächter `download_includes_safety_program` mit dem Abnahmehinweis: read-write fragt bei jedem Aufruf nach, full läuft per Richtlinie.

**Ablauf:**
- `dryRun:true` zeigt Auswirkungen und Wächter, ohne etwas zu ändern. In keinem Modus wird nachgefragt.
- Die Phasen heißen `preview`, `applied`, `blocked` und `error`.
- Die Ausführung stoppt beim ersten Fehler und wird nie zurückgenommen. Vor einer Wiederholung mit `plc_read` neu lesen.
- Spätere Einträge dürfen Objekte nutzen, die frühere Einträge desselben Aufrufs angelegt haben.

**Namenskollisionen:**
- Beim Anlegen wird vor der Änderung nicht auf Kollisionen geprüft. Ausnahme sind Technologieobjekte und ihre Gruppen; dort werden vorhandene Namen abgelehnt.
- Live-Fälle im abgesteckten Umfang zeigten:
  - die Ablehnung doppelter Variablen, Tabellen, Bausteine und Gruppen,
  - die Ablehnung, die Standard-Variablentabelle zu löschen,
  - vier weitere Namensraum-Richtungen.
- Das ist keine Garantie für jede Kollision.

**Fehler, Wächter und Verifikation:**
- Eine native `EngineeringTargetInvocationException` in einer ändernden Worker-Methode wird zu `engineering_rejected`. Typ, begrenzte Meldung und innere Kette stehen in `engineeringError`, in der Sprache der Portal-Oberfläche.
- Wächter decken die Folgen von Löschungen, Adressüberschneidungen und nicht lesbare Belege ab.
- Die Prüfung auf einen einzigen OB bleibt erhalten, weil die native Absicherung nicht nachgewiesen wurde.
- Die Verifikation liest nur Inhalte zurück (`contentHash`), sonst `trusted` (Schreibvertrag `2.0`). Siehe die [PLC-Referenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md#failures-from-tia-portal).

**Inhaltsänderungen** (`update_block_logic`, `update_type_content`):
- Sie erhalten das Dokument als `content` zusammen mit `expectedContentHash`, dem `contentHash` aus einem `plc_read` desselben Objekts im selben Format.
- Wurde der Inhalt seit diesem Lesen geändert, wird mit `state_changed` abgelehnt.
- Ein Dokument über dem Wertebudget wird beim Lesen weggelassen, hat keinen Hash und kann nicht über `plc_write` geändert werden.
- Die Budgets sind standardmäßig 60.000 Zeichen pro Wert und 180.000 pro Antwort. Sie lassen sich beim Start mit `--max-item-chars`/`--max-document-chars` erhöhen (siehe [Antwortgrößen](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/guides/mcp-client-configuration.md#response-size-limits)).
- `preview_write_batch`, `apply_write_batch` und `execute_read_batch` wurden entfernt; ihre Operationen liegen jetzt in `plc_write` und `plc_read`.

**Warnungen:** Jedes Operationsergebnis kann ein Array `warnings` tragen. Es enthält nicht fatale Hinweise auf Einschränkungen aus dem TIA-Openness-Worker. Ein gefülltes `warnings` bedeutet, dass das Ergebnis unvollständig sein kann.

**Formate und Hashes beim Lesen:**
- Verfügbare `plc_read`-Operationen dieser Gruppe: `get_block_content`, `get_type_content`, `list_tag_tables`, `list_download_targets`, `get_block_interface`, `get_type_interface`, `get_db_values` und `get_db_snapshot`.
- `get_block_content` / `update_block_logic` und `get_type_content` / `update_type_content` nehmen ein Feld `format` an.
  - `format=source` gibt es für globale Datenbausteine, PLC-Datentypen und FB/FC/OB in SCL.
  - `format=sd` (nur Bausteine) liest und schreibt die SIMATIC-SD-Dokumente `.s7dcl`/`.s7res` (`ExportAsDocuments` / `ImportFromDocuments`); was TIA Portal nicht exportiert, scheitert mit dessen Meldung (live getestet am 2. 11. 2026; ein Baustein ohne Texte liefert nur die `.s7dcl`).
  - Alle anderen Bausteinsprachen bleiben bei `format=xml`.
- `withDependencies` (nur `plc_read`, Standard `false`) lässt TIA Portal die Abhängigkeiten des Objekts mitliefern.
  - Das Ergebnis deklariert mehrere Objekte und dient **nur als Kontext**.
  - Ein Schreibzugriff lehnt jede Quelle mit mehr als einem Objekt ab, und das Lesen trägt eine entsprechende Warnung.
  - Ohne das Feld erhält man ein Dokument, das man bearbeiten und zurückschreiben kann.
- `get_block_content` und `get_type_content` liefern zusätzlich einen `contentHash` (`xml:sha256:<hex>`, `source:sha256:<hex>` oder `sd:sha256:<hex>`).
  - Er wird über genau den Text in `result` berechnet und mit dem gelieferten Format gekennzeichnet.
  - So erkennt ein späterer abgesicherter Schreibzugriff, ob sich das Dokument seit dem Lesen geändert hat.
  - Er fehlt bei Lesen mit `withDependencies`, bei fehlgeschlagenem Lesen und bei Ergebnissen, die wegen ihrer Größe gekürzt oder weggelassen wurden.

### Netzwerkoperationen

`network_read` und `network_write` deklarieren beide ein MCP-Ausgabeschema. Sie liefern ein kanonisches JSON-Dokument, identisch als `content`-Textblock und als `structuredContent`, nie als verschachtelten JSON-String in einer äußeren Hülle. `network_read` nutzt Vertragsversion `1.0`, `network_write` `2.0`; beide haben Warnungs-Arrays an der Wurzel und explizite Nullwerte. Die genauen Hüllen beschreibt [docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.de.md).

- `network_read` führt bis zu 50 Netzwerk-Leseoperationen aus. Die Leseoperationen laufen unabhängig voneinander.
  - Operationen: `read_hardware_config`, `search_equipment_catalog`, `list_network_objects`, `inspect_network_object` (auch jeder Hardwareparameter eines Geräteelements).
  - Hardwaremodule: `list_plug_locations`, `find_module_types`, `find_container_types`, `list_io_addresses`, `get_custom_identity` (ein benutzerdefinierter Schlüssel an einem Gerät oder Geräteelement; nicht live getestet) `list_port_connections` (die PROFINET-Portverbindungen eines Geräts oder Geräteelements; nicht live getestet) und `list_transfer_areas` (die Transferbereiche einer Schnittstelle; nicht live getestet).
  - Katalogsuchen lassen sich mit `query` und `maxResults` begrenzen.
  - `read_hardware_config` nimmt einen optionalen Filter `deviceName` und optional `plcName` für den Variablenabgleich an.
  - Strukturierte E/A-Daten gibt es nur auf Anforderung: `includeIoDetails` für Adressen und Kanäle, `includeTagMatches` für exakte PLC-Variablen je Kanal. Ausführliche E/A-Ausgaben können groß werden; deshalb nach `deviceName` filtern oder auf mehrere Aufrufe aufteilen.
- `network_write` zeigt bis zu 50 Netzwerk-Schreibzugriffe in der Vorschau oder führt sie aus.
  - `add_network_device`: flach `typeIdentifier`/`deviceName`, weil es etwas benennt, das noch nicht existiert.
  - `configure_network_device`: verschachtelt `target: { deviceName, nodeId }` plus `changes: { ipAddress?, subnetMask?, pnDeviceNameAutoGeneration?, pnDeviceName?, subnet?: { subnetId }, ioSystem?: { subnetId, number } }`. Ein Nullwert in `changes` lässt diese Einstellung unverändert.
  - `create_subnet`: `subnet: { name, networkType }`, nur bei PROFIBUS zusätzlich `highestAddress`/`transmissionSpeed`.
  - `update_subnet`: `target: { kind: "subnet", subnetId }` plus `subnetChanges` mit mindestens einem Element.
  - `delete_subnet`: `target: { kind: "subnet", subnetId }`, verbunden oder nicht.
  - `create_io_system` (nicht live getestet): legt am PROFINET-Knoten einer CPU ein PROFINET-IO-System an, `target: { kind: "node", deviceName, nodeId, interfacePath }` wie `read_hardware_config` ihn meldet, optional `ioSystem: { name }`. `subnetId` und `number` aus dem Ergebnis nimmt `configure_network_device` `changes.ioSystem` im nächsten Aufruf, um ein IO-Device (z. B. ET 200SP) zuzuordnen.
  - Hardwaremodule (nicht live getestet; siehe die [Referenz zu Hardwaremodulen](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/HARDWARE_MODULES_SUMMARY.de.md)): `plug_device_item`, `move_device_item`, `copy_device_item`, `delete_device_item`, `change_device_item_type`, `set_hardware_attributes`, `set_io_address`, `set_custom_identity`, `remove_custom_identity` (Custom Identities, mit `Get` zurückgelesen), `connect_ports`, `disconnect_ports` (PROFINET-Topologie, aus `ConnectedPorts` zurückgelesen), `create_transfer_area`, `update_transfer_area`, `delete_transfer_area`, `delete_io_system`, `disconnect_from_io_system` (Issue #11; V21 hat kein eigenes Member zum Anlegen eines DP-Mastersystems, `create_io_system` an einem DP-Master-Knoten lässt TIA Portal entscheiden), `export_cax`, `import_cax` (CAx-Daten als AutomationML, Issue #9) und `export_io_map`.
  - **Ausführung:**
    - `dryRun:true` zeigt eine Vorschau; `dryRun:false` oder ein fehlendes `dryRun` führt aus, ohne Server-Nachfrage in read-write/full.
    - Veraltete Felder `confirm` und `acknowledge`, unbekannte Wurzelelemente und ein nicht boolesches `dryRun` werden vor dem Werkzeugaufruf abgelehnt.
    - Schreibzugriffe brauchen eine geprüfte Bindung an genau das bereits geöffnete Projekt.
    - Sie laufen nacheinander und stoppen beim ersten Fehler; spätere Einträge werden als übersprungen markiert, erledigte Schreibzugriffe nicht zurückgenommen.
    - Der fehlgeschlagene Eintrag trägt eine Warnung: Diese und jede frühere Operation desselben Aufrufs können den TIA-Zustand schon verändert haben. Vor einer Wiederholung mit `network_read` neu lesen, statt den Stapel blind erneut auszuführen.

`configure_network_device` wählt genau ein Gerät mit genau einem Knoten und genau ein Subnetz-/IO-Tupel aus, ohne Rückfall auf den ersten Treffer.
- Angewendete Einstellungen werden sofort und am Ende geprüft; nicht lesbare Belege können nicht bestehen.
- Angeforderte Auslassungen lassen den Eintrag fehlschlagen, die typisierten Teilergebnisse bleiben erhalten.
- Das Löschen verbundener Subnetze ist nur ein Hinweis. Ein unvollständiger Bestand der Folgen wird gemeldet (`network_state_unverifiable`, informierend); der Eintrag läuft, TIA Portal entscheidet.
- Die Ergebnisse tragen `phase:applied`, auch wenn Ausführung oder Verifikation fehlschlagen (`error:null`, MCP `isError:false`).
- Vor einer Wiederholung den aktuellen Zustand prüfen. Ein Weglassen in der Ausgabe kann `success:false` für die Auslieferung melden, während die tatsächliche Ausführung erfolgreich war.
- Wächter, Teilergebnisse und Wiederherstellung nach Weglassungen beschreibt der [Netzwerkvertrag](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md#network_write-envelope).

`read_hardware_config` meldet nicht lesbare Elemente zusätzlich in einem Array `messages`.
- Namens- und Typkennungsfelder von Geräten und Modulen lassen nicht lesbare Werte weg, statt Platzhalter wie `0` oder eine leere Zeichenkette zu liefern.
- Einige untergeordnete Namensfelder fallen weiterhin auf eine leere Zeichenkette zurück; der Fehler steht dann in `messages`.
- Hardwarekonfigurationsdaten sind Engineering-Belege, keine Bestätigung einer in Betrieb genommenen Anlage.

`read_hardware_config` kann auf Anforderung strukturierte E/A-Daten liefern.
- Mit `includeIoDetails: true` trägt jedes Geräteelement ein Objekt `ioDetails`:
  - `addresses[]` mit Openness-`Address`-Belegen: `ioType`, byte-basiertes `startAddress`, `length`, dynamischer `context` und geordnete `controllerNames`.
  - `channels[]` mit `number`, `ioType`, `type`, bit-basiertem `channelAddressBits`, `channelWidthBits` und einer formatierten `logicalAddress` wie `%I4.0`/`%IW64`. Diese erscheint nur, wenn die Belege vorhanden und ausgerichtet sind.
- Mit `includeTagMatches: true` (setzt `includeIoDetails` voraus) trägt jeder Kanal `tagMatches[]`:
  - Sie werden eindeutig gegen die Variablentabellen genau einer PLC aufgelöst.
  - `plcName` wählt die PLC über den exakten Namen; sonst gibt es den Abgleich nur, wenn genau eine PLC existiert.
  - Eine Variable passt nur zu einem Kanal, wenn ihr normiertes absolutes E/A-Intervall mit dem des Kanals übereinstimmt. Es wird nie über Steuerungen hinweg abgeglichen.
- Ein normales Lesen ohne Schalter liefert keine `ioDetails`.
- Genaue Anfrage- und Antwortformen sowie die Einheiten beschreibt [docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.de.md).

Große Hardware-Lesezugriffe können mit `pageSize` (`1..200`) oder `cursor` blättern.
- Die Seiten zählen erst Geräte, dann Subnetze in einer festen Reihenfolge; die beiden öffentlichen Arrays bleiben getrennt.
- Wegen der kanonischen Größenbegrenzung kann eine Seite weniger vollständige Einträge liefern als angefordert.
- `pagination.nextCursor` folgen, bis er fehlt oder null ist. Projekt, Filter und Detailschalter dabei nicht ändern.
- Anfragen ohne beide Felder nutzen den Pfad ohne Blättern.
- Cursor gelten nur im laufenden Prozess und überstehen keinen Neustart des Hosts.
- Wiederherstellung und Weglassungen beschreibt die [Netzwerkreferenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md#hardware-configuration-pagination).

### Projektwerkzeuge

- `bind_project` übernimmt in jedem Modus mit der Standardaktion `bind` ein bereits geöffnetes Projekt.
  - Ohne Pfad wird das einzige geöffnete Projekt übernommen oder die Kandidaten aufgelistet. Zum Wechseln `forceRebind:true` setzen.
  - Das ausdrückliche `list_portals` sucht, ohne zu übernehmen.
  - Fünf Inventuraktionen für den Project Server untersuchen die dauerhaft angebundene Instanz, ohne die Projektbindung zu ändern.
  - Ein erneutes Anbinden kann den Openness-Zugriffsdialog von TIA zeigen, den ein Mensch beantworten muss.
  - Siehe [Multiuser-Inventur und Grenzen der Abnahme](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/MULTIUSER_OPERATIONS_SUMMARY.de.md).
- `project_server_write(operations, dryRun=false, portalProcessId)` in read-write/full (nicht live getestet): die Project-Server-Verwaltung (Multiuser) einer TIA-Portal-Instanz, ohne gebundenes Projekt.
  - `create_server_connection`, `set_server_connection` (Host, Port, Protokoll), `delete_server_connection`, `add_project_to_server` (Wurzel oder Gruppe), `create_local_session` (danach mit `open_project` und der gemeldeten `.als21` binden), `delete_local_session_from_server`.
  - Das Löschen einer lokalen Sitzung oder einer Verbindung und das Hochladen eines Projekts sind acknowledge-Guards: read-write fragt einmal pro Aufruf, full läuft per Richtlinie. Kein Element nimmt ein Passwort. Der Aufrufer nennt jeden Server; nie auf einem Server verwenden, den der Benutzer nicht genannt hat.
  - Rückgelesen über das `bind_project`-Inventar; siehe die [Project-Server-Referenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PROJECT_SERVER_WRITE_SUMMARY.de.md).
- `get_project_status` zeigt die Metadaten des aktiven Projekts, ohne Projekte zu öffnen, zu binden oder zu wechseln; es nimmt keine Argumente.
- `browse_project_tree` durchsucht einen kanonischen, geblätterten Schnappschuss des Projektbaums (v3).
  - Optional mit typisierten Kopfdaten von PLC-Bausteinen in den `details` der Bausteinknoten: Autor, Version, Familie und Kopfname. Standardmäßig aktiv sind die Felder `HeaderAuthor`, `HeaderVersion`, `HeaderFamily` und `HeaderName`.
  - Parameter: typisierter `startSelector`, `depth` und `pageSize`; weiter geht es mit dem zurückgegebenen undurchsichtigen `cursor`.
- `compile_check` übersetzt und liefert die Compiler-Meldungen; verfügbar in read-write und full.
  - `scope` `software` (Standard) übersetzt nur das PLC-Programm (oder mit `blockPath` einen Baustein), **nicht** die Hardwarekonfiguration. Fehler, die erst beim Laden auffallen (z. B. Schutzstufe ohne Passwort), bleiben dabei unsichtbar.
  - `scope` `hardware` übersetzt die Hardwarekonfiguration der CPU jeder PLC (Bericht mit `scope` `hardware`). Vor `plc_download` beides ausführen.
- `plc_download` lädt die PLC des gebundenen eigenständigen Projekts in eine PLC-Simulation (PC-Schnittstelle `PLCSIM`) oder in eine echte Steuerung (jede andere PC-Schnittstelle). Nicht live getestet; siehe die [Download-Referenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_DOWNLOAD_SUMMARY.de.md).
  - **Zugriffsmodus:** Nur in read-write und full; der Zugriffsmodus ist die Steuerung durch den Menschen.
    - read-write fragt einmal pro tatsächlichem Aufruf nach; die Nachfrage sagt, wenn das Ziel echte Hardware ist.
    - full läuft nach Richtlinie.
    - `dryRun:true` zeigt eine Vorschau, ohne das Ziel zu berühren.
  - **Wächter:** Es gibt keinen blockierenden Wächter; zu bestätigen sind `download_to_hardware` und `download_stops_plc`.
  - **Pfad:** Modus, PC-Schnittstelle `{ name, number }` und Zielschnittstelle werden exakt aus `plc_read` `list_download_targets` aufgelöst, ohne Rückfall.
  - **Ladekonfigurationen** von TIA Portal werden über ihren exakten Typ erkannt und beantwortet:
    - durch eingebaute Vorgaben (`TargetForSoftware` ist auf PLCSIM `PlcSimulationAdvanced`, sonst `CPU`),
    - durch `decisions` des Aufrufers (Auswahlname oder `true`, nach Kurzname des Typs),
    - durch `passwords` (Namen von Umgebungsvariablen oder Windows-Anmeldeinformationen, nie das Passwort selbst; siehe [Passwörter](#passwörter-umgebungsvariable-oder-windows-anmeldeinformationsverwaltung)).
    - Eine unbeantwortete Konfiguration bricht ab und nennt, was eine Wiederholung braucht.
  - **TLS-Zertifikat** (nicht live getestet; [Download-Referenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_DOWNLOAD_SUMMARY.md#tls-certificate-trust-online-legitimation)):
    - Fragt TIA Portal beim Verbindungsaufbau über `OnlineLegitimation` (Phase `online_legitimation`), ob dem Zertifikat des Geräts vertraut werden soll (`TlsVerificationConfiguration`), wird das nur mit `trustedCertificates` bejaht.
    - `trustedCertificates` sind SHA-256-Fingerabdrücke, die der Mensch geprüft hat: je 64 Hex-Zeichen, Groß- und Kleinschreibung egal, Doppelpunkte und Leerzeichen werden entfernt, höchstens 16.
    - `Trusted` nur, wenn jedes vorgelegte Zertifikat aufgeführt ist; sonst `NonTrusted` und Abbruch mit Belegen in `abort.tls` (seit K27 ohne Ausnahme im Callback von TIA Portal: live am 2026-11-02 beendete sich TIA Portal, als dieser Callback eine Ausnahme warf; nicht live getestet) (je Zertifikat Fingerabdruck, Subject, Issuer, gültig von/bis; dazu `plcName` und `verificationInfo`).
    - Die Fingerabdrücke vergleicht der Mensch zum Beispiel mit „Zertifikate anzeigen“ in TIA Portal; erst dann mit `trustedCertificates` wiederholen. Die read-write-Nachfrage listet die Fingerabdrücke.
    - Kein Standardvertrauen, kein „allem vertrauen“, `EnableLegacyCommunication` wird nie gesetzt; ein `decisions`-Eintrag kann `Trusted` nicht wählen.
  - **Bestätigungsdialoge:**
    - Dialoge, die während des Aufrufs erscheinen (bis `Download` zurückkehrt und die Belege eingesammelt sind), werden mit Nein beantwortet und lassen den Aufruf fehlschlagen. Mit `portalConfirmations:"accept"` werden sie mit Ja beantwortet. In beiden Fällen werden sie festgehalten.
    - Ein Dialog danach, etwa nach einer frühen Rückkehr von `Download`, behält die Antwort des Aufrufs bis zum Beginn der nächsten Worker-Anfrage. Er wird dann als Warnung über eine späte Bestätigung gemeldet (und im Server-Log), nicht in den Belegen des Downloads.
  - `completionVerified` ist immer false.
- `plc_tia_online`: `go_online` / `go_offline` für eine PLC des gebundenen eigenständigen Projekts. Nur in read-write und full. Nicht live getestet; siehe die [PLC-Online-Referenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_ONLINE_OPENNESS_SUMMARY.de.md).
  - `go_online` nimmt den exakten Pfad (Modus, erforderliche PC-Schnittstelle `{ name, number }`, Zielschnittstelle) aus `plc_read` `get_online_state`.
  - Es wendet ihn auf denselben Online-Provider an und fragt den Online-Zustand bis `timeoutSeconds` ab.
  - Online-Passwörter kommen nur aus benannten Umgebungsvariablen. Eine TLS-Zertifikatsprüfung wird nur über `trustedCertificates` bejaht, wie bei `plc_download`. Jede andere Legitimierung (etwa Authentifizierung) bricht mit Belegen ab.
  - Bestätigungsdialoge von TIA Portal werden wie bei `plc_download` behandelt.
  - Das sind nicht die S7-1500-Web-API-Werkzeuge (`plc_online_*`).
  - read-write fragt einmal pro tatsächlichem Aufruf nach; full läuft nach Richtlinie.
- `plc_station_upload` lädt eine Station von einer Geräteadresse in das Offline-Projekt. Das ist eine Projektänderung; nur in read-write und full, eine Nachfrage pro tatsächlichem read-write-Aufruf. Nicht live getestet; siehe die [PLC-Online-Referenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_ONLINE_OPENNESS_SUMMARY.md#plc_station_upload).
  - Upload-Konfigurationen werden nur durch `decisions` des Aufrufers (Kurzname des Typs auf Name des Enum-Elements) und `passwords` (Namen von Umgebungsvariablen) beantwortet.
  - Eine unbeantwortete Konfiguration bricht ab und listet ihre verfügbaren Auswahlen.
  - Die Online-Legitimierung der Verbindung (Online-Passwörter, TLS-Zertifikat über `trustedCertificates`) wird wie bei `plc_tia_online` beantwortet.
  - `portalConfirmations` funktioniert wie bei `plc_tia_online`.
- `open_project` / `create_project` / `retrieve_project` / `save_project` / `save_project_as` / `archive_project` / `close_project` sind abgesicherte Lebenszyklus-Schreibzugriffe mit genau einem Aufruf, in read-write und full. `retrieve_project` (nicht live getestet) dearchiviert ein Projektarchiv in ein Verzeichnis (`upgrade:true` für `RetrieveWithUpgrade`) und bindet an das dearchivierte Projekt.
  - read-write fragt einmal pro tatsächlichem Aufruf nach; full läuft ohne Server-Nachfrage.
  - `dryRun:true` zeigt Auswirkungen und Wächter ohne Änderung und ohne Nachfrage.
  - Diese Werkzeuge bieten strukturierte Ausgaben an. Sie nehmen keine öffentliche Bestätigungsliste und kein Sicherheits-Token an und bleiben Einzelwerkzeuge.

**Lokale Sitzungen (Multiuser):**
- Lokale Sitzungen nutzen den beobachteten typisierten `.amc21`-Pfad für die Übernahme, den einfachen Status und Startprüfungen auf bereits geöffnete Sitzungen.
- Ein schreibfähiges `open_project` nimmt eine exakt vorhandene `.als21`-Datei an.
- Auf einer gebundenen lokalen Sitzung funktionieren:
  - die Leser `browse_project_tree`, `plc_read`, `network_read`, `read_cross_references` und `hmi_read`,
  - außerdem `plc_write`, `network_write`, `hmi_write`, `compile_check` und `save_project`.
- Ein lokales `save_project` speichert die Sitzung über `LocalSession.Save()` und checkt nie in den Project Server ein.
- Anlegen, Speichern unter, Archivieren, Schließen und andere abschließende Operationen bleiben nicht verfügbar.
- Die abgesteckte PR4-Abnahme umfasst die Wiederherstellung mit erhaltenem Portal im Online-Fall und das UI-unterstützte Öffnen im Offline-Fall mit read-write. Die getesteten Offline-Öffnungen erforderten das Schließen von Siemens-Dialogen.
- Siehe [Unterstützung lokaler Sitzungen und Grenzen](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/MULTIUSER_OPERATIONS_SUMMARY.de.md).

`get_project_status` und `compile_check` bieten strukturierte Ausgabeschemata mit Vertragsversion `1.0` an.
- Text und `structuredContent` enthalten dasselbe kanonische Dokument; die typisierten Daten stehen unter `result.value`.
- Compilerfehler setzen `success:false`, behalten aber die Diagnosen, mit MCP `isError:false`.
- Zu große Werte werden ganz weggelassen, mit Hinweisen für eine Wiederholung.
- Siehe den [Antwortvertrag für Status und Übersetzen](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md#standalone-status-and-compilation-contract).

Aufrufer des Projektbaums brauchen `v3.0.0` oder neuer. Die v2-Eingabe `startPath` und die nackte verschachtelte Array-Antwort wurden entfernt, nicht als Aliasse behalten. Die Migrationsanfrage, die vollständige Antworthülle, die Rekonstruktion von Selektoren, das Fortsetzen, die Grenzen und die Wiederherstellung beschreibt die [Projektreferenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md#browse_project_tree-v3).

### Claude in TIA: Arbeit aus TIA Portal heraus an Claude übergeben (noch nicht live getestet)

- Das TIA-Portal-V21-Add-In „Claude in TIA“ fügt der Projektnavigation, „Geräte & Netze“ und der Projekt- und globalen Bibliothek ein Kontextmenü **Claude** hinzu: An Claude übergeben…, Erklären, Prüfen, Überarbeiten, Dokumentieren und Claude-Antworten anzeigen.
- Ein Klick schreibt eine Anfrage (Namen und MCP-Adresshinweise der markierten Objekte, keine Projektdaten) nach `%APPDATA%\TiaMcpServer\inbox`. Claude liest sie mit dem Werkzeug `tia_inbox` (jeder Modus), wenn du „schau in TIA“ sagst, arbeitet mit den übrigen Werkzeugen unter dem Zugriffsmodus und antwortet mit `tia_inbox reply_request`; die Antwort erscheint in TIA Portal.
- Optional: Die Claude-Brücke (`TiaMcpClaudeBridge.exe`, standardmäßig aus, in der Passwortverwaltung einzuschalten) startet je Anfrage einen Claude-Code-Lauf ohne Chat, einen nach dem anderen, nie zweimal, nur mit den TIA-Werkzeugen.
- Einmalige Einrichtung: `scripts\install-claude-in-tia.ps1` übersetzt das Add-In gegen das installierte TIA Portal, ruft den Siemens-Publisher auf und kopiert die `.addin`; danach in der Task Card Add-Ins aktivieren. Siehe die [Referenz zu Claude in TIA](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/CLAUDE_IN_TIA.de.md).

### Ressourcen und Prompts (noch nicht live getestet)

- Neben den Werkzeugen bietet der Server MCP-**Ressourcen** und **Prompts**, in jedem Zugriffsmodus, ohne TIA Portal und ohne etwas von deinem Rechner zu lesen.
- Ressourcen: jedes Referenzdokument aus `docs/SupportedOperations`, die README und FORK_FEATURES auf Englisch und Deutsch, beim Bauen eingebettet (`tia-mcp://docs/en/PLC_OPERATIONS_SUMMARY.md`, `tia-mcp://docs/de/...`, `text/markdown`), und die Operationsreferenz `tia-mcp://operations`, `tia-mcp://operations/{tool}`, `tia-mcp://operations/{tool}/{operation}` (für `plc_read`/`plc_write` derselbe Eintrag wie `describe_operations`).
- Prompts: `explain_block`, `review_block`, `revise_block_safely` (mit Hash lesen, planen, Probelauf, mit `expectedContentHash` schreiben, `compile_check`; nie ohne dich speichern oder laden), `document_project`, `check_hardware`, `safety_overview` (nur lesend; die Abnahme bleibt bei einer befähigten Person), `download_to_plcsim` (Ladeziele, Probelauf, Zertifikats-Fingerabdrücke erst nach deiner Freigabe), `handle_tia_inbox`. Nur lesend stehen die sechs in der Liste, die kein schreibendes Werkzeug brauchen.
- Siehe die [Referenz zu Ressourcen und Prompts](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/MCP_RESOURCES_PROMPTS_SUMMARY.de.md).

### Simulationsprojekt ohne Passwort (noch nicht live getestet)

Ein mit `create_project` angelegtes Projekt kann beim Laden in PLCSIM am Hardware-Übersetzen scheitern: Die Zugriffsstufe „Kein Zugriff (kompletter Schutz)“ und „Vertrauliche PLC-Konfigurationsdaten schützen“ verlangen ein Passwort, das das PLC-Kommunikationszertifikat ebenfalls braucht. Für ein Testprojekt ohne Passwort genügt ein Aufruf:

```json
plc_write {
  "operations": [
    { "operationId": "zugriff", "operation": "set_plc_access_level", "plcName": "PLC_1", "accessLevel": "FullAccess" },
    { "operationId": "vertraulich", "operation": "disable_plc_confidential_data_protection", "plcName": "PLC_1" }
  ]
}
```

Danach `compile_check` mit `scope` `hardware` prüfen und erst dann `plc_download` aufrufen. Schlägt der zweite Eintrag mit `api_member_missing` fehl, bietet diese TIA-Portal-Installation `Unprotect()` nicht an; dann das Häkchen in den CPU-Eigenschaften unter „Schutz & Security“ von Hand entfernen. Nur für Simulations- und Testprojekte gedacht, nie für eine echte Steuerung, die Schutz braucht.

**Echtes Projekt mit Passwortschutz:** Die Passwörter einmal hinterlegen (siehe [Passwörter](#passwörter-umgebungsvariable-oder-windows-anmeldeinformationsverwaltung)), dann zum Beispiel:

```json
plc_write {
  "operations": [
    { "operationId": "zugriff", "operation": "set_plc_access_level", "plcName": "PLC_1", "accessLevel": "HMIAccess",
      "accessLevelPasswords": { "FullAccess": { "credential": "TiaMcp/Anlage/PLC_1/Vollzugriff" }, "ReadAccess": { "credential": "TiaMcp/Anlage/PLC_1/Lesezugriff" } } },
    { "operationId": "vertraulich", "operation": "protect_plc_confidential_data", "plcName": "PLC_1",
      "passwordVariable": { "credential": "TiaMcp/Anlage/PLC_1/Vertraulich" } }
  ]
}
```

Beim späteren `plc_download` beantworten `passwords` (oder die Zuordnungsdatei mit `ModuleWriteAccessPassword`, `PlcMasterSecretPassword`) die Passwortabfragen. Einzelheiten und Herkunft jedes Siemens-Members stehen in der [Referenz zum CPU-Schutz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_SECURITY_SUMMARY.de.md).

**F-CPU (zum Beispiel CPU 1516F-3 PN/DP):** Hier `accessLevel` `FullAccessIncludingFailsafe` („Vollzugriff inkl. Fehlersicherheit (kein Schutz)“) statt `FullAccess` verwenden. Bei einer F-CPU ist „Vollzugriff (kein Schutz)“ nicht die oberste Stufe: Für den fehlersicheren Zugriff (Laden und Ändern des Sicherheitsprogramms in der CPU) verlangt TIA Portal dann weiterhin ein Passwort für die Stufe „Vollzugriff inkl. Fehlersicherheit“, und genau das soll in einem Simulationsprojekt ohne Passwort entfallen. Das ist das Verhalten des TIA-Portal-Dialogs, hier nicht live geprüft (Schritt im [F-CPU-Testplan](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/development/plcsim-fcpu-live-test-plan.md)). Unabhängig davon bleibt das Passwort des Sicherheitsprogramms (Offline-Programm, `set_safety_password`/`safety_login`, siehe [Passwörter](#passwörter-umgebungsvariable-oder-windows-anmeldeinformationsverwaltung)) eine eigene Schutzebene; beim Laden in PLCSIM wird das Sicherheitsprogramm nur mit `decisions` `SafetyProgram` mitgeladen, und auch ein simuliertes Sicherheitsprogramm ersetzt keine Sicherheitsabnahme.

## Schreibsicherheit

**Ablauf und Wächter:**
- Jedes schreibende Werkzeug durchläuft in einem Aufruf: Eingaben prüfen, die exakte Bindung vorbereiten, Ziele auflösen, Wächter auswerten, ändern, verifizieren und protokollieren.
- `plc_write`, `network_write`, `hmi_write` und `vci_write` registrieren nur blockierende und informierende Wächter und fragen deshalb nie nach. Einzige Ausnahme: `plc_write`-Aufrufe mit F-Operationen (Wächter `plc_safety_acceptance_required`) fragen in read-write einmal nach.
- Bei jedem schreibenden Werkzeug liefert `dryRun:true` die Phase `phase:preview`, ohne Änderung und ohne Nachfrage.

**Nachfrage je Zugriffsmodus:**
- In read-write braucht jeder tatsächliche Lebenszyklus-, `plc_download`-, `plc_tia_online`- und `plc_station_upload`-Aufruf eine Formular-Nachfrage (Elicitation) mit ausdrücklicher Zustimmung und booleschem `confirm:true`. Das gilt auch für Aufrufe mit nur informierenden oder gar keinen Wächtern.
- Nicht unterstützte Clients, Ablehnen, Abbrechen, Zeitüberschreitung oder Übertragungsfehler verweigern mit `access_denied`.
- Der Modus full erfüllt zu bestätigende Wächter nach Richtlinie, ohne Server-Nachfrage.
- Blockierende Wächter stoppen den Aufruf in jedem Modus. Es gibt sie nur noch, wo TIA Portal oder Openness die Änderung selbst nicht ausführen kann oder wo eine Sicherheitsregel gilt (Grundsatz: der Server ist nicht strenger als TIA Portal; die vollständige Liste steht im [Verbesserungsprotokoll](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/IMPROVEMENT_LOG.md#tia-alignment-audit-2026-11-01)).
- `plc_online_write` folgt derselben Regel: eine Nachfrage pro tatsächlichem read-write-Aufruf mit allen Online-Änderungen, im Modus full nach Richtlinie.
- Die Bestätigung folgt dem Zugriffsmodus; der alte Startschalter wurde entfernt.
- `plc_download` hat keinen blockierenden Wächter. Ob die KI laden darf, auch auf echte Hardware, steuert allein der Zugriffsmodus, mit dem der Server gestartet wird: read-only nie, read-write mit einer Nachfrage pro tatsächlichem Download, full ohne Nachfrage.

**Zugriffsmodus wählen:**
- Am einfachsten im Programm „TiaMcp Passwortverwaltung“, Reiter **Zugriffsmodus** (siehe [Passwortverwaltung](#passwortverwaltung-windows-programm-tiamcp-passwortverwaltung)). Es speichert die Wahl in `%APPDATA%\TiaMcpServer\settings.json` (oder in der Datei aus `TIA_MCP_SETTINGS`); die MCP-Client-Konfiguration braucht dann kein `--access-mode`, `"args"` kann ganz entfallen.
- Reihenfolge beim Start: Argument `--access-mode` (oder `--read-only`/`--read-write`) vor der Umgebungsvariablen `TIA_MCP_ACCESS_MODE` vor der Einstellungsdatei vor dem Standard read-write.
- Der Server liest die Datei nur beim Start und schreibt sie nie; kein MCP-Werkzeug kann sie lesen oder ändern. Eine Änderung gilt nach einem Neustart des MCP-Servers (Claude Desktop vollständig beenden und neu starten).
- Eine ungültige Einstellungsdatei verhindert den Start, auch wenn ein Argument oder die Umgebungsvariable den Modus festlegt. Der Server meldet beim Start den Modus mit seiner Herkunft (`cli`, `environment`, `settingsFile`, `default`); `tia-mcp doctor` zeigt sie ebenfalls (`--json`: `accessModeSource`, `settingsFile`).
- Wächter, Anfragen, Antworten und Wiederherstellung beschreibt die [Lebenszyklus-Referenz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md#lifecycle-operations).

**Die Zustimmung hängt vom MCP-Client ab.**
- Eine Nachfrage beweist nur, dass der Client eine zustimmende Antwort zurückgegeben hat, nicht dass ein Mensch sie gesehen hat.
- Projektändernde und Lebenszyklus-Werkzeuge behalten vorsichtige Hinweise auf zerstörende Wirkung. Wer bei jedem Aufruf eine Freigabe will, nimmt sie nicht in die Auto-Freigabe-Listen des Clients auf.
- `plc_write`, `network_write`, `hmi_write` und `vci_write` nutzen `dryRun:true` als Vorschau und führen standardmäßig aus, ohne Token und ohne Server-Nachfrage. Freigabe-Abfragen des Clients bleiben davon unabhängig.

**Bindung und Protokoll:**
- Der Lebenszyklus nutzt eine festgehaltene Bindung und Prüfungen beim Übergang:
  - Öffnen und Anlegen können ungebunden beginnen.
  - Speichern unter bindet an die entstandene Kopie.
  - Schließen hebt die Bindung auf.
- Der Lebenszyklus protokolliert jeden Aufruf, auch Probeläufe und Ablehnungen, in `writes-yyyy-MM-dd.jsonl` unter `%LOCALAPPDATA%\TiaMcpServer\audit`. Audit v2 hält die Bestätigung als `user`, `policy` oder `none` fest.
- `plc_write`, `network_write`, `hmi_write` und `vci_write` schreiben ebenfalls einen Audit-v2-Eintrag pro angenommenem Aufruf, auch für Vorschauen und Ablehnungen. Die Bestätigung ist in read-write `none` und in full `policy`.
- Eine Ablehnung der Argumente durch das SDK vor dem Aufruf erzeugt keinen Schreibeintrag. Der alte Schreib-Audit-Strom wurde entfernt.

**Wahl von TIA-Instanz und Projekt:**
- Der Worker bindet sich nie an das erste aufgezählte TIA Portal und wählt nie das erste geöffnete Projekt.
- Er verlangt einen exakt passenden Pfad oder einen tatsächlich einzigen Kandidaten. Mehrere mögliche Ziele schlagen mit `target_ambiguous` fehl, bevor angebunden oder geändert wird.
- Der Server startet nicht, wenn `--project` oder `TIA_MCP_PROJECT_PATH` kein absoluter `.ap21`- oder `.amc21`-Pfad ist.
- Ein konfigurierter `--project`-Pfad ist erst schreibbereit, wenn eine passende Worker-Identität beobachtet wurde.
- Weicht die Identität später ab, gibt es `binding_conflict`, und die Sitzung bleibt ungültig, bis ausdrücklich neu gebunden wird.

**Öffnen und Wechseln:**
- Es gibt kein implizites Öffnen: Nur `open_project`, `create_project` und `retrieve_project` öffnen ein Projekt. Für ein bereits geöffnetes Projekt `bind_project` verwenden.
- Die Eigentümerschaft des Workers geht beim Trennen verloren; ein wieder angebundenes Projekt gilt als von der Oberfläche besessen.
- read-only öffnet, legt an, speichert oder schließt nie ein Projekt, eine ausdrückliche Bindung darf die gewählte Sitzung aber wechseln.
- Cursor des Projektbaums lehnen Bindungswechsel ab, auch einen Wechsel weg und zurück zum selben Pfad.

**Festgehaltene Bindung bei Schreibzugriffen:**
- `plc_write`, `network_write`, `hmi_write` und `vci_write` halten eine geprüfte Projektbindung über Planung, geordnete Änderung, Verifikation und Protokoll fest. Ein gleichzeitiges Neubinden kann einen geplanten Schreibzugriff deshalb nicht umleiten.
- Netzwerk und HMI planen nicht pro Eintrag neu und lesen im Worker nicht zurück.
- HMI plant aus einem einzigen anfänglichen Satz öffentlicher Lesebelege pro HMI; diese Lesezugriffe laufen nacheinander und sind nicht atomar.
- Beide Bereiche halten erledigte Einträge als `trusted` oder `superseded` fest.
- `plc_write` blockiert vor jeder Ausführung nur einen doppelten Singleton-OB und F-Änderungen bei passwortgeschütztem, nicht angemeldetem Sicherheitsprogramm. Nicht lesbare Belege, Löschungen und Adressüberschneidungen werden als Hinweis gemeldet; über alles andere entscheidet TIA Portal.

**Fehlerformen:**
- Eine Lebenszyklus-Ablehnung hat oben ein `error` mit `{category,message}` und MCP `isError:true`.
- Ein versuchter Schreibzugriff oder eine fehlgeschlagene Verifikation hat dagegen `success:false`, `error:null` und `isError:false`, mit typisierten Fehlerbelegen in `result` oder `verification`.
- Eine Änderung kann trotz fehlgeschlagener Verifikation gelungen sein; vor einer Wiederholung den Zustand prüfen.
- `save_project_as` verlangt weiterhin `rebind:true`, und Warnungen bleiben von Erfolg/Misserfolg getrennt.

**Stand der Umstellung:** Die entfernten Lebenszyklus-Eingaben und die strukturierten Ausgaben sind inkompatible Änderungen, vorgemerkt für das abschließende Hauptrelease der Neugestaltung. Die Umstellungen für Netzwerk und PLC sind umgesetzt; der Sicherheits-Token-Kern und die generischen Stapelwerkzeuge sind entfernt. Dieser Schritt veröffentlicht kein Paket.

### Passwörter: Umgebungsvariable oder Windows-Anmeldeinformationsverwaltung

Kein Werkzeug nimmt ein Passwort entgegen. `plc_download`, `plc_tia_online`, `plc_station_upload` (`passwords`) und die F-Operationen von `plc_write` (`passwordVariable`) nennen nur die Quelle, in einer von drei Formen:

- `"PLC1_PW"` oder `{ "env": "PLC1_PW" }`: der Name einer Umgebungsvariablen, die in der Konfiguration des MCP-Clients gesetzt ist (wie bisher).
- `{ "credential": "TiaMcp/MCP_Safety_01/PLC_F1/Safety" }`: das Ziel (Target) einer generischen Windows-Anmeldeinformation. Der Server liest sie erst im Moment des Aufrufs mit `CredReadW` in einen `SecureString`, überschreibt den Puffer und gibt, protokolliert und auditiert nie den Wert, sondern nur den Zielnamen und die Art der Quelle. Zielnamen müssen mit dem eingestellten Präfix beginnen (Standard `TiaMcp/`, siehe unten); andere Anmeldeinformationen des Nutzers (Git, Browser, VPN) kann der Server dann nicht lesen. Auf anderen Betriebssystemen als Windows wird diese Quelle mit einer klaren Fehlermeldung abgelehnt.

Ein Passwort einmalig hinterlegen (der Nutzer tippt es selbst in seine eigene Konsole; Claude sieht und verarbeitet es nie):

```cmd
cmdkey /generic:"TiaMcp/MCP_Safety_01/PLC_F1/Safety" /user:tia /pass
```

`cmdkey` fragt das Passwort verdeckt ab. Der Eintrag gilt für den Windows-Benutzer, unter dem der MCP-Server läuft; ändern mit demselben Befehl, entfernen mit `cmdkey /delete:"TiaMcp/MCP_Safety_01/PLC_F1/Safety"`.

**Präfix der Anmeldeinformationen (Einstellung des Betreibers):** Standard ist `TiaMcp/`. Wer einen anderen Präfix will, setzt in der Konfiguration des MCP-Clients unter `env` die Variable `TIA_MCP_CREDENTIAL_PREFIX`, zum Beispiel auf `Anlage/Linie1/`; `*` erlaubt jedes generische Anmeldeinformations-Ziel. Kein Werkzeugaufruf kann den Präfix ändern; ein ungültiger Wert (Leerzeichen am Anfang oder Ende, Steuerzeichen, `*` innerhalb eines Präfixes, mehr als 128 Zeichen) verhindert den Start. Der aktive Präfix steht in der Startausgabe (`TIA MCP credential prefix: ...`) und in der Doctor-Prüfung `password-sources`. Passwörter selbst nimmt weiterhin kein Werkzeug als Argument an.

**Netzwerkpfade:** Alle Datei-Ein- und -Ausgaben (Exporte, Importe, Archive, E/A-Liste, VCI-Arbeitsbereiche, Sicherungen, Pfad der Zuordnungsdatei) akzeptieren UNC-Freigaben (`\\server\freigabe\...`) wie Laufwerkspfade. Abgelehnt werden nur Gerätepfade (`\\?\`, `\\.\`, `\??\`), weil Windows sie ohne Pfadnormalisierung weitergibt (keine Auflösung von `.`/`..`, keine Behandlung reservierter Namen) und die Prüfungen deshalb nicht greifen würden.

Wer die Quelle nicht bei jedem Aufruf angeben will, legt eine Zuordnungsdatei an: `%APPDATA%\TiaMcpServer\passwords.json` (oder den Pfad in `TIA_MCP_PASSWORD_MAP`). Sie enthält nur Namen, nie Werte:

```json
{ "version": 1, "entries": [
  { "project": "MCP_Safety_01", "plc": "PLC_F1", "purpose": "safety", "credential": "TiaMcp/MCP_Safety_01/PLC_F1/Safety" },
  { "plc": "PLC_1", "purpose": "ModuleWriteAccessPassword", "env": "PLC1_WRITE_PW" } ] }
```

`purpose` ist `safety` für `safety_login`, `set_safety_password` und `revoke_safety_password`, der Name einer Zugriffsstufe (`FullAccessIncludingFailsafe`, `FullAccess`, `ReadAccess`, `HMIAccess`) für `set_plc_access_level`, `PlcMasterSecretPassword` für die vertraulichen Konfigurationsdaten (Schutz und Download) oder der Kurzname der Passwort-Konfiguration (zum Beispiel `ModuleWriteAccessPassword`, `OnlineReadAccessPassword`) für Download, Online, Vergleich und Upload. `project` (Projektname ohne Endung) und `plc` sind optional; weggelassen oder `"*"` heißt „für alle“. Der genaueste Eintrag gewinnt: Projekt und PLC vor Projekt (alle PLCs des Projekts) vor PLC (dieser PLC-Name in jedem Projekt) vor `"*"`/`"*"` (Vorgabe für alle Projekte und PLCs). Eine Angabe im Aufruf gewinnt immer. Eine Datei mit unbekannten Feldern oder mit Feldern, die nach einem Wert aussehen (`password`, `secret`, `value`, ...), verhindert den Start des Servers.

### Passwortverwaltung (Windows-Programm „TiaMcp Passwortverwaltung“)

Statt `cmdkey` und die Zuordnungsdatei von Hand zu pflegen, gibt es ein kleines Windows-Programm, das neben dem Server installiert wird (`password-manager\TiaMcpPasswortverwaltung.exe`). Passwörter tippt der Nutzer nur in dessen Fenster; über den Chat, die Kommandozeile oder eine Datei läuft kein Passwort. Im Reiter **Zugriffsmodus** wählt man außerdem, ob der Server nur liest, mit Rückfrage schreibt oder voll arbeitet (siehe unten).

**Starten:**

```powershell
tia-mcp passwords
```

Der Befehl startet das Programm aus dem Installationsordner des Tools. Wer in der MCP-Client-Konfiguration einen eigenen Präfix (`TIA_MCP_CREDENTIAL_PREFIX`) oder eine eigene Zuordnungsdatei (`TIA_MCP_PASSWORD_MAP`) gesetzt hat, gibt dieselben Werte mit: `tia-mcp passwords --prefix "Anlage/Linie1/" --map "\\server\freigabe\passwords.json"`. Ohne diese Angaben gelten die Umgebungsvariablen der Konsole, sonst `TiaMcp/` und `%APPDATA%\TiaMcpServer\passwords.json`. Das Programm läuft nur unter Windows und braucht die .NET 10 Desktop Runtime (im .NET SDK für Windows enthalten, sonst einzeln installieren). Es wird nur beim Bauen unter Windows erzeugt; ein Linux-Build enthält es nicht.

**Bedienung:**
- Die Liste zeigt je Eintrag den Zweck, das Projekt („Für alle Projekte“ oder ein Projektname), die PLC („alle PLCs“ oder ein Name), die Quelle (Name der Anmeldeinformation oder der Umgebungsvariablen) und das Ergebnis des letzten Tests. Ein Passwort zeigt sie nie.
- **Hinzufügen…**: Zweck wählen, nämlich Zugriffsstufe (Vollzugriff, Vollzugriff einschließlich fehlersicher, Lesezugriff, HMI-Zugriff), vertrauliche PLC-Konfigurationsdaten, Safety (F-Programm), Modul-/Online-Passwort (Schreib-/Lesezugriff, Online-Lesezugriff, Hochladen) oder eine andere Passwortkonfiguration per Kurzname. Dann den Geltungsbereich wählen: „Für alle Projekte“ oder ein Projekt, jeweils für alle PLCs oder eine bestimmte PLC. Zum Schluss das Passwort zweimal eingeben. Die Felder sind verdeckt; „anzeigen“ blendet die Eingabe ein.
- **Ändern…**: Geltungsbereich oder Zweck anpassen und ein neues Passwort eingeben. Das gespeicherte Passwort wird nie zurückgelesen. Bleibt der Geltungsbereich gleich, wird die vorhandene Anmeldeinformation überschrieben; sonst entsteht eine neue, und das Programm fragt, ob es die alte löschen soll.
- **Löschen**: entfernt den Eintrag aus der Zuordnungsdatei und fragt, ob auch die Anmeldeinformation gelöscht werden soll, falls kein anderer Eintrag sie nutzt.
- **Test** / **Alle testen**: prüft, ob die Anmeldeinformation für diesen Windows-Benutzer vorhanden ist und zum Präfix des Servers passt, ohne den Wert zu zeigen oder zu vergleichen. Bei Einträgen mit Umgebungsvariable prüft es nur die Umgebung des Programms selbst; der Server sieht die Umgebung seines MCP-Clients.

**Was gespeichert wird:**
- Das Passwort landet als generische Windows-Anmeldeinformation (`CredWriteW`, Persistenz `CRED_PERSIST_LOCAL_MACHINE`: dieser Benutzer auf diesem Rechner, kein Roaming) unter `<Präfix><Projekt>/<PLC>/<Zweck>`, zum Beispiel `TiaMcp/Anlage_01/PLC_1/FullAccess` oder `TiaMcp/Alle Projekte/Alle PLCs/Safety`.
- In die Zuordnungsdatei schreibt das Programm nur Projekt, PLC (`"*"` für alle), Zweck und den Namen der Anmeldeinformation. Es schreibt über eine temporäre Datei, sodass nie eine halbe Datei entsteht.
- Eine ungültige Zuordnungsdatei überschreibt es nicht. Eigene Einträge mit `env` bleiben erhalten.
- Passwörter erscheinen weder in der Datei noch in einem Protokoll. Der Server selbst liest Anmeldeinformationen nur; schreiben kann sie nur dieses Programm.
- Die Zuordnungsdatei liest der Server beim Start. Nach neuen oder gelöschten Einträgen den MCP-Server (bzw. den MCP-Client) neu starten. Ein geändertes Passwort unter demselben Namen gilt sofort, weil der Server die Anmeldeinformation erst beim Aufruf liest.

**Reiter „Zugriffsmodus“:**
- Drei Optionen, jeweils mit einer Zeile Erklärung: **Nur lesen (read-only)** (der Server ändert, öffnet, speichert oder schließt nie ein Projekt und schreibt nie in eine PLC oder Simulation), **Lesen und schreiben, mit Rückfrage (read-write)** (jeder Lebenszyklus-, Download- und Online-Aufruf wird einmal bestätigt) und **Voll, ohne Rückfrage (full)** (dieselben Aufrufe ohne Rückfrage).
- Angezeigt wird der gespeicherte Wert oder „nicht gesetzt – Standard read-write“. **Speichern** schreibt die Wahl, **Einstellung entfernen** setzt auf den Standard zurück. Das Programm schreibt `settings.json` über eine temporäre Datei und überschreibt eine ungültige Datei nie.
- Die Datei enthält nur `{ "version": 1, "accessMode": "read-only" }` (geschlossenes Schema: unbekannte Felder oder eine andere Version verhindern den Start).
- Ein `--access-mode`-Argument oder `TIA_MCP_ACCESS_MODE` in der MCP-Client-Konfiguration hat Vorrang; der Reiter weist darauf hin. Die Änderung gilt nach einem Neustart von Claude Desktop bzw. des MCP-Servers.
- Eine eigene Datei (`TIA_MCP_SETTINGS` in der Client-Konfiguration) gibt man dem Programm mit: `tia-mcp passwords --settings "D:\TiaMcp\settings.json"`.

## Grenzen (Siemens / TIA Openness)

Was dieser Server nicht kann, hat zwei Ursachen: Entweder bietet Siemens keine Schnittstelle (**Siemens**), oder dieser Fork hat sich dagegen entschieden (**Fork**). Punkte, die nur aus der Dokumentation oder aus Beobachtung stammen, sind entsprechend markiert. Die meisten Werkzeuge sind noch nicht live getestet; das steht jeweils bei den Werkzeugen oben.

### Inbetriebnahme und Online-Betrieb

- **Kein Programmstatus, keine Haltepunkte, kein Einzelschritt** (Siemens, nicht verifiziert): In der Openness-Referenz V21 haben wir dafür keine Schnittstelle gefunden. Istwerte gibt es nur über den DB-Snapshot (`get_db_snapshot`/`create_db_snapshot`), PLCSIM Advanced (`plcsim_read`), die Web-API (`plc_online_read`, nur S7-1500 mit aktiviertem Webserver) oder Beobachtungstabellen über `read_watch_table_live`.
- **Kein Online-Forcen** (Siemens): Für Forcetabellen ist keine Online-API dokumentiert. Sie werden nur offline bearbeitet.
- **Achsoptimierung, PID-Einstellung, Trace** (Siemens, nicht verifiziert): Dafür haben wir keine Openness-Schnittstelle gefunden. Technologieobjekte lassen sich anlegen, parametrieren und verschalten, aber nicht in Betrieb nehmen.
- **Download ohne Abschlussnachweis** (Siemens): `Download()` kann in V21 vor dem Ende zurückkehren. `completionVerified` ist deshalb immer `false`; den Zustand danach mit `compare_to_online` oder der Web-API prüfen.
- **TLS-Zertifikate** (Siemens; Vertrauen nur auf Fingerabdruck ist Fork): TIA Portal fragt bei jeder PLCSIM-Instanz und jeder CPU mit selbstsigniertem Zertifikat nach Vertrauen. Entweder einmal im TIA-Dialog bestätigen oder die SHA-256-Fingerabdrücke als `trustedCertificates` übergeben. Ein „allem vertrauen“ gibt es nicht.
- **PLCSIM-Instanzen** (Fork): Instanzen werden im PLCSIM Advanced Control Panel angelegt, nicht vom Server. `plcsim_test` misst Wanduhrzeit, nicht die virtuelle Zeit.

### Safety

- **Abnahme, Validierung und Ausdruck bleiben Handarbeit** (Norm und Siemens): Ein Sicherheitsprogramm muss von einer qualifizierten Person validiert und abgenommen werden. Der Server ersetzt das nie; jeder Fail-safe-Schreibvorgang zeigt den Hinweis `plc_safety_acceptance_required`.
- **Kein eigenes F-Übersetzen** (Siemens): Es gibt keine dokumentierte F-Compile-Funktion. `compile_check` der PLC übersetzt auf einer F-CPU das Sicherheitsprogramm mit (live am 2. 11. 2026: Meldung „Compile safety program 'Safety Administration'“).
- **F-Logik schreiben** (teilweise live getestet): Für `create_block` mit F_FUP/F_KOP haben wir keine geprüfte SimaticML-Vorlage. Ein SIMATIC-SD-Netzwerkimport in einen F-Baustein gelang live am 2. 11. 2026, entfernte aber beim Main-Safety-Baustein den Verweis der F-Ablaufgruppe; deshalb gehen SD-Änderungen am Main-Safety-Baustein über einen temporären F-Baustein und SimaticML (siehe oben); danach `get_safety_administration` und `compile_check` prüfen. Ein frisch aus einem SD-Dokument angelegter Baustein wird von TIA Portal erst nach dem Übersetzen als SimaticML exportiert; `get_block_content` xml meldet das jetzt als Warnung statt das `.xml` stillschweigend wegzulassen.
- **Safety-Passwort** (Siemens): Ist ein Offline-Passwort gesetzt, verlangt TIA Portal eine Anmeldung. Der Server meldet sich nur mit Passwörtern aus den Passwortquellen an, nie mit ausgedachten.

### Programmiersprachen und Inhalte

- **Know-how-geschützte Bausteine** (Siemens): Ihr Export enthält nur die Schnittstelle. Inhalt lesen oder ändern geht nicht; der Server lehnt Round-Trips dafür ab.
- **KOP/FUP und GRAPH** (Siemens): Grafische Sprachen gibt es nur als SimaticML-XML. Das geht, ist aber umständlich und fehleranfällig; GRAPH besonders. SCL und Textquellen sind der einfachere Weg.
- **Einträge in Beobachtungs- und Forcetabellen** (Siemens): Keine Objekt-API für einzelne Einträge dokumentiert; Änderung nur über den SimaticML-Round-Trip der ganzen Tabelle.
- **Textlisteneinträge, Meldetexte, ProDiag-Überwachungen** (Siemens): Kein dokumentiertes Objektmodell; nur über den XLSX-Export und -Import von TIA Portal.
- **Siemens-Elemente ohne Dokumentation** (Fork): Werden zur Laufzeit über ihren exakten Namen gebunden. Fehlt eines in der installierten Version, schlägt der Aufruf mit `api_member_missing` fehl, ohne etwas zu ändern. Es wird nichts erraten.

### TIA Portal selbst

- **Modale Dialoge blockieren Openness** (Siemens, im Live-Test beobachtet): Solange in der TIA-Oberfläche ein Dialog offen ist, hängen Openness-Aufrufe. Beobachtet wurden der Dialog zur IP-Zuweisung an die PG/PC-Schnittstelle (versteckt hinter anderen Fenstern) und der Zertifikatsdialog. Während der Download- oder Online-Dialog der Oberfläche offen war, stand außerdem kein `DownloadProvider` zur Verfügung (beobachtet; dass die Oberfläche die CPU dabei belegt, ist eine Vermutung). Bestätigungsdialoge, die TIA Portal über Openness meldet, beantwortet `portalConfirmations`; Dialoge der Oberfläche sieht der Server nicht.
- **Ein Projekt pro TIA-Instanz** (Siemens, beobachtet): `create_project` und `open_project` schlagen fehl, solange ein anderes Projekt offen ist (`retrieve_project` bildet dieselbe Verweigerung ab). Erst `close_project`.
- **Was die Oberfläche gerade zeigt, sieht der Server nicht** (Siemens): Openness kennt keine Auswahl, kein aktives Fenster und keinen Editor-Inhalt, der noch nicht übernommen ist.
- **Projekte mit CPU-Schutz** (Siemens): Zugriffsstufen und der Schutz vertraulicher Daten brauchen Passwörter. Der Server setzt und entfernt sie nur mit Passwörtern aus den Passwortquellen. Für Simulationsprojekte siehe den Abschnitt „Simulationsprojekt ohne Passwort“ oben.

### Entscheidungen dieses Forks

- **Nur TIA Portal V21** (Fork): Andere Versionen werden nicht unterstützt und nicht getestet.
- **Klassisches WinCC über Dateien** (Fork): Klassische HMIs (Comfort/Advanced, `HmiTarget`) werden von den klassischen Operationen aufgelistet, exportiert, importiert und gelöscht (SimaticML, Member zur Laufzeit gebunden, nicht live getestet); jede andere HMI-Operation gilt nur für WinCC Unified und schlägt an einem klassischen HMI mit `target_kind_unsupported` fehl.
- **Nicht strenger als TIA Portal** (Fork): Der Server verweigert nur, was TIA Portal selbst verweigert, oder ungültige und mehrdeutige Eingaben. Alles andere sind Hinweise; TIA Portal entscheidet.
- **Passwörter nie im Chat** (Fork): Werkzeuge nehmen nur Namen von Umgebungsvariablen oder Anmeldeinformationen entgegen, nie Passwortwerte. Der Server erzeugt auch keine Passwörter.

## Architektur

TIA Portal V21 liefert seine Openness-API als .NET-Framework-4.8-Assemblies. Diese nutzen Remoting-APIs von .NET Framework, die in einem .NET-10-Prozess nicht richtig laufen.

Das Projekt nutzt deshalb zwei Prozesse für TIA Portal und optional einen dritten für S7-PLCSIM Advanced:

- `TiaMcpServer`: der .NET-10-MCP-Server über stdio und Host des .NET-Global-Tools. Er betreibt auch den S7-1500-Web-API-Client für `plc_online_read`/`plc_online_write` und den Leser der Siemens-Dokumentation `siemens_docs` im eigenen Prozess (ohne Worker).
- `TiaMcpServer.OpennessWorker`: ein Worker-Prozess unter .NET Framework 4.8, der `Siemens.Engineering.*` lädt und mit TIA Portal spricht.
- `TiaMcpServer.PlcSimWorker`: ein x64-Worker unter .NET Framework 4.8, der erst beim ersten PLCSIM-Werkzeugaufruf startet. Er lädt die S7-PLCSIM-Advanced-API aus deren Installation mit `Assembly.LoadFile` und bindet sie per Reflection.

**Kommunikation zwischen Host und Workern:**
- Der MCP-Host hält einen dauerhaften Worker-Prozess unter .NET Framework 4.8, der an TIA Portal angebunden ist.
- Er tauscht zeilengetrenntes JSON über stdin/stdout aus. Anfragen laufen nacheinander, und der Worker startet nach einem Absturz oder einer Zeitüberschreitung automatisch neu.
- Der PLCSIM-Worker nutzt dieselbe Hülle und denselben Transport mit eigenem Handshake.

**Siemens-DLLs:**
- Openness- und PLCSIM-Advanced-DLLs von Siemens werden nie in dieses Repository oder das NuGet-Paket kopiert. Die Worker laden sie aus den lokalen Installationen von TIA Portal V21 und S7-PLCSIM Advanced.
- Die einzige Siemens-Assembly im Paket ist der MIT-lizenzierte Web-API-Client `Siemens.Simatic.S7.Webserver.API` von nuget.org, eine Abhängigkeit des Hosts.

## Schnellstart

Server als .NET-Global-Tool installieren, die Umgebung prüfen und ihn bei einem MCP-Client registrieren:

```powershell
dotnet tool install -g TiaMcpServer
tia-mcp doctor
tia-mcp install claude-code
```

`tia-mcp doctor` prüft vor dem Verbinden:
- die Windows-Version und die .NET-Laufzeiten,
- die TIA-Portal-Installation und die Openness-Assemblies,
- die Gruppenmitgliedschaft und die Kompatibilität von Host und Worker.

Er meldet außerdem, woher die optionale S7-PLCSIM-Advanced-API geladen würde und welche PLC-Web-API-Ziele die optionale Datei `--plc-webapi-config` enthält, ohne sie zu kontaktieren. Am besten zuerst ausführen; er sagt genau, welche Voraussetzung fehlt.

| Voraussetzung | Hinweis |
| --- | --- |
| Windows | nur Windows; die Openness-API gibt es auf keinem anderen System |
| Siemens TIA Portal V21 | mit installiertem und aktiviertem Openness |
| Gruppe `Siemens TIA Openness` | der aktuelle Windows-Benutzer muss Mitglied sein |
| Framework-abhängiges Global-Tool: stabiles .NET 10 SDK 10.0.400 oder neuer | liefert `dotnet tool install` und die unterstützte .NET-10-Laufzeit |
| .NET Framework 4.8 Runtime | wird vom Openness-Worker-Prozess gebraucht |

**Installationsvarianten:**
- Das framework-abhängige Global-Tool `tia-mcp` braucht eine unterstützte .NET-10-Laufzeit.
- Das separate eigenständige `win-x64`-Archiv bringt die Host-Laufzeit mit.
- Bei keiner der beiden Varianten muss das NuGet-Paket `ModelContextProtocol` installiert werden.

Eigene .NET-Integrationen, die direkt gegen das C#-MCP-SDK kompilieren, müssen die Migrationshinweise zu ModelContextProtocol 2.2 lesen. Danach Protokollverhandlung, Werkzeugschemata und strukturierte Ergebnisse erneut testen. Diese Anforderung ist unabhängig von der oben beschriebenen v3-Migration von `browse_project_tree`.

**Unterstützte Clients für `tia-mcp install`:** Claude Code, Codex, OpenCode, MiMoCode.
- Server werden standardmäßig im Modus **read-only** registriert (dreizehn Werkzeuge).
- `--access-mode read-write` schaltet frei, mit einer Nachfrage pro tatsächlichem Aufruf (zweiunddreißig Werkzeuge):
  - Änderungen, Übersetzen und Simulationssteuerung,
  - Lebenszyklus,
  - den PLC-Download (PLCSIM oder Hardware),
  - Online-Änderungen, Online-Verbindung herstellen und trennen, Station hochladen,
  - den Lebenszyklus globaler Bibliotheken.
- `--access-mode full` führt diese Aufrufe ohne Server-Nachfrage aus (dieselben zweiunddreißig Werkzeuge).
- Wer den Modus lieber im Programm wählt, lässt `--access-mode` in der Client-Konfiguration weg und stellt ihn in `tia-mcp passwords`, Reiter **Zugriffsmodus**, ein ([Zugriffsmodus wählen](#schreibsicherheit)). Ein von `tia-mcp install` eingetragenes `--access-mode` hat Vorrang vor dieser Einstellung.

Die Bindung an ein bestimmtes Projekt, alle Installationsoptionen und die vollständige Beschreibung der Zugriffsmodi stehen in der [Installationsanleitung](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/guides/installation.de.md). Wie man aus dem Quellcode baut statt das veröffentlichte Werkzeug zu installieren, steht unter [Aus dem Quellcode bauen](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/development/building.md).

## Dokumentation

Die Links führen zur deutschen Fassung, wo es eine gibt (`.de.md`), sonst zur englischen. Was dieser Fork mehr kann als das Original, mit dem Live-Test-Stand jeder Funktion: [Was dieser Fork mehr kann als das Original](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/FORK_FEATURES.de.md).

**Den Server nutzen**

- [Installation](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/guides/installation.de.md): installieren, mit `doctor` prüfen, bei einem Client registrieren, Zugriffsmodi
- [MCP-Client-Konfiguration](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/guides/mcp-client-configuration.de.md): Client-Konfiguration, Adressierung von Bausteinpfaden und Antwortgrößen
- [Fehlersuche](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/guides/troubleshooting.de.md): häufige Fehler und geprüftes Verhalten von TIA Portal V21
- [Unterstützte Operationen](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/README.de.md): jede Operation nach Bereich, mit Parametern
- [PLC online über die Web-API](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_ONLINE_WEBAPI_SUMMARY.de.md): `plc_online_read`/`plc_online_write`: Zieldatei, Zugangsdaten, Zertifikate, Bestätigung, Herkunft
- [Claude in TIA](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/CLAUDE_IN_TIA.de.md): TIA-Portal-Add-In, `tia_inbox`, Claude-Brücke, einmalige Einrichtung, Herkunft
- [Siemens-Dokumentation](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/SIEMENS_DOCS_SUMMARY.de.md): `siemens_docs`: Operationen, Referenzen, Host-Freigabeliste und Grenzen, Live-Prüfung der Endpunkte
- [CPU-Schutz](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_SECURITY_SUMMARY.de.md): Zugriffsstufe und vertrauliche Konfigurationsdaten für Simulationsprojekte ohne Passwort, Hardware-Übersetzen, Herkunft jedes Members

**Den Aufbau verstehen**

- [Architektur](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/ARCHITECTURE.md): Aufbau mit zwei Prozessen, Durchsetzung der Zugriffsrechte, Schreibsicherheit, die kanonische JSON-Schnittstelle

**Bauen und mitarbeiten**

- [Mitarbeit](https://github.com/Birger90/tia-portal-mcp-server/blob/main/CONTRIBUTING.de.md): Arbeitsablauf, Konventionen für Branches und Commits
- [Aus dem Quellcode bauen](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/development/building.md): bauen, testen, Abdeckung, lokal ausführen
- [Lokales Testen in der MCP-Sandbox](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/development/local-mcp-testing.md): die Testschleife mit dem MCP Inspector
- [Paketierung](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/development/packaging.md): das Paket bauen, einen lokalen Build als `tia-mcp` installieren

**Richtung**

- [Roadmap](https://github.com/Birger90/tia-portal-mcp-server/blob/main/ROADMAP.de.md): grundsätzliche Prioritäten
- [Roadmap der Netzwerkoperationen](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/roadmap/network-operations.md): schrittweise Auslieferung der Netzwerkwerkzeuge
- [Roadmap der Export-/Importformate](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/roadmap/export-import-format.md): Austausch im Quellformat
- [Roadmap des JSON-Vertrags](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/roadmap/json-contract.md): ein strukturierter Ausgabevertrag für alle Werkzeuge
- [Verbesserungsprotokoll](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/IMPROVEMENT_LOG.md): offene Punkte und erledigte Entwicklungsarbeit

## Mitarbeit

Beiträge sind willkommen. Arbeitsablauf und Einrichtung der Umgebung stehen in [CONTRIBUTING.md](https://github.com/Birger90/tia-portal-mcp-server/blob/main/CONTRIBUTING.de.md). Architektur und Build-Referenz stehen in [AGENTS.md](https://github.com/Birger90/tia-portal-mcp-server/blob/main/AGENTS.md).

## Lizenzhinweise für diesen Fork

Dieser Fork steht als Gesamtwerk unter der **GNU Affero General Public License v3.0** (AGPL-3.0-only, siehe [LICENSE](https://github.com/Birger90/tia-portal-mcp-server/blob/main/LICENSE)). Grund: Er enthält Code aus [rickgaiser/TiaMcp](https://github.com/rickgaiser/TiaMcp), der unter AGPL-3.0 steht. Welche Dateien das sind, steht in [THIRD_PARTY_NOTICES.md](https://github.com/Birger90/tia-portal-mcp-server/blob/main/THIRD_PARTY_NOTICES.de.md).

Der Basiscode stammt aus [Czarnak/tia-portal-mcp](https://github.com/Czarnak/tia-portal-mcp) und steht unter MIT. Dessen Lizenzhinweis bleibt in [LICENSE-MIT](https://github.com/Birger90/tia-portal-mcp-server/blob/main/LICENSE-MIT) erhalten.

Wer diesen Fork weitergibt oder als Netzwerkdienst anderen anbietet, muss den vollständigen Quellcode unter AGPL-3.0 bereitstellen. Das ist kein Rechtsrat.

## Sicherheit

Wie Sicherheitslücken gemeldet werden, steht in [SECURITY.md](https://github.com/Birger90/tia-portal-mcp-server/blob/main/SECURITY.de.md).

## Weitere Werkzeuge

- [TIA Portal V21 Git Add-In](https://github.com/Czarnak/tia-git-addin)
- [Claude-Code-/Codex-/Gemini-Plugin für die Entwicklung mit TIA Portal](https://github.com/Czarnak/totally-integrated-claude)
