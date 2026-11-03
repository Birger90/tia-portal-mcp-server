# Operationen für SIMATIC-Antriebe und Startdrive

**Sprache:** [English](SIMATIC_DRIVES_OPERATIONS_SUMMARY.md) · Deutsch

## Unterstützungsstatus

Der aktuelle MCP-Vertrag enthält keine antriebsspezifische Operation. Generisches Durchsuchen der Hardware, Hardwarelesezugriffe, Suche im Hardwarekatalog, Bereitstellung von Netzwerkgeräten und Übersetzungsprüfungen können auf einem Projekt arbeiten, das einen Antrieb enthält, sie legen aber das Objektmodell des Antriebs nicht offen.

## Aktuelle Grenzen

Die folgenden Bereiche von Startdrive, SINAMICS, SIMATIC Drive Controller und PROFIdrive liegen außerhalb der aktuellen Oberfläche:

- Navigation in Antriebsobjekten und Behandlung von Aktivierung/Typ.
- Lesen und Schreiben von SINAMICS-Parametern.
- Telegramm-Operationen zum Suchen, Einfügen, Löschen und für die Größe.
- Inbetriebnahme über das Drive Function Interface, Motor-/Geberkonfiguration und DFI-Operationen.
- Safety- und Security-Objekte des Antriebs.
- Startdrive-spezifische Konfiguration für Download und Upload.
- PROFIdrive-spezifisches Engineering über die generischen Netzwerkfelder in [NETWORK_OPERATIONS_SUMMARY.md](NETWORK_OPERATIONS_SUMMARY.de.md) hinaus.

Für die generischen Geräte- und Netzwerkoperationen, die rund um einen Antrieb verfügbar sind, siehe [DEVICES_OPERATIONS_SUMMARY.md](DEVICES_OPERATIONS_SUMMARY.de.md) und [NETWORK_OPERATIONS_SUMMARY.md](NETWORK_OPERATIONS_SUMMARY.de.md).
