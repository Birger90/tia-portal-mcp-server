# TIA-Portal-Geräteoperationen

**Sprache:** [English](DEVICES_OPERATIONS_SUMMARY.md) · Deutsch

## Unterstützte Operationen

| Einstiegspunkt | Operation | Eingaben und Verhalten |
|---|---|---|
| `network_read` | `read_hardware_config` | Findet Geräte rekursiv sowohl auf der Projektwurzel als auch in verschachtelten Gerätegruppen und liest dann Geräteelemente, Netzwerkschnittstellen, Knoten, Subnetze und IO-Systeme. Optionaler Filter `deviceName`, `plcName`, optional zuschaltbare strukturierte E/A-Extraktion (`includeIoDetails`, `includeTagMatches`) und begrenzte Seitenaufteilung (`pageSize`, `cursor`) — siehe [NETWORK_OPERATIONS_SUMMARY.md](NETWORK_OPERATIONS_SUMMARY.de.md). |
| `network_read` | `search_equipment_catalog` | Erfordert `query`; akzeptiert ein begrenztes `maxResults`; gibt Katalog-Typkennungen für infrage kommende Geräte zurück. |
| `network_write` (abgesichert) | `add_network_device` | Erfordert eine exakte Katalog-`typeIdentifier` und `deviceName`; akzeptiert optional `deviceItemName`. |
| `network_write` (abgesichert) | `configure_network_device` | Erfordert `target: { deviceName, nodeId }`, das einen vorhandenen Knoten benennt, und `changes` mit mindestens einem von `ipAddress`, `subnetMask`, `pnDeviceNameAutoGeneration`, `pnDeviceName`, `subnet: { subnetId }` und `ioSystem: { subnetId, number }`. |
| `network_read` | `list_plug_locations`, `find_module_types`, `find_container_types`, `list_io_addresses` | Freie Steckplätze eines Geräts oder Geräteelements, Baugruppenversionen und Containertypen aus `ModuleInformationProvider` sowie die `Address`-Objekte eines Geräteelements. Siehe [HARDWARE_MODULES_SUMMARY.md](HARDWARE_MODULES_SUMMARY.de.md). |
| `network_write` (abgesichert) | `plug_device_item`, `move_device_item`, `copy_device_item`, `delete_device_item`, `change_device_item_type`, `set_hardware_attributes`, `set_io_address`, `export_io_map` | Baugruppen in Baugruppenträger/Steckplatz (`CanPlug*`/`Plug*`), Löschen, Typ ändern, typisierte Schreibzugriffe auf Hardwareparameter (gesammelt per `SetAttributes` mit `AttributeDelegate` oder sequenziell), E/A-Anfangsadressen und der CSV-Export der E/A-Zuordnung. Siehe [HARDWARE_MODULES_SUMMARY.md](HARDWARE_MODULES_SUMMARY.de.md); nicht live getestet. |
| `browse_project_tree` | `browse_project_tree` | Findet dieselben direkten und gruppierten Geräte rekursiv als flache `Device`-Knoten; akzeptiert optional `startSelector`, `depth`, `pageSize` und `cursor`. |

`add_network_device` und `configure_network_device` sind Datenschreibzugriffe. Vorschau mit `dryRun:true`; Ausführung mit `dryRun:false` oder weggelassenem `dryRun`, ohne Server-Elicitation. Eine exakte, bereits geöffnete verifizierte Bindung ist erforderlich; keine Token, kein Rollback und keine automatische Wiederholung. Die Katalogkennung und der Gerätename werden validiert, bevor der Worker die Änderung ausführt.

## Gerätedatenmodell

Der Hardware-Lesepfad ist eine Inspektionsoberfläche. Der Schreibpfad beschränkt sich auf katalogbasiertes Erstellen von Geräten und ausgewählte Identitätsfelder von Gerät/Netzwerk:

- Das Erstellen eines Geräts nutzt eine Katalog-`typeIdentifier`.
- `deviceItemName` gilt für das Erstellen und ist bei Weglassen standardmäßig `deviceName`; ein leerer oder nur aus Leerzeichen bestehender Wert ist ein `validation_error`.
- Hardwareergebnisse können verschachtelte Geräteelemente und Netzwerkmetadaten enthalten.
- Hardwareparameter werden mit `inspect_network_object` (Ziele vom Typ deviceItem) gelesen und mit `set_hardware_attributes` geschrieben (Gerät oder Geräteelement; Namen, Zugriff und Typen kommen aus `GetAttributeInfos`).

## Aktuelle Grenzen

Die aktuelle Oberfläche bietet nicht:

- Erstellen, Löschen, Umbenennen von Gerätegruppen oder Verschieben von Geräten zwischen Gruppen. Vorhandene gruppierte Geräte werden von Leseoperationen gefunden, aber Gruppenordner werden nicht als Baumknoten offengelegt.
- Stecken in ein Gerät oder eine Baugruppe, das bzw. die früher im selben `network_write`-Aufruf erstellt wurde (ein
  Hardware-Snapshot plant den Aufruf).
- Schreibzugriffe auf Kanäle oder Adresscontroller, `AssignProcessImageToOrganizationBlock`,
  Hardwarekennungen und Diagnosedaten. E/A-Anfangsadressen, Prozessabbild, taktsynchroner Betrieb und
  Alarm-OB-Nummer sind mit `set_io_address` schreibbar.
- Startdrive `DriveItemHardwareModule.ChangeType`, Verbindungen zu Erweiterungsbaugruppenträgern, PSC-Export und die
  übrigen in [HARDWARE_MODULES_SUMMARY.md](HARDWARE_MODULES_SUMMARY.md#omitted-and-why) aufgeführten Auslassungen.
- Eine Software-Container-API auf Geräteebene; PLC-Bausteine, -Variablen und -Konstanten nutzen die in [PLC_OPERATIONS_SUMMARY.md](PLC_OPERATIONS_SUMMARY.de.md) beschriebenen PLC-Operationen.

## Verwandte Operationen

Nutze [NETWORK_OPERATIONS_SUMMARY.md](NETWORK_OPERATIONS_SUMMARY.de.md) für die Konfiguration der Netzwerkidentität und [README.md](README.de.md) für Batch-Sicherheit und Warnungen in Antworten.
