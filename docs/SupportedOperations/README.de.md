# Unterstützte TIA-Portal-Operationen

**Sprache:** [English](README.md) · Deutsch

Dieses Verzeichnis ist die Referenz für die öffentliche Operationsoberfläche von `tia-portal-mcp`. Es beschreibt die MCP-Tools, Operationsnamen, unterstützten Datenformate, Sicherheitsregeln und die aktuellen Grenzen des Funktionsumfangs.

Die MCP-Oberfläche ist bewusst schmaler als die vollständige TIA Portal Openness V21 API. Eine als aktuelle Grenze aufgeführte Fähigkeit liegt außerhalb des Serververtrags; das ist keine Aussage darüber, dass die zugrunde liegende Openness API diese Aktion nicht ausführen kann.

## Operationsmodell

### PLC-Lese- und Schreibtools

PLC-Lesezugriffe laufen über `plc_read` (bis zu 50 voneinander unabhängige Operationen; ein fehlgeschlagener Eintrag hält die
übrigen Einträge nicht an) und das eigenständige `read_cross_references`. PLC-Schreibzugriffe laufen über `plc_write`
(bis zu 50 geordnete Operationen gegen ein Projekt; der erste Fehler beendet den Aufruf, ohne
Rollback). Siehe die [Zusammenfassung der PLC-Operationen](PLC_OPERATIONS_SUMMARY.md#plc-writes-plc_write).
Die generischen Batch-Tools `preview_write_batch`, `apply_write_batch` und `execute_read_batch` wurden
außer Dienst gestellt.

Jeder Eintrag enthält eine `operationId`, einen `operation`-Namen und die Felder für diese Operation. Lese- und Schreiboperationsnamen sind getrennt; Operationen des Projektlebenszyklus sind keine gültigen Einträge.

#### Leseoperationen

`plc_read` unterstützt `get_block_content`, `get_type_content`, `list_tag_tables`, `list_download_targets` (den Offline-Downloadpfad für `plc_download`), `read_address_occupancy` und `find_free_address` (siehe [HARDWARE_MODULES_SUMMARY.md](HARDWARE_MODULES_SUMMARY.de.md)),
dazu die Lesezugriffe für Online/Offline-Vergleich, Online-Zustand, Station-Upload-Schnittstellen und Beobachtungs-/Forcetabellen aus der [Referenz PLC online über Openness](PLC_ONLINE_OPENNESS_SUMMARY.de.md), `list_external_sources`, `read_tag_comments`, `find_objects` und `scan_accessible_devices` ([PLC_SOURCES_FOLDERS_SUMMARY.md](PLC_SOURCES_FOLDERS_SUMMARY.de.md)), die Lesezugriffe für Textlisten, Meldungstexte, Überwachungen und ProDiag aus der [Referenz PLC-Meldungen und Textlisten](PLC_ALARMS_TEXTLISTS_SUMMARY.de.md) und die Technologieobjekt-Lesezugriffe `list_technology_objects`, `get_technology_object_parameters`, `get_technology_object_hardware_connection` und `get_technology_object_content` ([Technologieobjekt-Referenz](TECHNOLOGY_OBJECTS_SUMMARY.de.md)) den CPU-Schutz-Lesezugriff `get_plc_security` ([CPU-Schutz-Referenz](PLC_SECURITY_SUMMARY.de.md); nicht live getestet) und den DB-Snapshot-Lesezugriff `get_db_snapshot` ([Referenz der BetterTIA-Operationen](BETTERTIA_ADOPTED_OPERATIONS_SUMMARY.de.md); nicht live getestet), sowie die SIMATIC-SD-Lesezugriffe `get_block_content` Format `sd` und `list_networks` ([SIMATIC-SD-Referenz](PLC_SIMATIC_SD_SUMMARY.de.md); nicht live getestet) und die Lesezugriffe der Änderungserkennung `get_plc_checksums` und `get_fingerprints` ([Offline-Änderungserkennung](PLC_OPERATIONS_SUMMARY.md#offline-change-detection-plc_read); nicht live getestet), und der Lesezugriff des Bausteinschutzes `get_block_protection` ([Know-how-Schutz und Schreibschutz von Bausteinen](PLC_OPERATIONS_SUMMARY.md#block-know-how-and-write-protection-plc_readplc_write); nicht live getestet), und der Lesezugriff der Projektsprachen `get_project_languages` ([Projektsprachen und Projekttexte](PLC_OPERATIONS_SUMMARY.md#project-languages-and-project-texts-plc_readplc_write); nicht live getestet); Die Einstellungsfamilie ergänzt `get_cpu_settings`, `get_umac`, `get_cpu_certificates`, `get_safety_validation` und `check_activation_test` (siehe [PLC_SETTINGS_SUMMARY.de.md](PLC_SETTINGS_SUMMARY.de.md)).
`read_cross_references` ist ein eigenständiges Tool. `execute_read_batch` wurde außer Dienst gestellt.
Hardware-/Kataloglesezugriffe nutzen `network_read`. Lesezugriffe auf WinCC-Unified-HMIs nutzen `hmi_read`; Schreibzugriffe auf Variablentabellen, Variablen und Meldungen nutzen `hmi_write`. Siehe [HMI_OPERATIONS_SUMMARY.md](HMI_OPERATIONS_SUMMARY.de.md).

Projektbindung, Status, Durchsuchen des Projektbaums und Übersetzen sind getrennte Tools: `bind_project`, `get_project_status`, `browse_project_tree` und `compile_check`. Die ersten drei sind in jedem Modus verfügbar; Übersetzen, Lebenszyklus, `plc_download`, `plc_online_write`, `plc_tia_online` und `plc_station_upload` sind in read-write und full verfügbar. Full fügt keine Tools oder Operationen hinzu; das Openness-PLC-Run/Stop (`start_plc`/`stop_plc`) wurde entfernt, und der Betriebszustand einer realen CPU ändert sich nur über `plc_online_write change_operating_mode` über die Web API. Siehe [Installation](../guides/installation.md#access-modes) für die Oberflächen 13/33/33 und die Bestätigungsrichtlinie.

`plc_download` lädt die PLC des gebundenen eigenständigen Projekts in die PLC-Simulation (die PC-Schnittstelle `PLCSIM`) oder in eine reale Steuerung (jede andere PC-Schnittstelle) und bestätigt jeden tatsächlichen read-write-Aufruf; der Zugriffsmodus ist die einzige Kontrolle über Hardware-Downloads. Siehe die [PLC-Download-Referenz](PLC_DOWNLOAD_SUMMARY.de.md); sie ist nicht live getestet.

`plc_tia_online` (online/offline gehen auf genau dem Downloadpfad) und `plc_station_upload` (eine Station in das Offline-Projekt hochladen) sind in read-write und full verfügbar, bestätigen jeden tatsächlichen read-write-Aufruf und beantworten TIA-Portal-Bestätigungen per `portalConfirmations` wie `plc_download`; `compare_to_online` nutzt die neue Fähigkeit `OnlineObserve` (jeder Modus). Diese Openness-Tools sind von den Web-API-Tools `plc_online_read`/`plc_online_write` verschieden. Siehe die [Referenz PLC online über Openness](PLC_ONLINE_OPENNESS_SUMMARY.de.md); sie ist nicht live getestet.

`library_read` (jeder Modus) liest die Projektbibliothek und die geöffneten globalen Bibliotheken (Baum, Typen, Versionen, Instanzen, Aktualisierungsprüfung, Vergleich); `library_write` (read-write und full) öffnet, holt ab, erstellt, speichert und schließt globale Bibliotheken (eine Abfrage pro read-write-Aufruf, der einen solchen Eintrag enthält), erfasst, kopiert, löscht, benennt um und instanziiert Kopiervorlagen, instanziiert Typversionen, aktualisiert das Projekt und bearbeitet, gibt frei, verwirft, setzt als Standard oder löscht Typversionen. Siehe die [Bibliotheksreferenz](LIBRARY_OPERATIONS_SUMMARY.de.md).

#### Schreiboperationen

`plc_write` unterstützt:

`update_block_logic`, `update_type_content`, `create_block`, `delete_block`, `create_block_group`, `delete_block_group`, `create_tag_table`, `delete_tag_table`, `create_tag`, `update_tag`, `delete_tag`, `move_tag`, `create_user_constant`, `update_user_constant`, `delete_user_constant`, die Schreibzugriffe auf Beobachtungs-/Forcetabellen (`create_watch_table`, `delete_watch_table`, `create_watch_table_group`, `delete_watch_table_group`, `update_watch_table_content`, `update_force_table_content`) und `update_program` ([Referenz PLC online über Openness](PLC_ONLINE_OPENNESS_SUMMARY.md#plc_write-operations)), die Schreibzugriffe für Textlisten, Meldungstexte, Meldeklassen, Überwachungen und ProDiag (`update_text_list`, `delete_text_list`, `update_text_list_cells`, `update_alarm_text_cells`, `update_supervision_cells`, die Meldungsoperationen `export_*` und `import_*`, `set_prodiag_assignment`, `export_prodiag_info`; [Referenz PLC-Meldungen und Textlisten](PLC_ALARMS_TEXTLISTS_SUMMARY.de.md), nicht live getestet), die Schreibzugriffe für externe Quellen, Gruppen, Software Units und Dateiexporte (`import_external_source`, `generate_blocks_from_source`, `delete_external_source`, `export_source_file`, `rename_block_group`, `rename_type_group`, `rename_tag_table_group`, `rename_watch_table_group`, `create_type_group`, `delete_type_group`, `create_tag_table_group`, `delete_tag_table_group`, `create_software_unit`, `delete_software_unit`, `rename_software_unit`, `export_tag_tables_csv`, `export_watch_tables_csv`, `export_force_tables_csv`, `export_blocks`; [PLC_SOURCES_FOLDERS_SUMMARY.md](PLC_SOURCES_FOLDERS_SUMMARY.de.md)) und die Technologieobjekt-Schreibzugriffe `create_technology_object`, `delete_technology_object`, `rename_technology_object`, `create_technology_object_group`, `delete_technology_object_group`, `rename_technology_object_group`, `set_technology_object_parameter`, `connect_technology_object_hardware`, `disconnect_technology_object_hardware`, `export_technology_object`, `import_technology_object` und `update_technology_object_content` ([Technologieobjekt-Referenz](TECHNOLOGY_OBJECTS_SUMMARY.de.md); nicht live getestet), und die CPU-Schutz-Schreibzugriffe `set_plc_access_level`, `protect_plc_confidential_data`, `change_plc_confidential_data_password` und `disable_plc_confidential_data_protection` mit Passwörtern aus Passwortquellen ([CPU-Schutz-Referenz](PLC_SECURITY_SUMMARY.de.md); nicht live getestet), und die von BetterTIA übernommenen Operationen `create_db_snapshot`, `set_network_text`, `replace_network_text`, `rename_members`, `rename_tags`, `rename_objects`, `replace_tag_comments` ([Referenz der BetterTIA-Operationen](BETTERTIA_ADOPTED_OPERATIONS_SUMMARY.de.md); nicht live getestet), und die Variablentabellendateien `export_tag_table`, `import_tag_table` ([dieselbe Referenz](BETTERTIA_ADOPTED_OPERATIONS_SUMMARY.md#tag-tables-and-wincc-unified-text-lists-as-files-owner-additions); nicht live getestet), sowie die SIMATIC-SD-Schreibzugriffe `update_block_logic` Format `sd`, `insert_network`, `replace_network`, `delete_network` und `create_block_from_document` ([SIMATIC-SD-Referenz](PLC_SIMATIC_SD_SUMMARY.de.md); nicht live getestet). Inhaltsaktualisierungen nehmen `content` zusammen mit `expectedContentHash` aus einem `plc_read`. Netzwerkbereitstellung/-konfiguration, Baugruppen in Baugruppenträger/Steckplatz, Hardwareparameter, E/A-Adressen und der Export der E/A-Zuordnung nutzen `network_write`; siehe [HARDWARE_MODULES_SUMMARY.md](HARDWARE_MODULES_SUMMARY.de.md). Die Einstellungsfamilie ergänzt die Schreiboperationen aus Issue #15 `set_account_locking`, `set_plc_password_policy`, `set_legacy_plc_password_policy`, `set_syslog`, `add_syslog_server`, `remove_syslog_server`, `create_project_syslog_server`, `update_project_syslog_server`, `delete_project_syslog_server`, `set_system_web_pages`, `set_default_web_application`, `set_watch_table_access`, `remove_watch_table_access`, `set_force_table_access`, `remove_force_table_access`, `set_display_password`, `set_front_panel_display`, `set_plc_project_language`, `set_ip_accessibility`, `generate_web_blocks` sowie die Schreiboperationen aus Issue #13 `create_webserver_user`, `update_webserver_user`, `delete_webserver_user`, `update_simple_webserver_user`, `create_opcua_user`, `set_opcua_user_password`, `delete_opcua_user` sowie die Schreiboperationen aus Issue #12 `create_custom_role`, `update_custom_role`, `delete_custom_role`, `assign_engineering_right`, `unassign_engineering_right`, `assign_device_function_right`, `unassign_device_function_right`, `create_project_user`, `update_project_user`, `delete_project_user`, `assign_user_role`, `unassign_user_role`, `set_anonymous_user`, `create_custom_device_function_right`, `update_custom_device_function_right`, `delete_custom_device_function_right`, `update_umc_user`, `delete_umc_user`, `set_umac_password_policy` sowie die Schreiboperationen aus Issue #14 `set_certificate_manager`, `create_cpu_certificate`, `import_cpu_certificate`, `export_cpu_certificate`, `delete_cpu_certificate`, `assign_cpu_certificate`, `unassign_cpu_certificate`, `set_certificate_management`, `add_certificate_supported_service`, `update_certificate_supported_service`, `remove_certificate_supported_service` sowie die Schreiboperation aus Issue #21 `generate_safety_printout` sowie die Schreiboperationen aus Issue #25 `create_activation_test`, `delete_activation_test`, `change_activation_test_evaluation_device`, `create_activation_test_group`, `delete_activation_test_group`, `create_safety_function`, `update_safety_function`, `delete_safety_function`, `reset_safety_function_test_result`, `create_safety_function_condition`, `update_safety_function_condition`, `delete_safety_function_condition`, `export_activation_test`, `generate_activation_test_report` (siehe [PLC_SETTINGS_SUMMARY.de.md](PLC_SETTINGS_SUMMARY.de.md)).

`hmi_write` unterstützt Erstellen/Aktualisieren/Löschen für Unified-Variablentabellen, -Variablen und Bit-/Analogmeldungen;
siehe die [Referenz der neun Operationen](HMI_OPERATIONS_SUMMARY.md#writes-hmi_write), dazu neun von BetterTIA übernommene Schreibzugriffe für Bilder, Skripte und
Batch-Umbenennungen ([Referenz der BetterTIA-Operationen](BETTERTIA_ADOPTED_OPERATIONS_SUMMARY.md#wincc-unified-screens-scripts-and-batch-renames-hmi_write); nicht live getestet)
und die Dateien für Anwender-Textlisten `export_text_lists` / `import_text_lists` (Member zur Laufzeit gebunden, von
Siemens für Unified nicht dokumentiert; [dieselbe Referenz](BETTERTIA_ADOPTED_OPERATIONS_SUMMARY.md#tag-tables-and-wincc-unified-text-lists-as-files-owner-additions); nicht live getestet),
und die Schreibzugriffe für klassisches WinCC `export_classic_objects`, `import_classic_objects`, `delete_classic_object`,
`create_classic_folder`, `delete_classic_folder` mit dem Lesezugriff `list_classic_objects` ([klassisches WinCC und Unified-Erweiterungen](HMI_CLASSIC_UNIFIED_EXTENSIONS_SUMMARY.de.md); nicht live getestet),
und die Unified-Schreibzugriffe für Ereignisse und Dynamisierungen `set_event_script`, `delete_event_script`, `set_dynamization_script`,
`set_dynamization_flashing`, `delete_dynamization` mit den Lesezugriffen `list_screen_item_events`, `list_screen_item_dynamizations` ([dieselbe Referenz](HMI_CLASSIC_UNIFIED_EXTENSIONS_SUMMARY.de.md#wincc-unified-ereignisse-und-dynamisierungen-issue-29); nicht live getestet),
und die Unified-Schreibzugriffe für Audit-Klassen, Runtime-Einstellungen und Anlagensichten `create_audit_class`, `delete_audit_class`,
`set_audit_class_property`, `set_runtime_setting`, `create_plant_view`, `delete_plant_view`, `create_plant_view_node`,
`delete_plant_view_node`, `set_plant_object_interface_property` mit dem Lesezugriff `list_plant_views` ([dieselbe Referenz](HMI_CLASSIC_UNIFIED_EXTENSIONS_SUMMARY.de.md#wincc-unified-audit-klassen-runtime-einstellungen-anlagensichten-issue-29); nicht live getestet).

### Tools für den Projektlebenszyklus

Der Server stellt außerdem sechs Lebenszyklus-Tools mit jeweils einem einzigen Zweck bereit:

| Tool | Verhalten |
|---|---|
| `open_project` | Öffnet ein Projekt und bindet die MCP-Sitzung daran. |
| `create_project` | Erstellt ein Projekt und bindet die Sitzung daran. |
| `save_project` | Speichert das aktive Projekt. |
| `save_project_as` | Speichert eine Kopie und bindet die Sitzung neu an die Kopie. |
| `archive_project` | Archiviert ein Projekt am angeforderten Ort. |
| `close_project` | Schließt das aktive Projekt und hebt die Sitzungsbindung auf. |

`get_project_status`, `browse_project_tree` und `compile_check` sind eigenständige Projekttools und keine Batch-Operationen.

### PLC-Online-Tools (S7-1500 Web API)

`plc_online_read` (jeder Modus) und `plc_online_write` (read-write und full) beobachten und ändern reale
S7-1500-CPUs online über den offiziellen Web-API-Client von Siemens, innerhalb des Hosts. Die PLCs werden vom
Benutzer in einer `--plc-webapi-config`-Zieldatei mit ihren Credential-Variablen und Zertifikatsregeln
aufgeführt; Tools nennen nur ein `target`. `plc_online_write` bestätigt jeden tatsächlichen read-write-Aufruf. Sie sind
noch nicht live getestet. Siehe die [PLC-Online-Referenz](PLC_ONLINE_WEBAPI_SUMMARY.de.md) für die Voraussetzungen
in TIA Portal, das Dateiformat, die Operationen, Guards, die Herkunft und die Schritte der Live-Abnahme.

### Siemens-Dokumentation (`siemens_docs`)

`siemens_docs` (jeder Modus, kein TIA Portal nötig) durchsucht und liest die offizielle Siemens-Dokumentation auf
docs.tia.siemens.cloud und docs.industrial-operations-x.siemens.cloud: `search`, `read_page` (Markdown, seitenweise),
`get_toc`, `list_publications`. Nur die freigegebenen Hosts werden kontaktiert; Referenzen werden in Ids aufgelöst,
nie selbst abgerufen. Auf dem Windows-PC nicht live getestet; die Endpunkte wurden live aus dem Cloud-Container
geprüft. Siehe die [Referenz zur Siemens-Dokumentation](SIEMENS_DOCS_SUMMARY.de.md).

### S7-PLCSIM-Advanced-Tools

`plcsim_read` (jeder Modus), `plcsim_write` und `plcsim_test` (read-write und full) steuern
virtuelle S7-1500-CPUs von S7-PLCSIM Advanced über einen separaten PLCSIM-Worker; sie berühren nie TIA
Portal oder die Projektbindung. Sie sind noch nicht live getestet. Siehe die
[Referenz der PLCSIM-Operationen](PLCSIM_OPERATIONS_SUMMARY.de.md) für Operationen, die per Reflection angesprochene API-Oberfläche
und ihre Herkunft, Voraussetzungen und die Schritte der Live-Abnahme.

## Schreibsicherheit

Lebenszyklus-, Network-, PLC- und HMI-Schreibzugriffe, `plc_download` und `plc_online_write` nutzen abgesicherte Schreibzugriffe in einem einzigen Aufruf. Kein Tool nutzt Sicherheitstoken.

- `plc_online_write` zeigt mit `dryRun:true` eine Vorschau, ohne eine PLC zu kontaktieren; jeder tatsächliche read-write-Aufruf fragt einmal nach (alle Einträge in einer Abfrage), full läuft unter Richtlinie, und jeder betretene Aufruf wird mit `recordKind:"plc_online"` auditiert. Siehe die [PLC-Online-Referenz](PLC_ONLINE_WEBAPI_SUMMARY.md#access-confirmation-and-audit).

- PLC-Schreibzugriffe zeigen mit `dryRun:true` eine Vorschau; ein weggelassenes `dryRun` führt aus, ohne jede Server-Elicitation. Ein doppelter Singleton-OB und die Safety-Anmelderegel (ein Safety-Passwort ist gesetzt, nicht angemeldet) blockieren den Aufruf; nicht lesbare Nachweise, Löschungen und Adressüberschneidungen sind Info, und den Rest entscheidet TIA Portal. Inhaltsschreibzugriffe benötigen `expectedContentHash`; veralteter Inhalt schlägt mit `state_changed` fehl. Siehe die [PLC-Referenz](PLC_OPERATIONS_SUMMARY.md#plc-writes-plc_write).
- Der Lebenszyklus nutzt `dryRun` mit Operationseingaben und kein öffentliches Bestätigungsarray oder Token. Jeder tatsächliche read-write-Aufruf fragt einmal über eine Formular-Elicitation nach; full läuft unter Richtlinie ohne Server-Elicitation. Block-Guards lehnen in jedem Modus ab; Probeläufe ändern nichts und lösen keine Elicitation aus. Siehe die [Lebenszyklus-Referenz](PROJECT_OPERATIONS_SUMMARY.md#lifecycle-operations).
- Network zeigt mit `dryRun:true` eine Vorschau; `dryRun:false` oder ein weggelassenes dryRun führt standardmäßig aus, ohne jede Server-Elicitation. Es gelten eine exakte, bereits geöffnete verifizierte Bindung, vollständige Guards und typisierte Nachprüfungen; Abbruch bei Fehler, kein Batch-Rollback und keine automatische Wiederholung. Siehe den [Network-Vertrag](NETWORK_OPERATIONS_SUMMARY.md#network_write-envelope) zur SDK-Ablehnung vor dem Eintritt gegenüber kanonischen Ablehnungen nach dem Eintritt.
- HMI zeigt mit `dryRun:true` eine Vorschau; ein weggelassenes dryRun führt Schreibzugriffe auf Variablentabellen, Variablen und Meldungen ohne Elicitation aus. Es gelten bestehende Verbindungen, vollständige Löschnachweise und typisierte Aufräum- bzw. Teilergebnisse auf eigenständigen und lokalen Sitzungswurzeln. Siehe die [HMI-Referenz](HMI_OPERATIONS_SUMMARY.md#writes-hmi_write).
- Ein Schreibzugriff mit mehreren Einträgen ist sequenziell und nicht transaktional. Die Anwendung stoppt beim ersten Fehler; abgeschlossene Einträge bleiben angewendet und spätere Einträge werden als `skipped` markiert.
- Audit-JSONL liegt unter `%LOCALAPPDATA%\TiaMcpServer\audit`. Audit v2 für Lebenszyklus, Network, PLC und HMI zeichnet jeden betretenen Aufruf auf, einschließlich Vorschauen und Ablehnungen, mit Bestätigung durch `user`, `policy` oder `none`; eine SDK-Ablehnung vor dem Eintritt hat kein Schreibaudit. Der alte Schreibaudit-Strom wurde außer Dienst gestellt.

Leseantworten können `warnings` für unvollständige oder eingeschränkte Daten enthalten. Hardwarelesezugriffe liefern zusätzlich `messages` auf Payload-Ebene für nicht lesbare Member. Aufrufer sollten diese Felder als Teil des Ergebnisvertrags behandeln, statt fehlende Werte lokal aufzufüllen.

## Referenz nach Bereichen

| Bereich | Referenz |
|---|---|
| Projekt- und Portal-Lebenszyklus | [PROJECT_OPERATIONS_SUMMARY.md](PROJECT_OPERATIONS_SUMMARY.de.md) |
| Geräte | [DEVICES_OPERATIONS_SUMMARY.md](DEVICES_OPERATIONS_SUMMARY.de.md) |
| PLC-Software | [PLC_OPERATIONS_SUMMARY.md](PLC_OPERATIONS_SUMMARY.de.md) |
| PLC-Download in PLCSIM oder reale Hardware (`plc_download`) | [PLC_DOWNLOAD_SUMMARY.md](PLC_DOWNLOAD_SUMMARY.de.md) |
| PLC online über die S7-1500 Web API (`plc_online_read`, `plc_online_write`) | [PLC_ONLINE_WEBAPI_SUMMARY.md](PLC_ONLINE_WEBAPI_SUMMARY.de.md) |
| PLC online über Openness: Vergleich, Online-Zustand, online/offline gehen, Station-Upload, Beobachtungs-/Forcetabellen (`plc_tia_online`, `plc_station_upload`) | [PLC_ONLINE_OPENNESS_SUMMARY.md](PLC_ONLINE_OPENNESS_SUMMARY.de.md) |
| S7-PLCSIM Advanced (`plcsim_read`, `plcsim_write`, `plcsim_test`) | [PLCSIM_OPERATIONS_SUMMARY.md](PLCSIM_OPERATIONS_SUMMARY.de.md) |
| Siemens-Dokumentation (`siemens_docs`) | [SIEMENS_DOCS_SUMMARY.md](SIEMENS_DOCS_SUMMARY.de.md) |
| HMI | [HMI_OPERATIONS_SUMMARY.md](HMI_OPERATIONS_SUMMARY.de.md), [HMI_CLASSIC_UNIFIED_EXTENSIONS_SUMMARY.md](HMI_CLASSIC_UNIFIED_EXTENSIONS_SUMMARY.de.md) |
| Eigenständige VCI-Arbeitsbereiche (`vci_read`, `vci_write`) | [VCI_OPERATIONS_SUMMARY.md](VCI_OPERATIONS_SUMMARY.de.md) |
| Projekt- und globale Bibliotheken, Typen, Kopiervorlagen (`library_read`, `library_write`) | [LIBRARY_OPERATIONS_SUMMARY.md](LIBRARY_OPERATIONS_SUMMARY.de.md) |
| „Claude in TIA“: TIA-Portal-Add-In, Übergabe an Claude über Ein- und Ausgang (`tia_inbox`), Claude-Brücke | [CLAUDE_IN_TIA.md](CLAUDE_IN_TIA.de.md) |
| MCP-Ressourcen (eingebettete Referenzdokumente, Operationsreferenz) und Prompts (Arbeitsabläufe) | [MCP_RESOURCES_PROMPTS_SUMMARY.md](MCP_RESOURCES_PROMPTS_SUMMARY.de.md) |
| Externe Quellen, Projektbaumordner (Suchen, Umbenennen, Erstellen/Löschen von Typ- und Variablentabellengruppen), Software Units (Erstellen, Löschen, Umbenennen), Variablenkommentare, CSV- und Batch-Dateiexporte, Scan erreichbarer Geräte | [PLC_SOURCES_FOLDERS_SUMMARY.md](PLC_SOURCES_FOLDERS_SUMMARY.de.md) |
| PLC-Meldungen, Meldeklassen, Textlisten, ProDiag-Überwachungen und -Zuordnungen (`plc_read`, `plc_write`) | [PLC_ALARMS_TEXTLISTS_SUMMARY.md](PLC_ALARMS_TEXTLISTS_SUMMARY.de.md) |
| Technologieobjekte: Auflisten, Parameter, Hardwareverbindungen, Export/Import (`plc_read`, `plc_write`) | [TECHNOLOGY_OBJECTS_SUMMARY.md](TECHNOLOGY_OBJECTS_SUMMARY.de.md) |
| CPU-Schutz: jede Zugriffsstufe mit Stufenpasswörtern, Schutz vertraulicher Konfigurationsdaten aktivieren/ändern/aufheben aus Passwortquellen, Hardware-Übersetzen (`plc_read`, `plc_write`, `compile_check`) | [PLC_SECURITY_SUMMARY.md](PLC_SECURITY_SUMMARY.de.md) |
| CPU- und Projekteinstellungen (Issue #15), Webserver- und OPC-UA-Benutzer (Issue #13), UMAC (Issue #12), CPU-Zertifikate (Issue #14), Safety-Ausdruck (Issue #21), Safety Validation Assistant (Issue #25): Kontosperre, Passwortrichtlinien, Syslog, Webseiten, Zugriff auf Beobachtungs-/Forcetabellen, Display-Passwort, Frontdisplay, Projektsprache, IP-Erreichbarkeit (`plc_read`, `plc_write`) | [PLC_SETTINGS_SUMMARY.de.md](PLC_SETTINGS_SUMMARY.de.md) |
| Vervollständigung der V21-API (Paket G): dokumentierte V21-Methoden in den bestehenden Tools, mit der Liste der ausgelassenen (`plc_write`, ...) | [V21_API_COMPLETION_SUMMARY.de.md](V21_API_COMPLETION_SUMMARY.de.md) |
| Fehlersichere (F-)PLCs: Safety Administration, F-Baustein-Signaturen, Safety-Anmeldung und -Passwort, F-Ablaufgruppen, Antwort für den F-Programm-Download (`plc_read`, `plc_write`, `plc_download`) | [PLC_SAFETY_SUMMARY.md](PLC_SAFETY_SUMMARY.de.md) |
| Von rickgaiser/TiaMcp gemeldete Fallstricke in TIA Portal V21: Guards, Lese-Flags, Export-Ledger, Liste für die Live-Abnahme | [V21_PITFALLS.md](V21_PITFALLS.de.md) |
| SIMATIC SD (.s7dcl/.s7res): sd-Lesezugriffe, `list_networks`, Netzwerk einfügen/ersetzen/löschen, `update_block_logic` sd, `create_block_from_document` (`plc_read`, `plc_write`) | [PLC_SIMATIC_SD_SUMMARY.md](PLC_SIMATIC_SD_SUMMARY.de.md) |
| Netzwerke und Topologie | [NETWORK_OPERATIONS_SUMMARY.md](NETWORK_OPERATIONS_SUMMARY.de.md) |
| SIMATIC-Antriebe / Startdrive | [SIMATIC_DRIVES_OPERATIONS_SUMMARY.md](SIMATIC_DRIVES_OPERATIONS_SUMMARY.de.md) |
| PLC-Import-/Exportformate | [IMPORT_EXPORT_OPTIONS_SUMMARY.md](IMPORT_EXPORT_OPTIONS_SUMMARY.de.md) |
| Multiuser-Inventar über `bind_project`, passiver Kontext und Abnahmegrenze | [MULTIUSER_OPERATIONS_SUMMARY.md](MULTIUSER_OPERATIONS_SUMMARY.de.md) |
| Project-Server-Verwaltung (`project_server_write`): Verbindungen, Hochladen, lokale Sitzungen | [PROJECT_SERVER_WRITE_SUMMARY.md](PROJECT_SERVER_WRITE_SUMMARY.de.md) |
| Teamcenter | [TEAMCENTER_OPERATIONS_SUMMARY.md](TEAMCENTER_OPERATIONS_SUMMARY.de.md) |
| TestSuite | [TESTSUITE_OPERATIONS_SUMMARY.md](TESTSUITE_OPERATIONS_SUMMARY.de.md) |

## Laufzeitvoraussetzungen

Der Openness-Worker ist der einzige Prozess, der Siemens-Assemblies lädt. Die Nutzung des Servers erfordert Windows, TIA Portal V21 mit aktiviertem Openness, die Mitgliedschaft in der Siemens-Benutzergruppe TIA Openness und die unterstützten .NET-Laufzeiten. Der Worker kommuniziert mit dem .NET-10-Host über zeilengetrenntes JSON und wird über Timeouts und Abstürze hinweg überwacht. Das frameworkabhängige globale Tool erfordert eine unterstützte .NET-10-Laufzeit; das separate eigenständige `win-x64`-Archiv enthält die Host-Laufzeit. Beide Layouts behalten den Openness-Worker für .NET Framework 4.8.

Diese Referenz beschreibt den Softwarevertrag. Ein erfolgreicher Build oder automatisierter Test ersetzt nicht die Validierung gegen die Ziel-Installation von TIA Portal V21, das Projekt, die Gerätekonfiguration oder die Hardware.
