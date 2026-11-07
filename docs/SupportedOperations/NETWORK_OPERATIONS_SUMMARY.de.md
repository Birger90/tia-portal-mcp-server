# TIA-Portal-Netzwerk- und Topologieoperationen

**Sprache:** [English](NETWORK_OPERATIONS_SUMMARY.md) · Deutsch

`network_write` verwendet Vertrag **2.0**; `network_read` bleibt bei **1.0**. Der Kandidat für
native Ablehnungen `75c0568` hat die Offline-Qualifizierung und alle 17 erwarteten Ergebnisse im
begrenzten Wiederholungslauf auf installiertem TIA Portal V21 bestanden. Herkunft, Zeitmessung,
Wiederherstellung und ungetestete Grenzen stehen im [Abnahmebericht vom 2026-10-29](../superpowers/acceptance/reports/2026-10-29-network-write-native-rejections-live-verification.md).
Die spätere reine Host-Nacharbeit zur Ergebnisvalidierung und Tool-Hilfe hat eigene
Offline-Nachweise; sie wurde weder installiert noch neu zeitlich vermessen und fügt keine native
Abnahme hinzu. Ältere datierte Nachweise unten gelten nur für ihre protokollierte Quelle und ihren
Vertrag.

Historischer Status von Phase 3: Snapshot-bezogene Discovery von Netzwerkobjekten und typisierte
schreibgeschützte Inspektion sind auf dem einschichtigen JSON-Vertrag von Phase 2 implementiert. Der
gesondert autorisierte Nachweislauf auf TIA Portal V21 wurde am 2026-08-05 abgeschlossen, aber die
endgültige Stabilisierung steht noch aus, bis eine Designprüfung erfolgt ist, weil nur drei von acht
beobachteten Kommunikationsverbindungen vollständige Selektoren hatten. Messungen, Herkunft,
Aufbewahrungsentscheidung und Abdeckungsgrenzen sind in
[NETWORK_PHASE3_LIVE_ACCEPTANCE.md](NETWORK_PHASE3_LIVE_ACCEPTANCE.de.md) festgehalten.

Historischer Status von Phase 4: Erstellen/Aktualisieren/Löschen von Ethernet- und
PROFIBUS-Subnetzen wurde `network_write` ohne neues MCP-Tool hinzugefügt. Das frühere Audit fand
Vertragslücken; gezielte statische Prüfungen verifizieren die Reparaturen aus PR 1. Der öffentliche
Lauf vom 2026-09-23 stoppte an einer Ablehnung durch eine TIA-Sicherheitsberechtigung beim Löschen
eines verbundenen Ethernet-Subnetzes. Am 2026-09-24 bestand ein frischer, geschützter öffentlicher
Lauf alle acht Lifecycle-Operationen, einschließlich beider Löschungen verbundener Subnetze, und ein
separates abschließendes Lesen beobachtete null Subnetze und insgesamt 81 Hardwaregeräte. Identitäten
der verbliebenen Geräte sowie Attribute von Knoten/IO-Systemen wurden nicht unabhängig zurückgelesen.
Siehe den [begrenzten Live-PASS-Bericht](../superpowers/acceptance/reports/2026-09-21-network-phase4-current-revision-live.md)
und die [Operationsreferenz zu Phase 4](NETWORK_PHASE4_SUBNET_LIFECYCLE.de.md) für die vollständigen
Anfrageformen, schreibbaren Werte, Zielauswahl, Löschsemantik, das minimale Ergebnis und den
Nachweisstatus.

Siehe [../roadmap/network-operations.md](../roadmap/network-operations.md) für den Umfang späterer Phasen.

Ein fataler Portal-Verlust, der aus Discovery, Auswahl, Verbindungs-/Attributlesen oder Mutation im
Netzwerkbereich entweicht, wird an die gemeinsame `portal_lost`-Zuordnung des Workers weitergereicht.
Gewöhnliche unlesbare Nachweise und Fehler optionaler Eigenschaften behalten ihr bisheriges
Verhalten. Der Host macht verifizierte Bindung/Cursor ungültig, ohne zu wiederholen; jede versuchte
Mutation prüfen und vor einem erneuten Versuch explizit neu binden. Bestehende typisierte Ergebnisse
nach der Mutation und Aufräumwarnungen behalten ihre etablierten Verträge. Der
[B-16/Q3-Bericht](../superpowers/acceptance/reports/2026-10-20-b16-q3-worker-portal-loss-live-verification.md) unterscheidet
Nachweise aus Offline-Fehlerinjektion von begrenzten Live-Lesetests, die vom Maintainer akzeptiert
wurden. Unterbrechungen nativer Schreibvorgänge und weitere Fehlerorte bleiben ungetestet.

## Unterstützte Operationen

Das MCP bietet eine begrenzte Oberfläche für Geräte und Netzwerkidentitäten:

| Einstiegspunkt | Operation | Eingaben und Verhalten |
|---|---|---|
| `network_read` | `read_hardware_config` | Ermittelt rekursiv Geräte sowohl auf Projektebene als auch in verschachtelten Gerätegruppen und liest dann deren Netzwerk-DTOs: Schnittstellen, Knoten, Subnetze und IO-Systeme, sofern vorhanden. Optionaler Filter `deviceName` (der Stations-/Root-Gerätename wie in `devices[].name`), optionaler Selektor `plcName` für den Variablenabgleich und optionale strukturierte E/A-Extraktion (`includeIoDetails`, `includeTagMatches`) — siehe "Strukturierte E/A-Map" unten. |
| `network_read` | `search_equipment_catalog` | Durchsucht den Hardwarekatalog vor dem Anlegen nach einem Gerätetyp (`query`, optional `maxResults`). |
| `network_read` | `list_network_objects` | Liefert seitenweise deterministische Zusammenfassungen für einen oder mehrere `objectKinds`; akzeptiert optionale gerätebezogene Filterung, `pageSize` 1-200 und einen opaken Fortsetzungs-`cursor`. Vollständige Identitäten enthalten einen Selektor, der in die Inspektion kopiert werden kann. |
| `network_read` | `inspect_network_object` | Löst genau ein `target` auf, verifiziert dessen erfasste Identitätsnachweise und liefert modellierte und generische Attribute. Optionales `attributeNames` beachtet Groß-/Kleinschreibung, ist duplikatfrei und auf 200 Namen begrenzt. |
| `network_write` | `add_network_device` | Legt ein Gerät aus einem exakten Katalog-`typeIdentifier` an; verlangt `deviceName` und akzeptiert optional `deviceItemName` (leer oder nur Leerraum ist ein `validation_error`). Bewusst flach — es benennt etwas, das noch nicht existiert. Das angelegte Element wird nur über einen eindeutigen exakten Namen unter den obersten Elementen des Geräts verifiziert, daher besteht ein Kopfmodul, dessen Kind seinen Namen wiederholt. |
| `network_write` | `configure_network_device` | Konfiguriert genau einen bestehenden Knoten: `target: { deviceName, nodeId }` plus `changes: { ipAddress?, subnetMask?, pnDeviceNameAutoGeneration?, pnDeviceName?, subnet?: { subnetId }, ioSystem?: { subnetId, number } }`. Siehe "PROFINET-Gerätename" unten. |
| `network_write` | `create_subnet` | Legt ein neues Ethernet- oder PROFIBUS-Subnetz aus `subnet: { name, networkType, highestAddress?, transmissionSpeed? }` an. Nur für PROFIBUS gültige Felder werden für Ethernet abgelehnt. |
| `network_write` | `update_subnet` | Benennt ein bestehendes Subnetz um oder ändert dessen PROFIBUS-Attribute: `target: { kind: "subnet", subnetId }` plus `subnetChanges` (mindestens ein Member). |
| `network_write` | `delete_subnet` | Löscht ein bestehendes Subnetz über das exakte `target: { kind: "subnet", subnetId }`. Verbundene Subnetze sind löschbar; Geräte werden nie gelöscht. |
| `network_write` | `create_io_system` | Legt ein PROFINET-IO-System am Knoten einer CPU an: `target: { kind: "node", deviceName, nodeId, interfacePath }`, wie `read_hardware_config` es meldet, optional `ioSystem: { name }`. **Nicht live getestet.** Siehe "PROFINET-IO-System" unten. |

Hardwaremodul-Operationen (`list_plug_locations`, `find_module_types`, `find_container_types`,
`list_io_addresses`, `plug_device_item`, `move_device_item`, `copy_device_item`,
`delete_device_item`, `change_device_item_type`, `set_hardware_attributes`, `set_io_address`,
`export_io_map`) teilen sich diese Tools und dieselbe Schreib-Pipeline; Vertrag, Guards, Herkunft
und Liste der Live-Abnahmen stehen in [HARDWARE_MODULES_SUMMARY.md](HARDWARE_MODULES_SUMMARY.de.md).
Sie sind nicht live getestet.

`configure_network_device` ist kein allgemeiner Proxy für den Netzwerkeditor. Sein schreibbarer Vertrag ist auf die aufgeführten `changes`-Felder beschränkt, und jeder Selektor ist exakt — siehe "Die Selektorauflösung ist exakt und fail-closed" unten.
`create_subnet`, `update_subnet` und `delete_subnet` sind die Subnetz-Lifecycle-Operationen von
Phase 4, ausführlich beschrieben in [NETWORK_PHASE4_SUBNET_LIFECYCLE.md](NETWORK_PHASE4_SUBNET_LIFECYCLE.de.md).
Jede Batch-Operation im Netzwerkbereich verlangt eine eindeutige, nicht leere `operationId` von
höchstens 256 Zeichen. Das bestehende Limit von 256 Zeichen pro ID ist unverändert. Schreibvorgänge
reservieren außerdem vor der Bindung oder jeder Worker-Aktivität die aggregierte kanonisch kodierte
Form aus Identität/geschützter Zusammenfassung unter 180.000 Zeichen. Stark maskierte IDs können
diese Reserve überschreiten, selbst wenn alle einzelnen IDs gültig sind: Ein Aufruf nach dem Eintritt
in die Pipeline wird mit einem festen `validation_error` ohne Echo, dem Hinweis auf kleinere Aufrufe
und einem Audit-Eintrag abgelehnt. Dies ist weder ein kleineres Limit pro ID noch ein universelles
aggregiertes Limit für Rohzeichen; Lesevorgänge behalten ihre Zulassungsregeln.

### Discovery- und Inspektionsanfragen

```jsonc
{
  "operations": [
    {
      "operationId": "list-device-network",
      "operation": "list_network_objects",
      "objectKinds": ["deviceItem", "networkInterface", "node", "communicationConnection"],
      "deviceName": "ET 200SP station_2",
      "pageSize": 50
    },
    {
      "operationId": "inspect-node",
      "operation": "inspect_network_object",
      "target": {
        "kind": "node",
        "deviceName": "ET 200SP station_2",
        "nodeId": "<node id returned by discovery>"
      },
      "attributeNames": ["Name", "Address"]
    }
  ]
}
```

`objectKinds` ist Pflicht, nicht leer, duplikatfrei und auf `deviceItem`, `networkInterface`,
`node`, `subnet`, `ioSystem` und `communicationConnection` beschränkt. `deviceName` darf nur
verwendet werden, wenn jede angeforderte Art gerätebezogen ist; mit `subnet` oder `ioSystem` ist es
ungültig. Die Standardseitengröße ist 100, beim ersten wie bei fortgesetzten Aufrufen. Öffentliche
Cursor sind opak und vom Host authentifiziert, gebunden an seine exakte Projektbindungs-Epoche und
die Sitzungsidentität des Workers sowie an die normalisierten Filter, die stabile
Elementreihenfolge und den aktuellen Discovery-Snapshot. Ein fehlerhafter oder veralteter Cursor wird
abgelehnt, statt neu zu starten oder stillschweigend auf ein anderes Ziel umgelenkt zu werden.

Jedes Listenelement meldet `kind`, `selectable`, `selector`, die erfassten `evidence` und
`diagnostics`. Wenn eine erforderliche Identität nicht gelesen werden kann, bleibt das Element mit
`selectable:false` und einem null-Selektor sichtbar. Aufrufer dürfen keinen Selektor aus
unvollständigen Nachweisen konstruieren; nach Projektänderungen neu auflisten, weil Selektoren
snapshot-bezogene Locator sind, keine dauerhaften TIA-Bezeichner.

### Fortsetzungs-Cursor für Netzwerkobjekte

Den zurückgegebenen `nextCursor` unverändert verwenden, `objectKinds` und `deviceName` unverändert
lassen und über `network_read` fortsetzen. Ein explizites `pageSize` darf sich innerhalb von
`1..200` ändern. Der Host versiegelt die bestehende Worker-Fortsetzung zusammen mit
Bindungs-ID/-Revision/-Pfad und Sitzungs-ID, Generation, Portal-PID und Projektpfad des Workers. Der
Worker behält seine bestehenden Filter-, Snapshot- und Bereichsprüfungen; dies ist eine
Authentifizierung, keine Verschlüsselung und kein neuer Snapshot-Speicher. Erste und fortgesetzte
Aufrufe verwenden denselben geschützten Host-Pfad.

| Kategorie | Bedeutung und Wiederherstellung |
|---|---|
| `invalid_cursor` | Fehlerhafter, manipulierter, für einen anderen Zweck bestimmter, aus einem früheren Host-Prozess stammender oder veralteter unsignierter öffentlicher Cursor. Ohne den alten Cursor neu beginnen; alte Cursor-Werte werden nicht migriert. |
| `cursor_binding_mismatch` | Die gespeicherte Host-Epoche oder Worker-Identität stimmt nicht mehr überein. Eine neue Folge unter der beabsichtigten verifizierten Bindung beginnen. Eine lokale Ablehnung eines veralteten Cursors erhält die gesunde aktuelle Bindung. |
| `cursor_filter_mismatch` | Der Worker hat geänderte Abfragefilter abgelehnt. Die ursprünglichen Filter wiederholen oder eine neue Folge beginnen. |
| `cursor_snapshot_mismatch` | Die aktuellen Discovery-Nachweise weichen ab. Eine neue Folge beginnen; den alten Offset nicht wiederverwenden. |
| `cursor_out_of_range` | Der Fortsetzungs-Offset liegt außerhalb des aktuellen Ergebnisses. Eine neue Folge beginnen. |
| `protocol_error` | Der Worker lieferte eine fehlerhafte Nutzlast/Identität oder eine leere Fortsetzung ungleich null. Das Element schlägt kanonisch fehl, und spätere unabhängige Lesevorgänge laufen trotzdem. |

Eine erneute Verifizierung per `bind_project` mit demselben Eigentümer, die die Bindungs-Epoche
beibehält, macht einen Cursor nicht ungültig. Ein tatsächlicher Verlust von Worker/Portal behält sein
ursprüngliches Fehler- und Invalidierungsverhalten: den Fehler prüfen, zur Wiederherstellung explizit
binden und eine neue Folge beginnen. Keine Anfrage wird wiederholt. Ein ungebundener Aufruf liefert
weiterhin den `binding_conflict` auf Aufrufebene, bevor ein Cursor verarbeitet wird.

Öffentliche Cursor-Werte sind auf 4.096 Zeichen begrenzt. Überschreitet die Ausgabe eines Cursors
dieses Limit, ist das Element `omitted` mit null-Ergebnis/-Fehler und dem Grund
`networkObjectCursorExceededCharLimit`, Metadaten zu tatsächlicher Größe/Limit und einem Hinweis zum
erneuten Versuch. Es wird kein unsignierter Ersatz und keine unbrauchbare Teilseite geliefert; eine
letzte Seite mit `nextCursor:null` braucht keinen Cursor und kann weiterhin erfolgreich sein. Den
Projektpfad kürzen und neu beginnen oder die Anfrage eingrenzen, bis ihr Ergebnis auf eine letzte
Seite passt.

Die öffentlichen Anfrage-/Ergebnisschemas für Lesevorgänge und `network_read`
`contractVersion:"1.0"` sind unverändert.

Der [begrenzte Smoke-Test des installierten Kandidaten vom 2026-10-23](../superpowers/acceptance/reports/2026-10-23-c5-network-object-cursors-live-verification.md)
bestand erstes/fortgesetztes Paging, die Ablehnung von Manipulationen, die erneute Verifizierung mit
demselben Eigentümer und eine neue Folge im Modus `full` auf einer Einzelplatz-Fixture mit
`pageSize:1`. Der Bericht führt nicht ausgeführte Fehler-, Modus- und Local-Session-Fälle; die
Standardänderungen von C-9 und die kombinierte Qualifizierung sind getrennt.

### Selektorformen

| Art | Erforderliche Selektoridentität | Zusätzliche Verifizierungsnachweise |
|---|---|---|
| `deviceItem` | `deviceName`, nicht leerer `itemPath` | Jedes Pfadsegment trägt den nullbasierten Geschwister-`index`, `name`, `positionNumber` und `typeIdentifier`. |
| `networkInterface` | `deviceName`, nicht leerer `itemPath` | Optionale `interfaceName`, `interfaceType` und `interfaceOperatingMode` werden verifiziert, wenn vorhanden. |
| `node` | `deviceName`, `nodeId` | Die Discovery kann zusätzlich `itemPath` plus `nodeIndex` liefern; diese beiden Felder werden zusammen geliefert und verifizieren die besitzende Schnittstelle und die Geschwisterposition. |
| `subnet` | `subnetId` | Der exakte Bezeichner wird vor der Inspektion erneut gelesen. |
| `ioSystem` | `subnetId`, `number` | Optionale `ioSystemIndex` und `ioSystemName` unterscheiden und verifizieren Kandidaten mit doppelter Nummer. |
| `communicationConnection` | `deviceName`, nicht leerer Eigentümer-`itemPath`, `connectionIndex`, `connectionType`, `localConnectionName` | `localConnectionId` ist für die unterstützten Nicht-HMI-Selektorformen Pflicht und auf `HmiConnection` nicht anwendbar. |

Die Auflösung folgt zuerst dem protokollierten Pfad/Index und verifiziert dann alle gelieferten
Identitätsnachweise. Null Treffer, Mehrdeutigkeit oder jede Abweichung der Nachweise lassen das Ziel
fehlschlagen; es gibt keinen Rückfall auf das erste Element, die erste Schnittstelle, den ersten
Knoten oder nur den Namen.

### Typisierte Attributergebnisse

Ein Inspektionsergebnis enthält das verifizierte `target`, modellierte Identitäts-/Beziehungs-
`evidence`, ein geordnetes `attributes`-Array und nicht fatale `messages`. Jedes Attribut meldet
unabhängig:

- `source`: `modeled`, `dynamic` oder `modeledAndDynamic` (null nur für einen unbekannten angeforderten Namen);
- `access`: `none`, `readOnly`, `writeOnly`, `readWrite` oder `unknown`;
- `supportedTypes`: die deklarierten CLR-Typnamen in Metadatenreihenfolge;
- `availability`: `available`, `notApplicable`, `unsupported`, `unreadable`, `readFailed`,
  `unrepresentable` oder `unknownAttribute`;
- `value`: nur `null`, `string`, `boolean`, `integer`, `number` oder `enum`; und
- `diagnostic`: Kategorie, Meldung und optionaler CLR-Typname, wenn ein Wert nicht verfügbar ist.

Ein unbekanntes oder fehlgeschlagenes Attribut lässt die Inspektion nicht fehlschlagen und
unterdrückt keine späteren Attribute. Ein erfolgreich gelesenes CLR-null wird durch einen Wert mit
`kind:"null"` und einem expliziten `value:null` dargestellt; ein beliebiges CLR-Objekt ist
`unrepresentable` und wird nie über `ToString()` veröffentlicht.

## Paginierung der Hardwarekonfiguration

Die Paginierung von `read_hardware_config` ist optional (opt-in). `pageSize` (`1..200`) in der
ersten Anfrage setzen oder mit einem opaken `cursor` fortsetzen. Eine Fortsetzung nur mit Cursor
verwendet standardmäßig die Seitengröße 100. Eine Anfrage ohne beide Felder bleibt auf dem
ungepagten Pfad; Version/Warnungen des Lese-Umschlags und zusätzliche Verbindungsnachweise folgen dem
aktuell deklarierten Vertrag.

```jsonc
{
  "operations": [
    {
      "operationId": "hardware-page",
      "operation": "read_hardware_config",
      "deviceName": "PLC_1",          // optional, cursor-bound
      "plcName": "PLC_1",             // optional, cursor-bound
      "includeIoDetails": true,        // optional, cursor-bound
      "includeTagMatches": true,       // optional, cursor-bound
      "pageSize": 50,
      "cursor": null                   // omit on the first page
    }
  ]
}
```

Die logische Folge ist jedes passende Gerät in stabiler Durchlaufreihenfolge, gefolgt von jedem
passenden Subnetz. `pageSize` zählt über diese kombinierte Folge, während das öffentliche Ergebnis
`devices[]` und `subnets[]` getrennt hält. Die kanonische Größenprojektion kann ein kleineres
vollständiges Präfix als angefordert liefern; Fortschritt und nächster Cursor rücken nur über
tatsächlich gelieferte Entitäten vor.

Gepagte Ergebnisse fügen hinzu:

```jsonc
{
  "devices": [],
  "subnets": [],
  "messages": [],
  "pagination": {
    "totalDevices": 0,
    "totalSubnets": 0,
    "returnedDevices": 0,
    "returnedSubnets": 0,
    "nextCursor": "opaque-process-local-value"
  }
}
```

`nextCursor` ist nullbar und wird auf der letzten Seite weggelassen. `deviceName`, `plcName`,
`includeIoDetails` und `includeTagMatches` für eine Fortsetzung unverändert lassen. `pageSize` gehört
nicht zur Abfrageidentität und darf sich innerhalb von `1..200` ändern. Eine Fortsetzung führt keinen Projektpfad mit; der Host verwendet den aufgelösten Pfad des Cursors, verlangt dieselbe verifizierte Bindung und schlägt andernfalls mit `cursor_binding_mismatch` fehl. Content-Tools wirken nur auf das gebundene Projekt; ohne verifizierte Bindung (ein konfiguriertes `--project` wird zuerst über den Status geerdet) schlagen sie fail-closed mit `binding_conflict` fehl; zuerst `bind_project` aufrufen.

Cursor werden mit einem Schlüssel authentifiziert, der für die Lebensdauer des Host-Prozesses gilt,
und funktionieren nach einem Host-Neustart absichtlich nicht mehr. Sie binden außerdem das
aufgelöste Projekt, den gebundenen/ungebundenen Host-Snapshot, die beobachtete Worker-Sitzung, den
stabilen Kandidaten-Snapshot und den kombinierten Offset. Die Wiederherstellung ist
kategoriespezifisch:

| Kategorie | Bedeutung | Wiederherstellung |
| --- | --- | --- |
| `validation_error` | `pageSize` liegt außerhalb von `1..200`, oder ein abhängiges Feld ist ungültig. | Die erste Anfrage korrigieren und eine neue Folge beginnen. |
| `invalid_cursor` | Kodierung, Version, Schema, kanonische Form oder Signatur ist ungültig; dies umfasst auch einen Cursor aus einem früheren Host-Prozess. | Eine neue Folge ohne den alten Cursor beginnen. |
| `cursor_filter_mismatch` | Ein an den Cursor gebundener Filter oder ein Detail-Flag hat sich geändert. | Die unveränderte Anfrage am selben Cursor wiederholen oder eine neue Folge mit den neuen Feldern beginnen. |
| `cursor_binding_mismatch` | Bindung/Pfad des Hosts oder die beobachtete Worker-Sitzung hat sich geändert. | Den aktuellen Projektstatus erneut lesen, dann eine neue Folge beginnen; den Cursor nicht wiederverwenden. |
| `cursor_snapshot_mismatch` | Die stabile Menge oder Reihenfolge passender Geräte/Subnetze hat sich geändert. | Eine neue Folge gegen den neuen Snapshot beginnen. |
| `cursor_out_of_range` | Der gespeicherte kombinierte Offset liegt außerhalb der aktuellen Kandidatenmenge. | Eine neue Folge beginnen. |
| `protocol_error` | Der Worker hat die Identität des Umschlags weggelassen oder fehlerhafte/inkohärente typisierte Kandidatennachweise geliefert. | Die Seite als unbrauchbar behandeln; Diagnosen prüfen und erst nach Behebung der Host/Worker-Abweichung erneut versuchen. |

Jedes erfolgreiche oder ausgelassene Operationselement bleibt bei höchstens 60.000 kanonischen
Zeichen. Vollständige Entitäten und Diagnose-Strings werden nie geteilt. Der Host liefert das größte
vollständige Präfix, das passt. Überschreiten allein die Seitendiagnosen das Limit, wird die
Operation mit `hardwarePageDiagnosticsExceededItemCharLimit` ausgelassen, ohne Subjekt und ohne
Vorrücken des Offsets. Passt die erste erforderliche Entität nicht, ist der Grund
`hardwarePageEntityExceededItemCharLimit`; ihr vollständiges optionales Subjekt
`{ kind, name, identifier }` bleibt nur erhalten, wenn auch dieses vollständige Subjekt passt,
andernfalls wird das ganze Subjekt weggelassen (nie gekürzt). Beide Auslassungen bewahren einen
sicheren Hinweis: die unveränderte Anfrage am selben Cursor wiederholen oder eine neue, engere Folge
beginnen. Das separate Batch-Limit von 180.000 Zeichen gilt weiterhin; verwirft es eine
Hardwareseite, denselben Hinweis befolgen und nach dem Ändern gebundener Felder nie einen alten
Cursor wiederverwenden.

## Paging-Matrix

| Leser | Öffentliche Größen- und Positionsfelder | Standardwerte und Opt-in-Verhalten | Grenzen | Semantik von Scan und Fortsetzung |
|---|---|---|---|---|
| [`browse_project_tree`](PROJECT_OPERATIONS_SUMMARY.md#browse_project_tree-v3) | `pageSize`, optionaler `cursor` | 100 beim ersten Aufruf und bei Fortsetzung | `pageSize` 1..200 | Zwischengespeicherter flacher Knoten-Snapshot eines Zeitpunkts; Fortsetzungen blättern im gespeicherten Snapshot. |
| [`network_read/read_hardware_config`](#paginierung-der-hardwarekonfiguration) | `pageSize`, optionaler `cursor` | Der erste gepagte Aufruf liefert die Größe; nur Cursor bedeutet Standard 100; ohne beide Felder bleibt die Ausgabe ungepagt | `pageSize` 1..200 | Geräte, dann Subnetze; aktueller Kandidaten-Snapshot wird geprüft. Die Budgetprojektion kann das gelieferte vollständige Präfix verkürzen. |
| [`network_read/list_network_objects`](#fortsetzungs-cursor-für-netzwerkobjekte) | `pageSize`, optionaler `cursor` | 100 auf jeder Seite | `pageSize` 1..200 | Aktuelles deterministisches Inventar wird gegen den Worker-Snapshot geprüft; der öffentliche Cursor ist vom Host versiegelt. |
| [Gepagte HMI-Lesevorgänge](HMI_OPERATIONS_SUMMARY.md#cursor-paging) | `offset`, `limit`, optionaler `cursor` | `offset` 0, `limit` 100; `cursor` schließt `offset` aus | `offset` >= 0; `limit` 1..2000 | Der Cursor setzt den gespeicherten Snapshot fort; ohne Cursor führt jeder Aufruf einen frischen Scan aus. |
| [`vci_read/list_mappings`](VCI_OPERATIONS_SUMMARY.md#reads) | `offset`, `limit` | `offset` 0, `limit` 100 | `offset` >= 0; `limit` 1..200 | Einfaches Live-Offset-Paging ohne Snapshot oder Cursor; Änderungen an Mappings können Zeilen verschieben. |

Die sieben gepagten HMI-Operationen sind `list_tags`, `list_system_tags`, `list_alarms`,
`list_logging_tags`, `list_screen_items`, `list_faceplate_instances` und `validate`.

Explizites `pageSize` oder `limit` darf sich zwischen Seiten innerhalb der Grenzen des Lesers
ändern. Den zurückgegebenen Fortsetzungsmetadaten folgen, statt genau die angeforderte Anzahl
anzunehmen: Letzte Seiten können kürzer sein, und die Budgetprojektion kann ein vollständiges Präfix
verkürzen. Snapshot-Lebensdauer, Verdrängung, Budget- und Wiederherstellungsregeln bleiben
leserspezifisch; beim Fortsetzen oder erneuten Versuchen die verlinkten Referenzen verwenden.
Zugehörige PLC-Lesevorgänge behandelt die [PLC-Referenz](PLC_OPERATIONS_SUMMARY.md#supported-read-operations).

Die Änderung der Paging-Standardwerte lässt `search_equipment_catalog.maxResults` bei 50: Es ist
eine Kürzungsobergrenze ohne Fortsetzung.

## Strukturierte E/A-Map

`read_hardware_config` kann neben dem bestehenden Hardwarebaum eine schreibgeschützte, optionale
strukturierte E/A-Map liefern. Das veraltete Member `address` pro Element ist immer `null`: Ein
TIA-`DeviceItem` hat kein Attribut `Address`, daher liest der Worker keines mehr (was nur Meldungen
pro Element erzeugte). Für E/A-Adressen `ioDetails.addresses` verwenden. Die strukturierte Map liegt unter einem neuen Member `ioDetails`, das **in einem Standard-Lesevorgang fehlt** (ohne Flags), sodass kein bestehender Aufrufer
eine optionale Erweiterung um die E/A-Map sieht; der Netzwerkbereich verwendet keine
Zustands-Hashes von Safety-Tokens mehr.

### Anfrage

```jsonc
{
  "operations": [
    {
      "operationId": "io-map",
      "operation": "read_hardware_config",
      "deviceName": "ET 200SP station_1",   // optional: ordinal-ignore-case, exactly one match
      "plcName": "PLC_1",                   // optional: exact ordinal PLC name for tag matching
      "includeIoDetails": true,             // required for any I/O map output
      "includeTagMatches": true             // optional; requires includeIoDetails
    }
  ]
}
```

- `deviceName` grenzt auf genau ein Gerät ein. Es benennt das Stations-/Root-Gerät, wie in
  `devices[].name` gemeldet (ohne Beachtung der Groß-/Kleinschreibung), nicht einen
  PLC-/CPU-Geräteelementnamen; für die PLC `plcName` verwenden. Null oder mehrere Treffer melden
  einen nicht fatalen `messages`-Eintrag und liefern keine Geräte — nie ein Rückfall auf den ersten
  Treffer. Ein gerätebezogener Lesevorgang liefert weiterhin jedes Subnetz des Projekts.
- `plcName` wählt die PLC, deren Variablentabellen abgeglichen werden, über den exakten ordinalen
  Namen (PLC-Softwarename oder Name des besitzenden Geräts). Ohne Angabe verwendet der
  Variablenabgleich eine PLC nur, wenn im Projekt genau eine PLC existiert; andernfalls meldet ein
  nicht fataler `messages`-Eintrag, dass keine Variablentreffer erzeugt wurden.
- `includeTagMatches` ohne `includeIoDetails` wird von der Anfragevalidierung abgelehnt.

### Antwortform

Ein Geräteelement mit E/A-Details trägt:

```jsonc
{
  "name": "DI_16",
  "typeIdentifier": "OrderNumber:TEST",
  "address": null,                // legacy member, always null; use ioDetails.addresses
  "ioDetails": {
    "addresses": [
      {
        "ioType": "Input",        // AddressIoType: Input, Output, Substitute, Diagnosis
        "startAddress": 4,        // raw Openness start, BYTES
        "length": 2,              // raw Openness length, BYTES
        "context": "Device",      // dynamic AddressContext where readable; null otherwise
        "controllerNames": ["PLC_1"]  // ordinal, deduplicated owning device names
      }
    ],
    "channels": [
      {
        "number": 0,
        "ioType": "Input",        // ChannelIoType: Input, Output, Complex
        "type": "Digital",        // ChannelType: Analog, Digital, Technology
        "channelAddressBits": 32, // raw Openness start, BITS
        "channelWidthBits": 1,    // raw Openness width, BITS
        "logicalAddress": "%I4.0",// formatted ONLY when evidence is aligned; null otherwise
        "tagMatches": [
          {
            "name": "StartButton",
            "dataType": "Bool",
            "logicalAddress": "%I4.0",
            "tableName": "Tag table_1",
            "folderPath": "/"
          }
        ]
      }
    ]
  }
}
```

### Einheiten- und Formatierungssemantik

- `startAddress`/`length` an einer Adresse sind **Bytes**, wie von Openness gemeldet.
  `channelAddressBits`/`channelWidthBits` an einem Kanal sind **absolute Bits**, wie von Openness
  gemeldet. Die beiden Einheitenfamilien werden nie vermischt oder stillschweigend umgerechnet.
- `logicalAddress` wird nur formatiert, wenn E/A-Typ, Bitanfang und Breite des Kanals alle
  vorhanden und korrekt ausgerichtet sind: Breite 1 → `%I4.0`/`%Q4.0` (beliebiges Bit), Breite 8 →
  `%IB4`/`%QB4` (Bytegrenze), Breite 16 → `%IW64`/`%QW64` (gerades Byte), Breite 32 →
  `%ID64`/`%QD64` (Byte durch vier teilbar). Jede andere Kombination lässt `logicalAddress` null,
  während `channelAddressBits`/`channelWidthBits` roh und unverändert bleiben. `%M`-Merker, DB- und
  rein symbolische Adressen werden nie ausgegeben.
- Unlesbare Skalare bleiben null (nie Standardwerte `0`/leerer String); unlesbare Member fügen einen
  nicht fatalen `messages`-Eintrag hinzu. Der Nutzlastvertrag lehnt zusätzlich eine explizite
  null-Collection innerhalb von `ioDetails` als `protocol_error` ab, ohne die Nutzlast
  zurückzugeben.
- TIA V21 meldet für manche Adressen vom Typ `Diagnosis` eine negative Anfangsadresse (und Länge);
  der Worker normalisiert diese mit einem nicht fatalen `messages`-Eintrag auf null. Die dynamischen
  Kanalattribute `ChannelAddress`/`ChannelWidth` werden akzeptiert, wenn Openness sie als
  64-Bit-Ganzzahlen (`Int64`/`UInt64`) innerhalb des DTO-Bereichs meldet.

### Konservativer Variablenabgleich

- Der Variablenindex wird **einmal** pro gewählter PLC aus deren Variablentabellen aufgebaut
  (Ordnerpfad bleibt erhalten).
- Eine Variable passt nur dann zu einem Kanal, wenn ihr normalisiertes absolutes E/A-Intervall
  **exakt gleich** dem Intervall des Kanals ist **und** die E/A-Bereiche übereinstimmen (`I` vs.
  `Q`). Es gibt keinen Rückfall auf Überlappung, Enthaltensein oder ersten Treffer; mehrere Variablen
  können zu einem Kanal passen (sie benennen alle dasselbe Intervall).
- Der Kanal muss laut Openness-Zuordnungsnachweisen (`Address.AddressControllers` → Name des
  besitzenden Geräts) zum Controller der gewählten PLC gehören. Variablen werden nie
  controllerübergreifend abgeglichen. Ist die Controller-Zuordnung unlesbar oder mehrdeutig, behält
  der Kanal seine Nachweise mit einem leeren `tagMatches`-Array und einem klaren nicht fatalen
  `messages`-Eintrag.
- `%M`-/DB-/rein symbolische Variablen werden übersprungen; harmlose Groß-/Kleinschreibung und
  umgebender Leerraum in Variablenadressen werden normalisiert.

### Nutzlastgröße

Der rekursive Durchlauf der Gruppen kann ein ungefiltertes Hardwareergebnis deutlich größer machen als in früheren Versionen, weil zuvor ausgelassene Geräte nun vorhanden sind. Der ungepagte Pfad wendet weiterhin das Budget von 60.000 Zeichen pro Ergebnis an und kann die ganze Operation mit `reason: "resultExceededItemCharLimit"` auslassen. Für einen vollständigen Abruf die gepagte Anfrage oben verwenden, `deviceName` eingrenzen, optionale Detail-Flags nach Möglichkeit deaktivieren und den Hardware-Lesevorgang in einen eigenen `network_read`-Aufruf legen. Kein Gerät wird stillschweigend übersprungen, nur damit es in das Budget eines der beiden Pfade passt.

## Der einschichtige JSON-Vertrag

Sowohl `network_read` als auch `network_write` deklarieren ein MCP-`outputSchema` und liefern **ein
kanonisches JSON-Dokument**, identisch im `content` des Tool-Ergebnisses (als Text) und in
`structuredContent`. Nirgends in der Antwort gibt es eine verschachtelte Schicht aus JSON in einem
String: Jedes Operationsergebnis ist ein echtes JSON-Objekt oder -Array unter
`batch.operations[n].result`, kein maskierter String, den ein Agent ein zweites Mal parsen muss.

### `network_read`-Umschlag

```jsonc
{
  "tool": "network_read",
  "contractVersion": "1.0",
  "warnings": [],
  "success": true,
  "batch": {
    "operationCount": 1,
    "counts": { "succeeded": 1, "failed": 0, "omitted": 0, "skipped": 0 },
    "operations": [
      {
        "operationId": "hardware",
        "operation": "read_hardware_config",
        "status": "succeeded",
        "result": { "devices": [ /* ... */ ], "subnets": [ /* ... */ ], "messages": [] },
        "failure": null,
        "omission": null,
        "skipReason": null,
        "warnings": []
      }
    ],
    "truncation": null
  },
  "error": null
}
```

`success` beschreibt den ganzen Aufruf: Ein Batch, der lief, aber ein fehlgeschlagenes Element
enthält, meldet `success:false` und bleibt dennoch ein erfolgreiches MCP-Ergebnis (`isError:false`).
Nur ein Aufruf, der abgelehnt wurde, bevor irgendeine Operation lief (falsche Anfrageform, Zugriff
verweigert), füllt `error` statt `batch` und setzt `isError:true`.

### `network_write`-Umschlag

Die öffentliche Eingabe ist `network_write(operations, dryRun=false)`: **Ohne `dryRun` wird
ausgeführt**. Ein älterer Aufruf nur mit Operationen lieferte früher eine Vorschau; jeden
Vorschau-Aufrufer auf `dryRun:true` migrieren. `confirm`, `acknowledge`, unbekannte Root-Schlüssel
und ein nicht boolesches `dryRun` werden vor dem Eintritt in das Tool mit dem kanonischen Dokument des
Tools (`validation_error`, `isError:true`) und ohne Audit-Eintrag abgelehnt, wie jeder
Argumentformfehler in allen 32 Tools. Ablehnungen nach dem Eintritt in die Pipeline durch Validierung,
Bindung und Guards haben einen typisierten kanonischen `error` auf oberster Ebene, MCP `isError:true`
und genau einen Audit-v2-Eintrag.

Fehler der Bindungsschranke behalten ihre ursprüngliche Kategorie, einschließlich `worker_timeout`,
`worker_crashed`, `portal_lost` und `protocol_error`, im Fehler auf oberster Ebene und im Audit. Eine
Ablehnung durch die Schranke stoppt vor Planung oder Mutation; eine fehlende, nicht passende oder
geänderte Bindung ist `binding_conflict`.

Beide schreibenden Modi verwenden null Server-Elicitation im Netzwerkbereich, auch beim Löschen
verbundener Subnetze und bei Dry Runs. Berechtigungsrückfragen des Clients bleiben davon unabhängig.
Der Lifecycle bestätigt weiterhin jeden tatsächlichen read-write-Aufruf; kein Tool verwendet
Safety-Tokens.

Schreibvorgänge verlangen genau eine bereits geöffnete, verifizierte Projektbindung. Eine
festgehaltene Lease umfasst Planung, geordnete Mutation, Verifizierung, Zusammenstellung der Antwort
und Audit. Die Planung liest einen Hardware-Snapshot; der Netzwerkbereich plant zwischen den
Elementen nicht neu, und seine gemeinsame Verifizierung liest nichts zurück. Lesevorgänge binden,
wechseln oder öffnen nie ein Projekt. Höchstens 50 Operationen mit eindeutigen IDs, Reihenfolge des
Aufrufers, Halt beim ersten fehlgeschlagenen Schreibvorgang; spätere Elemente sind `skipped` mit
`skipReason:"earlierOperationFailed"`. Kein Batch-Rollback und keine automatische Wiederholung.

| Phase | Bedeutung |
| --- | --- |
| `preview` | `dryRun:true`; nur Effekte/Guards, keine Mutation oder Elicitation. |
| `applied` | Eine Mutation wurde versucht; typisierte `batch` und `verification`, `error:null`, MCP `isError:false`, auch bei teilweisem Fehlschlag oder Fehlschlag der Verifizierung. |
| `blocked` | Ein nicht übersteuerbarer Guard hat die Ausführung verhindert; Root-Fehler und MCP `isError:true`. |
| `error` | Ablehnung von Eingabe/Bindung/Planung nach dem Eintritt in die Pipeline; Root-Fehler und MCP `isError:true`. |

Alle Root-Dokumente von Netzwerk-Schreibvorgängen deklarieren `tool`, `contractVersion:"2.0"`,
`phase`, `success`, `error`, `warnings`, `guards`, `effects`, `batch`, `verification`, `omission`.
Arrays bleiben vorhanden, und deklarierte nulls bleiben explizit. Zum Beispiel enthält ein
Dry-Run-Umschlag die aufgelösten Effektwerte in `effects`:

```jsonc
{
  "tool": "network_write", "contractVersion": "2.0", "phase": "preview",
  "success": true, "error": null, "warnings": [], "guards": [],
  "effects": [ /* { operationId, effect: resolved target/current/requested settings, omission:null } */ ],
  "batch": null, "verification": null, "omission": null
}
```

Hardwareidentitäten in Effekten stammen aus gewöhnlichen Lesevorgängen. Namen beim Anlegen werden
aus der Anfrage abgeleitet und haben keine erfundene Geräte-/Knoten-/Subnetz-ID.
`network_delete_connected_subnet` ist `info`: Seine betroffenen exakten Geräte-/Knotenidentitäten
erklären entfernte Subnetz-/IO-Verbindungen. Geräte/Knoten bleiben erhalten.
`network_state_unverifiable` ist nur dann `block`, wenn das Verbindungsinventar einer Löschung
unvollständig ist oder nicht aufgelöste/doppelte Identitäten enthält: unbekannt ist nie leer. Eine
unvollständige Projekt-Discovery lässt die Planung mit `worker_operation_failed` fehlschlagen. Beide
Guards haben null als Erfüllung; der Netzwerkbereich hat keinen Acknowledge-Guard. Löscheffekte
beschreiben den anfänglichen Snapshot. Ein Knoten, der früher im selben Aufruf verbunden wurde, fehlt
in dieser Liste; Aufrufe aufteilen und erneut inspizieren, wenn diese Verbindung im Löscheffekt
erscheinen muss.

Effekt-`currentSettings` enthält einfache Strings oder explizite nulls aus diesem Hardware-Snapshot,
ohne separaten `inspect_network_object`-Aufruf oder `source`/`access`-Metadaten. Dem Snapshot fehlen
`PnDeviceNameAutoGeneration`, `HighestAddress` und `TransmissionSpeed`, daher sind deren aktuelle
Werte null. Nur angeforderte Einstellungen erscheinen. Beziehungseffekte teilen `IoSystem` in die
skalaren Schlüssel `IoSystemSubnet` und `IoSystemNumber` auf.

Configure wendet Einstellungen in dieser Reihenfolge an: `Address`, `SubnetMask`,
`PnDeviceNameAutoGeneration`, `PnDeviceName`, `Subnet`, `IoSystem`. Die erste native Ablehnung
stoppt das Element und den Batch; frühere Einstellungen können angewendet bleiben. Ein
fehlgeschlagenes Configure kann dasselbe typisierte Ergebnis wie ein erfolgreiches tragen:
`deviceName`, dünn besetzte `appliedSettings`, `messages[]`. Angewendete Schlüssel behalten diese
sechs Einstellungsnamen; `IoSystem` meldet seine Nummer. Der Host validiert das Roh-JSON eines
mitgeführten Ergebnisses, bevor die nachsichtige Umschlag-Normalisierung fehlende Member ergänzen
oder fehlerhafte Nachweise verwerfen kann. Fehlerhafte Member sowie mitgeführte Schlüssel/Werte, die
der Anfrage widersprechen, schlagen mit `protocol_error` fehl, ohne die abgelehnten Nachweise
zurückzugeben. Ein fehlendes oder explizit null gesetztes Ergebnis lässt den angewendeten Zustand
unbekannt; es beweist nicht, dass sich keine Einstellung geändert hat. Ein fehlender IO-Konnektor
schlägt vor dem ersten Setter mit `target_not_found` fehl. Andere fehlgeschlagene
Netzwerkoperationen haben `result:null`.

Dieser Auszug ist der aufgezeichnete L11c-Fehlschlag: Address und SubnetMask wurden angewendet,
bevor eine inkompatible Subnetzverbindung `EngineeringTargetInvocationException` warf. Sein Umschlag
hat `phase:"applied"`, `success:false`, `error:null`, MCP `isError:false`; das spätere Element wurde
nie abgesetzt. Die vollständige kanonische Antwort, Warnungen und die durchgeführten Lesevorgänge
stehen in der [Eingabe des funktionalen Wiederholungslaufs](../superpowers/acceptance/reports/inputs/2026-10-29-network-write-native-rejections-live-rerun.md#l11c-corrected-two-key-partial-write-rejection).

```json
{
  "operationCount": 2,
  "counts": { "succeeded": 0, "failed": 1, "omitted": 0, "skipped": 1 },
  "operations": [
    {
      "operationId": "l11c-partial", "operation": "configure_network_device", "status": "failed",
      "result": {
        "deviceName": "S7-1500/ET200MP station_2",
        "appliedSettings": { "Address": "192.168.203.42", "SubnetMask": "255.255.254.0" },
        "messages": []
      },
      "failure": {
        "category": "engineering_rejected",
        "message": "TIA Portal rejected the operation: Error when calling method 'ConnectToSubnet' of type 'Siemens.Engineering.HW.Node'.\r\n\r\nAssignment to subnet failed.",
        "engineeringError": {
          "type": "EngineeringTargetInvocationException",
          "message": "Error when calling method 'ConnectToSubnet' of type 'Siemens.Engineering.HW.Node'.\r\n\r\nAssignment to subnet failed.",
          "inner": []
        }
      },
      "omission": null, "skipReason": null,
      "warnings": [ "This call stopped at this operation. This operation and any earlier operation in the same call may already have changed TIA state; no rollback was attempted. Re-read the affected objects before retrying." ]
    },
    {
      "operationId": "l11c-after", "operation": "create_subnet", "status": "skipped",
      "result": null, "failure": null, "omission": null,
      "skipReason": "earlierOperationFailed", "warnings": []
    }
  ],
  "truncation": null
}
```

Root-`verification` ist `{ success, operations, omission }`. Jeder Operationseintrag hat
`{ operationId, success, check, expected, actual, message }`. `check:"trusted"` bedeutet, dass der
Openness-Aufruf ohne Ablehnung abgeschlossen wurde; es wurde nichts zurückgelesen. Ein erfolgreiches
Element, das von einem späteren versuchten Element auf demselben Objekt geändert wurde, ist
`superseded`, einschließlich eines späteren Fehlschlags, der es bereits verändert haben kann.
Ablehnungen von Vorbedingungen verdrängen nicht. Die Configure-Identität umfasst das Gerät, Namen/
Positionen des Schnittstellenpfads und die rohe Knoten-ID; gleiche rohe IDs an verschiedenen
Schnittstellen bleiben unterschieden. Fehlgeschlagene und übersprungene Netzwerkelemente haben keinen
Verifizierungseintrag. Die `appliedSettings` eines fehlgeschlagenen Configure sind
Wiederherstellungsnachweise, kein Verifizierungsergebnis. Vor einem erneuten Versuch mit
`network_read` inspizieren.

`verification.success` ist die Konjunktion seiner Einträge und kann daher true sein, wenn
Root-`success` false ist, auch wenn das einzige versuchte Element fehlschlug und `operations` leer
ist. Vertrag 2.0 entfernt `skippedSettings`, die sofortige Worker-`verification`,
`identity`/`checks`, `finalChecks`, `networkDeviceCount` und `networkDeviceCountUnchanged`.

Die Klassifizierung hängt vom Siemens-Ausnahmetyp ab: Eine `EngineeringTargetInvocationException`
in einer mutierenden Worker-Methode ist `engineering_rejected`; eine andere `EngineeringException`,
etwa `EngineeringNotSupportedException`, ist `worker_operation_failed`. Beide tragen das bedingte
`engineeringError { type, message, inner[] }`. Die Wrap-Stellen von Configure verwenden die gemeinsame
Klassifizierung und bewahren ihr Teilergebnis. `portal_lost` hat Vorrang und macht die Bindung
ungültig; gewrappte Engineering-Fehler behalten jedes Configure-Teilergebnis. Native Meldungen folgen
der Sprache der Portal-Oberfläche; die Grenzen für Meldung/innere Kette bleiben wie in der
[PLC-Referenz](PLC_OPERATIONS_SUMMARY.md#failures-from-tia-portal) beschrieben.

### PROFINET-IO-System

`create_io_system` (K28, Anfrage des Eigentümers nach dem Live-Lauf vom 2026-11-02, bei dem ein
ET 200SP keiner CPU zugeordnet werden konnte, weil kein IO-System existierte: "ET200SP_1 is not
assigned to an IO-Controller") ist **nicht live getestet**.

- Anfrage: `target` ist der Knoten der CPU mit `kind: "node"` (hier Pflicht), exaktem `deviceName`
  und `nodeId` und dem Eigentümerpfad (`interfacePath` oder veraltet `itemPath` plus `nodeIndex`),
  wie `read_hardware_config` ihn meldet; optional `ioSystem: { name }` mit nicht leerem Namen
  (geformt wie `subnet` von `create_subnet`; das flache Feld `ioSystemName` bleibt zusammen mit den
  anderen veralteten flachen Configure-Feldern stillgelegt). Der Knoten wird genau so aufgelöst wie
  für `configure_network_device` (ein bloßes `deviceName`/`nodeId` wird aus dem Planungs-Snapshot
  qualifiziert).
- Worker: das besitzende `NetworkInterface` des Knotens (aus `GetService<NetworkInterface>` im
  Selektor-Resolver), dessen Composition `IoControllers` und der erste `IoController`
  (Stub-Member, bereits von `read_hardware_config` gelesen), dann
  `IoController.CreateIoSystem(System.String)`. Dieses Member hat keinen Stub; es wird zur Laufzeit
  über exakten Namen und Parametertyp gebunden (`LibraryRuntimeBinding.Find`), und eine Installation
  ohne es schlägt mit `api_member_missing` fehl, ohne dass sich etwas ändert. Ein weggelassenes
  `ioSystem` übergibt einen leeren String; welchen Namen TIA Portal dann vergibt, ist nicht live
  getestet, das Ergebnis meldet ihn. Eine Schnittstelle ohne IO-Controller (ein IO-Device wie der
  ET 200SP-Kopf) schlägt mit `target_kind_unsupported` fehl; jede Ablehnung durch TIA Portal folgt
  der gemeinsamen Klassifizierung (`engineering_rejected` mit `engineeringError`).
- Zurücklesen: Das angelegte IO-System (der Rückgabewert, sonst `IoController.IoSystem`) muss lesbare
  `Number`, `Name` und `Subnet` melden, das Subnetz eine lesbare `subnetId`, und die `IoSystems` des
  Subnetzes müssen es auflisten; andernfalls `postcondition_failed` ("may exist; inspect"). Ein
  anderer als der angeforderte Name oder ein `IoController.IoSystem`, das sich nicht als das neue
  IO-System zurücklesen lässt, ist eine Meldung, kein Fehlschlag.
- Ergebnis (`CreateIoSystemResultInfo`): `{ deviceName, nodeId, requestedName, name, number, subnetId,
  messages }`. `subnetId` und `number` sind das, was `configure_network_device`
  `changes.ioSystem: { subnetId, number }` nimmt, um den Knoten eines IO-Device in einem späteren
  Aufruf anzuschließen (ein Snapshot plant einen Aufruf, daher ist das neue IO-System im selben
  Aufruf nicht auswählbar).
- Guards, alle info (TIA Portal entscheidet): `network_io_system_create_unverified` bei jedem
  Element, `network_io_system_node_unconnected`, wenn der Planungs-Snapshot den Knoten ohne Subnetz
  zeigt (zuerst mit `configure_network_device` `changes.subnet` verbinden),
  `network_io_system_already_assigned`, wenn seine Schnittstelle bereits zu einem IO-System gehört,
  und `network_state_unverifiable`, wenn die Verbindungsnachweise des Knotens unvollständig sind.
- Die Verifizierung ist `trusted`; der verdrängende Schlüssel ist der IO-Controller des Knotens,
  daher verdrängt ein späteres Configure desselben Knotens es nicht.

Kette (FakeWorker-Szenario `network-io-system`, nur offline): `create_subnet` →
`configure_network_device` `changes.subnet` für den CPU-Knoten und den ET 200SP-Knoten →
`create_io_system` auf dem CPU-Knoten → `configure_network_device` `changes.ioSystem` mit den
gelieferten `subnetId` und `number` auf dem ET 200SP-Knoten.

### PROFINET-Gerätename

Das optionale boolesche `changes.pnDeviceNameAutoGeneration` schreibt die Einstellung des Knotens
"Generate PROFINET device name automatically" vor `PnDeviceName` und erscheint in angeforderten/
angewendeten Einstellungen und im Audit. `pnDeviceNameAutoGeneration:false` zusammen mit
`pnDeviceName` übergeben, um den Namen explizit zu setzen. Die frühere Vorprüfung bei der Planung ist
stillgelegt. Im begrenzten L6-Lauf schlug ein Schreiben des Namens bei weiterhin aktivierter
automatischer Namensvergabe nativ mit `EngineeringNotSupportedException` fehl
(`worker_operation_failed`); sein Teilergebnis hatte keine angewendeten Einstellungen.

Audit v2 enthält genau einen Eintrag pro Aufruf nach dem Eintritt in die Pipeline unter
`%LOCALAPPDATA%\TiaMcpServer\audit`. Die Bestätigung eines tatsächlichen read-write-Aufrufs ist
`none`; eines tatsächlichen full-Aufrufs `policy`; Vorschauen/Ablehnungen vor der Ausführung sind
`none`. Die Erfüllung von Info-/Block-Guards ist null. Antworttext/-Hash im Audit ist das exakt
gelieferte kanonische Dokument, während die Elementstatus im Audit die tatsächliche Ausführung
beschreiben. Keine `user`-Bestätigung im Netzwerkbereich.

## Die Selektorauflösung ist exakt und fail-closed

- **Gerät**: abgeglichen über `target.deviceName`, ohne Beachtung der Groß-/Kleinschreibung.
- **Knoten**: abgeglichen über die exakte, gerätebezogene `target.nodeId`, die ein vorheriges `read_hardware_config` gemeldet hat — ordinaler Vergleich, nie ein Raten nur über den Namen oder über die erste Schnittstelle.
- **Subnetz**: abgeglichen über die exakte `changes.subnet.subnetId` (oder `changes.ioSystem.subnetId`).
- **IO-System**: abgeglichen über die exakte `changes.ioSystem.number`, beschränkt auf das bereits aufgelöste Subnetz.

Alle Auswahlfehler bleiben fail-closed. Die Host-Vorprüfung meldet einen Geräte-, Knoten-, Subnetz- oder IO-System-Selektor, der nichts trifft, als `target_not_found` und einen, der mehrere trifft, als `target_ambiguous`; ein aufgelöstes Subnetz mit leerem oder nicht unterstütztem `NetworkType` ist `target_kind_unsupported`; ein unlesbarer Discovery-Snapshot oder ein Treffer, dessen konkurrierende Identitäten unlesbar sind, ist `worker_operation_failed`. `postcondition_failed` deckt eine späte Abweichung des Selektors im Worker, eine ignorierte Subnetzumbenennung und eine unlesbare ID eines angelegten Subnetzes ab. Nirgends auf diesem Pfad gibt es einen Rückfall auf den ersten Treffer, den ersten Knoten oder nur den Namen — genau das macht es sicher, einen exakten Port an einem Gerät anzusteuern, das mehrere Netzwerkschnittstellen bietet.

`update_subnet` und `delete_subnet` verlangen ordinal `target.kind: "subnet"` und gleichen `target.subnetId` mit ordinaler Gleichheit (mit Beachtung der Groß-/Kleinschreibung) gegen `HardwareConfigInfo.Subnets` ab, ohne Rückfall auf Name, Index oder ersten Treffer. Ein Fehltreffer in der Vorprüfung ist `target_not_found`/`target_ambiguous`; späte null oder mehrere Treffer im Worker schlagen als `postcondition_failed` fehl. Delete löst einmal auf und erfasst vor `Delete()` einen nicht leeren Namen aus demselben transaktionslokalen Objekt; die Validierung unterstützter Typen verwendet den Host-Snapshot. Siehe [NETWORK_PHASE4_SUBNET_LIFECYCLE.md](NETWORK_PHASE4_SUBNET_LIFECYCLE.de.md).

Bei einer angeforderten Subnetzumbenennung vergleicht `update_subnet` den Namen, den es innerhalb der Transaktion ohnehin liest, mit dem angeforderten Namen über ordinale Gleichheit (mit Beachtung der Groß-/Kleinschreibung). Eine Abweichung schlägt vor `CommitOnDispose()` mit `postcondition_failed` fehl, sodass die Subnetzaktualisierung nicht festgeschrieben wird. Portal kann eine kollidierende Umbenennung stillschweigend ignorieren; der Fehler enthält den angeforderten und den tatsächlichen Namen und nennt eine Kollision als mögliche Ursache. Diese Prüfung fügt keinen nativen Getter und keinen Worker-Aufruf hinzu und prüft Namenskollisionen nicht vorab.

### Beispiel mit mehreren Netzwerkschnittstellen (Multi-homed)

Eine PC-Station (`PC_1`) mit zwei Ports — einer zur PLC hin (`nodeId: "node-plc"`), einer zur Datenbank hin (`nodeId: "node-db"`) — wird umkonfiguriert, indem nur der Knoten zur PLC hin angesteuert wird:

```jsonc
{
  "dryRun": true,
  "operations": [
    {
      "operationId": "configure",
      "operation": "configure_network_device",
      "target": { "deviceName": "PC_1", "nodeId": "node-plc" },
      "changes": { "ipAddress": "192.168.0.99" }
    }
  ]
}
```

Diese Anfrage mit `dryRun:true` für eine Vorschau verwenden, dann für die autorisierte Ausführung
explizit `dryRun:false` verwenden. Zustandsbehaftete FakeWorker-Tests prüfen die Auswahl eines Ports
eines Multi-homed-Geräts und den Erhalt seines Geschwisterports; das sind Offline-Nachweise. Vertrag
2.0 meldet den Abschluss über `trusted` oder `superseded`, ohne Zurücklesen. Unsicheren oder
unvollständigen Ergebnissen mit einem frischen gefilterten `network_read` nachgehen. Der Lauf vom
2026-10-05 unten verwendete den früheren Verifizierungsvertrag; aktuelle begrenzte Nachweise stehen
im [Bericht vom 2026-10-29](../superpowers/acceptance/reports/2026-10-29-network-write-native-rejections-live-verification.md).

## Die typisierten Nutzlast-Ergebnistypen

Jedes direkte öffentliche Worker-Ergebnis im Netzwerkbereich wird gegen genau einen deklarierten CLR-Vertrag in `TiaMcpServer/Network/NetworkPayloadContract.cs` dekodiert. Das private Kandidatenergebnis der gepagten Hardware wird vor der öffentlichen Projektion separat von `HardwarePagePayloadContract` dekodiert. Eine Nutzlast, die nicht zu ihrem deklarierten Vertrag passt — fehlerhaft, unbekannt, falsch geschrieben (Groß-/Kleinschreibung), falsch typisiert oder strukturell ungültig —, wird zu einem **fehlgeschlagenen** Element mit Kategorie `protocol_error`; die abgelehnte Nutzlast wird nie zurückgegeben.

| Operation | Ergebnistyp | Bemerkenswerte Form |
|---|---|---|
| `read_hardware_config` | `HardwareConfigInfo` | `devices[]` umfasst Geräte auf Projektebene und rekursiv gruppierte Geräte (jeweils mit verschachtelten `items[]`, jedes Element mit `networkInterfaces[].nodes[]`), `subnets[]` (jeweils mit `ioSystems[]` und `connectedNodeNames[]`), und ein `messages[]` auf Nutzlastebene bleibt der Kanal für Hardware-Einschränkungen (dedupliziert; ein null-Typbezeichner eines Geräteelements, den TIA für manche Elemente meldet, ist keine Meldung); `items[].address` ist immer null; nur auf Anforderung enthält `items[].ioDetails` Adressen, Kanäle und Variablentreffer. Opt-in-Seiten fügen `HardwarePaginationInfo` hinzu; die Kandidaten-Nutzlast des Workers wird separat dekodiert und nie offengelegt. |
| `search_equipment_catalog` | `CatalogEntryInfo[]` | `typeName`, `typeIdentifier`, optional `articleNumber`/`version`/`catalogPath`/`description`. |
| `list_network_objects` | `NetworkObjectListInfo` | `items[]`, exakte `totalCount`/`returnedCount` und nullbarer `nextCursor`; jedes Element bewahrt die Vollständigkeit des Selektors und die Discovery-Diagnosen. |
| `inspect_network_object` | `NetworkObjectInspectionInfo` | Verifiziertes `target`, typisierte `evidence`, unabhängige Ergebnisse pro Attribut und nicht fatale `messages[]`. |
| `add_network_device` | `AddDeviceResultInfo` | `deviceName`, `rootItemName`, `typeIdentifier`, `warnings[]`. |
| `configure_network_device` | `ConfigureNetworkDeviceResultInfo` | `deviceName`, dünn besetzte `appliedSettings`, `messages[]`; dasselbe typisierte Ergebnis kann ein fehlgeschlagenes Configure begleiten, wenn der Worker dessen Teilergebnis mitführt. |
| `create_subnet`, `update_subnet`, `delete_subnet` | `SubnetLifecycleResultInfo` | Pflichtfelder `subnetId`, `name`; alle drei Operationen teilen sich diesen Ergebnistyp. Fehlgeschlagene Elemente haben `result:null`. Siehe [NETWORK_PHASE4_SUBNET_LIFECYCLE.md](NETWORK_PHASE4_SUBNET_LIFECYCLE.de.md). |
| `create_io_system` | `CreateIoSystemResultInfo` | `deviceName`/`nodeId` müssen der Anfrage entsprechen (Gerätename ohne Beachtung der Groß-/Kleinschreibung), `requestedName` muss `ioSystem.name` entsprechen, nicht leere `subnetId`, nicht negative `number`, `name`, `messages[]`. |

`NodeInfo.NodeId` und `SubnetInfo.SubnetId` sind leere Strings und `IoSystemInfo.Number` ist `null`, wenn das Engineering-System diese Identität nicht melden konnte — eine leere/null-Identität darf nie einen Schreibselektor erfüllen (siehe "Die Selektorauflösung ist exakt und fail-closed" oben).

## Semantik von Auslassung und Kürzung

Einzelne kanonische Werte werden auf 60.000 Zeichen und das **vollständige Dokument** auf 180.000
begrenzt. Striktes Dekodieren, Guards und Mutation verwenden die vollständigen internen Werte vor der
Begrenzung für die Darstellung. Ganze Effekt-/Ergebnis-/Verifizierungswerte können mit gemeinsamen
Metadaten `{reason,limitChars,originalChars,
retryTool,guidance}` ausgelassen werden; Identität, tatsächliche Fehler-/Überspringstatus der
Ausführung, Guards und kompakte Verifizierungszusammenfassungen bleiben erhalten. Root-`omission`
ist explizit null, wenn vollständig; es trägt gemeinsame Metadaten, wenn die Lieferung der Diagnosen
unvollständig ist. Diagnose-Strings über 512 kanonisch kodierten Zeichen werden vollständig durch
knappe Zusammenfassungen ersetzt; Diagnose-Collections können unter Druck ganz verworfen werden. Es
wird kein rohes Diagnosepräfix und kein abgeschnittenes JSON geliefert.

Lieferungs-`success:false` kann neben erfolgreicher Mutation, Elementstatus `succeeded`, sofortigem
Status `passed` und Verifizierung `success:true` bestehen, wenn ein angeforderter Wert (ein Effekt,
ein Elementergebnis oder die Verifizierungseinträge) ausgelassen wurde. Eine Root-Auslassung, die nur
Diagnosetext ersetzt hat (`diagnosticDetailsOmitted`: Meldungen, Warnungen, Guard-Texte), ändert
`success` nicht; sie wird weiterhin in `omission` gemeldet (seit 2026-11-02, K07: `export_io_map`
schrieb seine CSV und lieferte deren `sha256`, aber 2.362 Zeichen Diagnosen über der Grenze von 512
Zeichen machten den Aufruf zu `success:false`).
Versuchte Aufrufe bleiben `phase:applied`, `error:null`, MCP `isError:false`. Das exakt gelieferte
Dokument/der Hash geht ins Audit; der Elementstatus im Audit bewahrt die Wahrheit der Ausführung.
Auslassungseinträge/-Digests sind keine Selektoren. Über gefiltertes/gepagtes `network_read` mit den
ursprünglichen exakten Selektoren inspizieren oder, falls nicht verfügbar, frische Selektoren
ermitteln. **Nie einen Schreibvorgang wiederholen, um ein ausgelassenes Ergebnis wiederherzustellen.**

Lese-Batches behalten das gemeinsame Auslassungs-/Kürzungsverhalten für ganze Werte. Hardwareseiten
liefern das größte vollständige Präfix: Überlauf nur durch Diagnosen verwendet
`hardwarePageDiagnosticsExceededItemCharLimit`, Überlauf durch die erste Entität verwendet
`hardwarePageEntityExceededItemCharLimit`, und keiner von beiden rückt den Cursor vor. Ihr optionales
vollständiges Subjekt bleibt nur erhalten, wenn es passt. Mit der unveränderten an den Cursor
gebundenen Anfrage fortsetzen oder eine engere Folge ohne alten Cursor beginnen.

## Empfohlener Ablauf

1. `list_network_objects` mit den engsten sinnvollen `objectKinds` und, bei gerätebezogenen Arten,
   `deviceName` verwenden. `nextCursor` folgen, bis er null ist, und nur vollständige gelieferte
   Selektoren aufbewahren.
2. Einen gelieferten Selektor unverändert in `inspect_network_object` kopieren; `attributeNames`
   verwenden, wenn ein begrenzter gezielter Lesevorgang genügt. Neu auflisten, wenn sich das Projekt
   ändert oder ein Snapshot-Cursor oder Selektor abgelehnt wird.
3. `search_equipment_catalog` verwenden, um vor dem Anlegen einen exakten Katalog-`typeIdentifier` zu
   erhalten.
4. Das exakte, bereits geöffnete Projekt mit `bind_project` binden; Status- und Hardware-Lesevorgänge
   binden nie.
5. Die vollständige exakte Anfrage mit `dryRun:true` als Vorschau ausführen; Guards und aufgelöste
   Effekte prüfen. Das Anlegen hat keine Syntax für Ausgabereferenzen: die tatsächlich angelegten
   Identitäten ermitteln, bevor spätere Aufrufe sie brauchen. Alle anfänglichen Ziele müssen vor der
   Mutation aufgelöst sein.
6. Die geprüften Operationen explizit mit `dryRun:false` ausführen. Typisierte Element- und
   Verifizierungsergebnisse sowie den frischen Zustand nach teilweisem Fehlschlag, Timeout, Absturz,
   Auslassung oder unsicherer Mutation lesen. Eine Vorschau reserviert keinen Zustand und gibt kein
   Token aus; die Ausführung löst ihren eigenen aktuellen Zustand auf.

Netzwerk-Schreibvorgänge laufen sequenziell und stoppen beim ersten Fehlschlag. Abgeschlossene Operationen werden **nicht** zurückgerollt; ein fehlgeschlagenes Element trägt eine explizite Warnung, dass diese Operation und jede frühere Operation im selben Aufruf den TIA-Zustand bereits verändert haben können, daher vor einem erneuten Versuch erneut lesen, statt den Batch blind erneut auszuführen.

## Aktuelle Grenzen

Die aktuelle Oberfläche bietet nicht:

- Knotenattribute über die oben aufgeführten Felder der Gerätekonfiguration hinaus.
- Bearbeiten von Attributen von PROFINET-IO-Systemen oder DP-Mastersystemen (Umbenennen, Nummerieren).
  Löschen eines IO-Systems (`delete_io_system`), Trennen eines IO-Devices davon (`disconnect_from_io_system`)
  und Transferbereiche (`list_transfer_areas`, `create_transfer_area`, `update_transfer_area`,
  `delete_transfer_area`) sind seit Issue #11 Hardwaremodul-Operationen (nicht live getestet; siehe die
  [Referenz zu Hardwaremodulen](HARDWARE_MODULES_SUMMARY.de.md#io-systeme-dp-mastersysteme-und-transferbereiche)).
  V21 dokumentiert kein eigenes Member zum Anlegen eines DP-Mastersystems: `create_io_system` ruft das
  allgemeine `IoController.CreateIoSystem(System.String)` an dem Knoten auf, dessen Schnittstelle einen
  IO-Controller hat, auch an einem PROFIBUS-DP-Master-Knoten, und TIA Portal entscheidet (an einer
  DP-Schnittstelle nicht live getestet).
- **Schreibvorgänge** auf Adressobjekte, Prozessabbild, Kanäle oder Adress-Controller; die E/A-Map
  ist eine schreibgeschützte Sicht (`ioDetails`), keine Bearbeitungsoberfläche.
- E/A-Map-Lesevorgänge über Adressen und Kanäle hinaus: Diagnosedaten, modulspezifische
  Hardwareparameter und Hardwarekennungen werden nicht offengelegt.
- IO-Konnektor-Timing, Watchdog, RT-Klasse, Sync-Rolle, Sendetakt oder taktsynchrone Einstellungen.
- Verwaltung von S7-, FDL-, ISO-, ISO-on-TCP-, TCP-, UDP-, PTP- oder HMI-Kommunikationsverbindungen.
- Auswahl des Online-Verbindungspfads, Ermittlung erreichbarer Geräte, Gateways oder
  `ApplyConfiguration`.
- Generische Schreibvorgänge auf Netzwerkattribute. Die Inspektion von Phase 3 ist schreibgeschützt
  und legt nur das oben beschriebene geschlossene Vokabular typisierter Werte offen.
- Anlegen, Löschen oder Bearbeiten von Kommunikationsverbindungen. Phase 3 ermittelt und inspiziert
  nur bestehende Verbindungen, deren Identität vollständig ist.

Ergebnisse der Hardwarekonfiguration und Übersetzungsergebnisse sind Engineering-Daten. Sie zertifizieren für sich genommen weder eine Live-Hardwarekonfiguration noch ein Inbetriebnahmeergebnis.

## Live-Abnahmenachweise

Veraltete aufgabenspezifische Abnahmeverfahren und ihre Tests per Quellcode-Inspektion wurden
entfernt. Ihre Entfernung ändert die Nachweisgrenze nicht: Automatisierte, Stub- und
FakeWorker-Ergebnisse sind keine Live-Abnahme mit TIA Portal.

Die Hardware-Paginierung wurde vor der Entfernung des veralteten Verfahrens nicht gegen ein live
laufendes TIA Portal ausgeführt, daher bleibt das Live-Verhalten der Paginierung unverifiziert. Der
abgeschlossene Lauf von Phase 3 vom 2026-08-05, einschließlich der Entscheidung, `list_network_objects`
beizubehalten, ist in [NETWORK_PHASE3_LIVE_ACCEPTANCE.md](NETWORK_PHASE3_LIVE_ACCEPTANCE.de.md)
dokumentiert.

Die Wiederholbarkeit wird über die kanonische Discovery-Nutzlast (`result`, `omission` und
`truncation`) und gezielte Inspektionen bewertet. Der vollständige MCP-Umschlag kann legitimerweise
abweichen, wenn das erste Anhängen die einmalige Warnung `Connected to running TIA Portal instance.`
meldet; diese Warnung bleibt sichtbar und wird nicht als Abweichung von Selektor oder Nutzlast
behandelt.

Die Live-Abnahme von Phase 2 bleibt eine separate Autorisierungsgrenze; eine Autorisierung für
schreibgeschützten Zugriff autorisiert weder eine Schreibvorschau noch ein Anwenden. Ein Live-Lauf
der strukturierten E/A-Map wurde am 2026-08-14 gegen ein reales TIA Portal V21-Projekt
(Project20.ap21) abgeschlossen; die Ergebnisse sind in
[`../superpowers/acceptance/reports/2026-08-14-io-map-defect-fixes-live.md`](../superpowers/acceptance/reports/2026-08-14-io-map-defect-fixes-live.md)
festgehalten.

Ein historischer öffentlicher MCP-Lauf von Phase 4 ist nur ein Nachweis für seinen protokollierten
älteren Commit. Der erste Versuch mit der aktuellen Revision am 2026-09-23 blieb nach einer Ablehnung
durch eine TIA-Sicherheitsberechtigung unvollständig. Der frische Lauf vom 2026-09-24 bestand das
öffentliche Inventar, die Vorschau, isoliertes Erstellen/Aktualisieren/Löschen von Ethernet- und
PROFIBUS-Subnetzen, beide Löschungen verbundener Subnetze und ein separates abschließendes Lesen. Er
beobachtete nach Apply null Subnetze und insgesamt 81 Hardwaregeräte. Die Root-Anzahl von 10 stammt
aus Lifecycle-Ergebnissen, ohne unabhängige Root-Basislinie vor Apply. Siehe
[NETWORK_PHASE4_SUBNET_LIFECYCLE.md](NETWORK_PHASE4_SUBNET_LIFECYCLE.de.md) und den
[Live-Bericht zur aktuellen Revision](../superpowers/acceptance/reports/2026-09-21-network-phase4-current-revision-live.md).
Subnetz-Lifecycle-Operationen speichern das Projekt nicht und übersetzen keine Hardware.

Die historischen Behebungen von Live-Defekten vom 2026-10-05 (Build aus `3642062`, entbehrliches
`SimpleProject_copy`, V21) wurden live bestätigt: 15/15 `list_network_objects`-Knoten auswählbar; ein
`delete_subnet` eines verbundenen Subnetzes wurde angewendet, und `subnetAbsent`,
`affectedNodesPreserved` und `affectedConnectionsRemoved` bestanden; `add_network_device` eines
ET200SP IM 155-6 PN ST bestand; ein unbekanntes Gerät (`NOPE-99`) lieferte `target_not_found`;
typisierte Verifizierungsidentität und `finalChecks[]`; ein Anschluss an IoSystem 100 auf `PN/IE_1`
bestand seine Prüfungen `IoSystemSubnet`/`IoSystemNumber`; eine ungültige IP erzeugte eine bestandene
abschließende Prüfung zum Erhalt von `Address`; ein Hardware-Lesevorgang eines einzelnen Geräts
lieferte ein leeres `messages[]`; und `pnDeviceNameAutoGeneration:false` gefolgt von einem Schreiben
des Namens wurde angewendet und verifiziert. Der Lauf ist im [Verbesserungsprotokoll](../IMPROVEMENT_LOG.md)
festgehalten.

### Begrenzte Ergebnisse für Vertrag 2.0

Der [Abnahmebericht vom 2026-10-29](../superpowers/acceptance/reports/2026-10-29-network-write-native-rejections-live-verification.md)
und seine eingefrorene funktionale Eingabe halten 17 bestandene erwartete Ergebnisse gegen eine
entbehrliche Fixture fest. O1 bedeutet eine native Ablehnung, O2 einen akzeptierten Schreibvorgang mit
öffentlichen `compile_check`-Diagnosen und O3 einen akzeptierten Schreibvorgang mit stiller
öffentlicher Softwareübersetzung. Kontrollen und MCP-Nachbedingungen sind gesondert gekennzeichnet.
Ein sauberes `compile_check` ist ein Nachweis für die PLC-Software, keine Hardwareabnahme.

| Fall | Probe | Beobachtetes Ergebnis | Frische öffentliche Softwareübersetzung |
| --- | --- | --- | --- |
| L1 | Doppeltes Anlegen eines Geräts | O1: `engineering_rejected`, `EngineeringTargetInvocationException`. | Bestehende PLC sauber; angeforderte neue PLC nicht vorhanden. |
| L2 | Doppeltes Anlegen eines Subnetzes | O1: `engineering_rejected`, `EngineeringTargetInvocationException`. | Sauber. |
| L3 | Kollidierende Subnetzumbenennung | Portal ignorierte die Umbenennung; MCP `postcondition_failed` vor dem Commit. Der anfängliche O3-Befund führte zu dieser Prüfung. | Sauber. |
| L4 | Doppelte IP-Adresse | O3 in der öffentlichen Softwareprobe; Wert übernommen. | Sauber; der Maintainer beobachtete gesondert Kollisionsdiagnosen des Hardware-Compilers. |
| L5 | Doppelter PROFINET-Name | O3 in der öffentlichen Softwareprobe; Wert übernommen. | Sauber; der Maintainer beobachtete gesondert Kollisionsdiagnosen des Hardware-Compilers. |
| L6 | PN-Name bei aktivierter automatischer Namensvergabe | O1: `worker_operation_failed`, `EngineeringNotSupportedException`; keine Einstellungen angewendet. | Sauber. |
| L7 | PROFIBUS-Attribut auf Ethernet-Subnetz | O1: `worker_operation_failed`, `EngineeringNotSupportedException`; Transaktion nicht festgeschrieben. | Sauber. |
| L8 | Fehlerhafte IP-Adresse | O1: `engineering_rejected`, `EngineeringTargetInvocationException`. | Für diesen Fall des Wiederholungslaufs nicht erfasst. |
| L9 | PROFINET-Knoten an PROFIBUS-Subnetz | O1: `engineering_rejected`, `EngineeringTargetInvocationException`. | Sauber. |
| L10 | Schnittstelle ohne IO-Konnektor | Kontrolle der Vorprüfung: `target_not_found` vor den Settern; späteres Element übersprungen. | Sauber. |
| L11 | GSD-Maskenablehnung nach Address | O1: `worker_operation_failed`, `EngineeringNotSupportedException`; Address beibehalten, späterer PN-Setter und Batch-Element nicht erreicht. | Sauber. |
| L11b | PN-Eingabe bei deaktivierter automatischer Namensvergabe akzeptiert | Akzeptierte Kontrolle: Address, Maske und PN-Eingabe abgeschlossen; späteres Subnetz angelegt. Eine frühere Beobachtung des Maintainers hielt den konvertierten PN-Namen fest. | Sauber. |
| L11c | Inkompatibles Verbinden nach Address und Maske | O1: `engineering_rejected`, `EngineeringTargetInvocationException`; beide angewendeten Schlüssel beibehalten; späteres Element übersprungen. | Sauber. |
| L12 | Lifecycle-Pfad mit fünf Operationen | Alle fünf abgeschlossen; Umbenennung durch Löschen `superseded`, vier andere Einträge `trusted`. Der Lösch-Guard listete fünf anfängliche Knoten und ließ die Verbindung aus demselben Aufruf aus. | O2: drei Fehler wegen fehlender Hardware-ID-Symbole auf einer PLC nach dem Löschen; zwei andere PLCs sauber. |
| L13 | Abgelehntes drittes Element | O1: zwei erfolgreich/trusted, eines `engineering_rejected`, zwei übersprungen. | Angelegte PLC sauber. |
| L13mini | Abgelehntes zweites Element | O1: eines erfolgreich/trusted, eines `engineering_rejected`, eines übersprungen. | Für diesen Fall des Wiederholungslaufs nicht erfasst. |
| L14 | Dry Run von L13 | Vorschau-Kontrolle: fünf Effekte, keine Mutation oder Verifizierung. | Nicht ausgeführt. |

Diese Ergebnisse belegen weder die Kompatibilität beliebiger Geräte noch eine Hardware-/Online-Abnahme.
Die L3-Prüfung hat keine Live-Nachweise für ein Rollback gemischter Eigenschaften; ein direkter
fataler Verlust nach einem teilweisen Configure bleibt unerprobt. Betroffene Objekte erneut lesen und
vor der Inbetriebnahme die passenden Schritte zur Hardwareprüfung und Übersetzung durchführen.

## Künftige Roadmap

Die genehmigte übergeordnete Richtung für erweiterte Topologieoperationen über diesen Vertrag hinaus
ist in [network-operations.md](../roadmap/network-operations.md) dokumentiert.
