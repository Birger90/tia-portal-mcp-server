# Netzwerkoperationen Phase 4: Lebenszyklus von Subnetzen

**Sprache:** [English](NETWORK_PHASE4_SUBNET_LIFECYCLE.md) · Deutsch

Aktueller Vertrag von `network_write`: **2.0**, offline qualifiziert, mit Abnahme des installierten Kandidaten auf
TIA Portal V21 im festgelegten Umfang. Der [Bericht vom 2026-10-29](../superpowers/acceptance/reports/2026-10-29-network-write-native-rejections-live-verification.md)
verzeichnet alle 17 erwarteten Ergebnisse, Timing und Wiederherstellung. Die unten datierten Läufe von Phase 4 behalten ihre
früheren Verträge und Grenzen der Belege.

Historischer Status von Phase 4: Anlegen/Aktualisieren/Löschen von Subnetzen ist implementiert, und gezielte automatisierte Gates verifizieren
die Vertragsreparaturen aus PR 1. Das frühere statische Audit fand Abweichungen in der ursprünglichen Implementierung
des Vertrags. Ein historischer Live-Lauf gilt nur für seinen aufgezeichneten älteren Commit. Der erste
öffentliche Versuch mit der aktuellen Revision am 2026-09-23 scheiterte am Löschen verbundener Subnetze; die frische, abgesicherte
öffentliche Wiederholung am 2026-09-24 bestand alle acht Lebenszyklusoperationen einschließlich beider verbundener Löschungen. Ein
separater abschließender Lesevorgang beobachtete null Subnetze und insgesamt 81 Hardwaregeräte. Siehe „Belegstatus“
unten für das begrenzte Live-PASS und die verbleibenden Grenzen des Rücklesens.

Designbegründung, Belegbasis und verworfene Alternativen sind in
[ADR-0012](../adr/0012-portal-as-rejection-authority.md) festgehalten.
Der historische Implementierungsplan Aufgabe für Aufgabe und sein früherer Locked Public Contract liegen im
[archivierten Plan](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-08-06-network-phase4-subnet-lifecycle.md).
Siehe [../roadmap/network-operations.md](../roadmap/network-operations.md) dafür, wie diese Phase in die
gesamte Netzwerk-Roadmap passt, und
[NETWORK_OPERATIONS_SUMMARY.md](NETWORK_OPERATIONS_SUMMARY.de.md) für die bisher unterstützten
Netzwerkoperationen und den aktuellen abgesicherten kanonischen Vertrag. Die historische Abnahme von Phase 4 verwendet ihren aufgezeichneten früheren Vertrag.

## Unterstützte Operationen

Phase 4 fügt dem bestehenden Tool `network_write` genau drei Schreiboperationen hinzu. Es wurde kein neues MCP-Tool
hinzugefügt. Die aktuelle Oberfläche umfasst 11 Tools in read-only und 30 in jedem schreibfähigen Modus. `network_write` ist in read-write und full verfügbar.

| Operation | Zweck | Erforderliche Request-Felder |
|---|---|---|
| `create_subnet` | Ein neues Ethernet- oder PROFIBUS-Subnetz anlegen. | `subnet: { name, networkType }` |
| `update_subnet` | Ein bestehendes Subnetz umbenennen oder dessen PROFIBUS-Attribute ändern. | `target: { kind: "subnet", subnetId }`, `subnetChanges` (mindestens ein Member) |
| `delete_subnet` | Ein bestehendes Subnetz löschen, verbunden oder nicht. | `target: { kind: "subnet", subnetId }` |

Diese drei Operationen existierten nie im ausgemusterten generischen Katalog `preview_write_batch` /
`apply_write_batch`, haben keinen Alias und werden nicht über `network_read` bereitgestellt.

### Request-Formen

`create_subnet`:

```json
{
  "operationId": "create-pb-1",
  "operation": "create_subnet",
  "subnet": {
    "name": "PROFIBUS_LINE_2",
    "networkType": "Profibus",
    "highestAddress": 126,
    "transmissionSpeed": "Baud1500000"
  }
}
```

`update_subnet`:

```json
{
  "operationId": "update-pb-1",
  "operation": "update_subnet",
  "target": {
    "kind": "subnet",
    "subnetId": "590-5"
  },
  "subnetChanges": {
    "name": "PROFIBUS_LINE_3",
    "highestAddress": 62,
    "transmissionSpeed": "Baud93750"
  }
}
```

`delete_subnet`:

```json
{
  "operationId": "delete-pb-1",
  "operation": "delete_subnet",
  "target": {
    "kind": "subnet",
    "subnetId": "590-5"
  }
}
```

`subnet` (Anlegen) und `subnetChanges` (Aktualisieren) sind strikte verschachtelte Objekte: Nicht abgebildete JSON-Member werden
von der Deserialisierung abgelehnt, wie bei jedem anderen Feld von `NetworkOperationRequest`. Keines der beiden DTOs
akzeptiert ein Member `subnetId`, und `subnetChanges` akzeptiert kein `networkType` -- die Identität und der Netzwerktyp
eines Subnetzes können über diesen Vertrag nie geschrieben werden.

## Schreibbare Werte

- `networkType` (nur Anlegen) ist exakt und beachtet Groß-/Kleinschreibung: `Ethernet` oder `Profibus`. Kein anderer Wert
  wird akzeptiert, und er kann nicht geändert werden, sobald ein Subnetz existiert.
- `name` ist beim Anlegen erforderlich und nicht leer; beim Aktualisieren optional, aber nicht leer, wenn angegeben. Er wird
  exakt erhalten -- nie getrimmt oder normalisiert.
- `highestAddress` gilt nur für PROFIBUS und muss eine ganze Zahl von `0` bis `126` sein. Ihn beim Anlegen eines
  Ethernet-Subnetzes anzugeben, wird als Request-Validierung abgelehnt. Ein Update verlässt sich auf die native
  Anwendbarkeitsregel des Portals; das Ethernet-Update im festgelegten Umfang scheiterte mit `EngineeringNotSupportedException`
  (`worker_operation_failed`).
- `transmissionSpeed` gilt nur für PROFIBUS und muss genau eines dieser zehn Symbole sein: `Baud9600`,
  `Baud19200`, `Baud45450`, `Baud93750`, `Baud187500`, `Baud500000`, `Baud1500000`, `Baud3000000`,
  `Baud6000000`, `Baud12000000`. Das Siemens-Enum-Member `None` ist kein akzeptierter Request-Wert, und
  keine andere Schreibweise, Variante der Groß-/Kleinschreibung oder numerische Baudrate wird akzeptiert.
- Regeln zur Request-Form, zur Anwendbarkeit beim Anlegen und zu Bereichen/Enums werden zweimal durchgesetzt: einmal durch die statische Request-Validierung
  des Hosts (`NetworkOperationCatalog`) vor jedem Worker-Aufruf und erneut durch den Worker
  (`TiaMcpServer.OpennessWorker/Program.cs`) unmittelbar bevor die Openness-Transaktion geöffnet wird.

## Exakte Adressierung per `subnetId`

`update_subnet` und `delete_subnet` verlangen, dass `target.kind` genau `"subnet"` ist (ordinal,
mit Beachtung der Groß-/Kleinschreibung), und wählen ein bestehendes Subnetz nur über seine exakte `subnetId` aus, abgeglichen
mit ordinaler String-Gleichheit (mit Beachtung der Groß-/Kleinschreibung) gegen `HardwareConfigInfo.Subnets` in einem Planungs-Snapshot
pro Aufruf. Eine Vorschau und ein späteres Anwenden sind getrennte Aufrufe; die Mutation löst die exakte ID unabhängig
im Worker erneut auf, ohne einen weiteren Host-Snapshot:

- es gibt keinen Rückfall auf `name`, Sammlungsindex, verbundenes Gerät oder „ersten Treffer“;
- eine `subnetId` in anderer Groß-/Kleinschreibung passt nicht;
- beim Host-Preflight scheitern null Treffer geschlossen mit `target_not_found` und mehr als ein Kandidat,
  der dieselbe `subnetId` meldet, mit `target_ambiguous`; eine späte erneute Auflösung im Worker nach dem Lesevorgang des Hosts,
  die null oder mehrere findet, scheitert mit `postcondition_failed`;
- ein Zielsubnetz, dessen eigener `networkType` fehlt oder außerhalb von `Ethernet`/`Profibus` liegt, scheitert geschlossen
  mit `target_kind_unsupported` und wird nie als Schreibziel aufgelöst.

`create_subnet` akzeptiert nie eine vom Aufrufer gelieferte `subnetId`: Openness vergibt sie, und die Identität des angelegten
Subnetzes wird erst nach dem Commit der Transaktion im Ergebnis zurückgemeldet.

## Nur Ethernet und PROFIBUS

Phase 4 unterstützt nur die beiden Subnetztypen, die bereits von `SubnetInfo.NetworkType` bereitgestellt werden. Sie fügt nicht
hinzu und beansprucht nicht, hinzuzufügen:

- Verbinden/Trennen von Knoten, Anlegen oder Bearbeiten von IO-Systemen oder Verwaltung von Kommunikationsverbindungen;
- Behandlung integrierter PROFIBUS- oder PROFIdrive-Netze;
- Ethernet-`DefaultSubnet`, PROFIBUS-`BusProfile`, Taktsynchronitätseinstellungen oder andere `Pb*`-Attribute;
- generische Schreibvorgänge dynamischer Attribute auf Subnetzen oder irgendeinem anderen Netzwerkobjekt;
- Anlegen, Löschen von Geräten oder anderes Verhalten im Gerätelebenszyklus;
- Speichern des Projekts oder Übersetzen der Hardware als Teil einer Subnetzoperation.

## Semantik des Löschens

`delete_subnet` akzeptiert sowohl leere als auch verbundene Subnetze. Das Löschen eines verbundenen Subnetzes:

- **wird mit vollständigen Belegen zu den Folgen unterstützt** -- betroffene Knoten/Beziehungen werden
  im initialen Planungs-Snapshot erfasst. Ein früher im selben Aufruf verbundener Knoten wird dieser Liste nicht hinzugefügt.
  Das Löschen verbundener Subnetze ist informativ; eine unvollständige Bestandsaufnahme ist nie ein leerer Beleg und informiert (`network_state_unverifiable`, Info seit 2026-11-01; TIA Portal entscheidet);
- **löscht keine Geräte** -- `SubnetLifecycleService.Delete` im Worker ruft nur
  `Subnet.Delete()` auf. Vertrag 2.0 hat keine Zusicherung zur Geräteanzahl und kein Rücklesen zum Erhalt;
- **kann netzwerkbezogene Geräteattribute als logische TIA-Wirkung löschen** -- das Entfernen der Beziehung eines Subnetzes
  zu einem Knoten oder IO-System ist eine erwartete Folge des Löschens des Subnetzes selbst in
  TIA Portal, kein Defekt. Ein Aufrufer, der den Zustand danach sehen muss, ruft `network_read` (`read_hardware_config` oder
  `inspect_network_object`) separat auf.

Bei jedem Löschen löst der Worker das exakte Ziel einmal innerhalb der Transaktion auf und erfasst
einen nicht leeren Namen aus demselben transaktionslokalen Subnetzobjekt,
und ruft dann `Delete()` darauf auf. Kein Subnetzobjekt aus der Zeit vor der Transaktion liefert die Identität der Mutation.
Die Validierung unterstützter Typen verwendet den Planungs-Snapshot des Hosts.

## Typisiertes Ergebnis

Jedes erfolgreiche Element des Subnetz-Lebenszyklus hat diese zwei erforderlichen Member, typisiert durch
`TiaMcpServer.Contracts.Network.SubnetLifecycleResultInfo` und durchgesetzt von `NetworkPayloadContract`:

```json
{
  "subnetId": "590-5",
  "name": "PROFIBUS_LINE_3"
}
```

- `subnetId` und `name` sind nicht leer. Fehlende oder falsch typisierte Member lassen das Dekodieren als
  `protocol_error` scheitern. Es gibt in diesem Ergebnis kein Verifikationsobjekt des Workers und keine Geräteanzahl.
- Das Anlegen erfasst die native ID innerhalb der Transaktion und liefert nach dem
  Commit den angeforderten Namen. Eine nicht lesbare ID des angelegten Subnetzes ist nach dem Commit `postcondition_failed`: vor dem Wiederholen prüfen.
- Das Aktualisieren liefert die angeforderte ID und den innerhalb der Transaktion erfassten Namen. Wurde eine angeforderte Umbenennung
  stillschweigend ignoriert, lässt ihr ordinaler Namensvergleich vor dem Commit `postcondition_failed` scheitern.
  Der Kollisionsfall L3 bewies diese Abweichung; ein Rollback bei gemischten Eigenschaften wurde live nicht durchgespielt.
- Das Löschen liefert die ID und den nicht leeren Namen, die unmittelbar vor `Delete()` erfasst wurden.
- Die Verifikation auf oberster Ebene meldet `trusted` für abgeschlossene Operationen oder `superseded`, wenn eine spätere
  versuchte Operation dasselbe Subnetz besitzt. Fehlgeschlagene/übersprungene Elemente haben keinen Verifikationseintrag.
  Sie führt kein Rücklesen zur Abwesenheit des Subnetzes, zu Attributen oder zum Erhalt von Geräten durch.
- Das umschließende strukturierte Operationselement trägt bereits den Operationsnamen (`create_subnet`,
  `update_subnet` oder `delete_subnet`) und den Status, sodass das Ergebnis selbst kein
  created/updated/deleted-Verb wiederholt.

## Transaktionen, Batching und Wiederholung

- Jede Subnetzoperation öffnet genau ein Openness-`ExclusiveAccess` und eine `Transaction`, führt
  jeden angeforderten Setter darin aus und ruft `CommitOnDispose()` erst auf, nachdem jeder Setter
  erfolgreich war. Eine Ausnahme vor diesem Aufruf rollt die Operation zurück.
- Ein `network_write`-Batch mit Subnetzoperationen bleibt über Elemente hinweg sequenziell und **nicht atomar**,
  genau wie jeder andere `network_write`-Batch: Der Batch stoppt beim ersten fehlgeschlagenen Element,
  und ein früheres erfolgreiches Element im selben Aufruf -- Subnetz oder anderes -- wird nicht zurückgerollt. Die
  Tool-Beschreibung gibt das als „no batch-wide rollback.“ an.
- Nach einem gestoppten Batch trägt das fehlgeschlagene Element eine explizite Warnung, dass diese Operation und jede
  frühere Operation im selben Aufruf den TIA-Zustand bereits verändert haben können und dass der Aufrufer
  vor dem Wiederholen mit `network_read` erneut lesen muss.
- Der Server wiederholt eine Subnetz-Mutation nie automatisch. `postcondition_failed` kann eine
  späte Selektor-Drift, eine ignorierte Umbenennung vor dem Commit oder eine nicht lesbare ID des angelegten Subnetzes nach dem Commit melden.
  Den aktuellen Projektzustand prüfen, bevor entschieden wird, ob manuell wiederholt wird.

## Vorschau, Anwendung und Sicherheit

Der Subnetz-Lebenszyklus verwendet das abgesicherte `network_write(operations, dryRun=false)`. **Weggelassenes dryRun führt aus**;
explizites `dryRun:true` prüft Effekte/Guards ohne Mutation oder Elicitation, und `dryRun:false`
führt in der Reihenfolge des Aufrufers aus. Keine Network-Server-Elicitation in read-write/full. Eine Vorschau reserviert keinen
Zustand; jeder Aufruf plant einen aktuellen Snapshot unter einem fixierten, bereits geöffneten
verifizierten Binding. Das Löschen verbundener Subnetze ist `info`; eine unvollständige Bestandsaufnahme ist ein nicht übersteuerbarer `block`.
Es gibt keine Neuplanung pro Element bei Network. Der Löscheffekt von L12 listete fünf anfänglich verbundene Knoten
und ließ den früher in diesem Aufruf verbundenen Knoten weg; frische, nicht zeitgemessene N2/N4-Timing-Ziele begannen
mit null gelisteten Verbindungen. Aufrufe aufteilen und erneut prüfen, wenn die neue Verbindung erscheinen muss.

Veraltetes `confirm`, `acknowledge`, unbekannte Root-Argumente und nicht boolesches dryRun
werden vor dem Eintritt ins Tool mit dem kanonischen Dokument abgelehnt (`validation_error`, `isError:true`), ohne Audit-Eintrag. Begonnene Ablehnungen bei Validierung/
Binding/Guard verwenden einen kanonischen Root-Fehler und ein Audit. Versuchte Teilfehler bleiben
`phase:applied`, `error:null`, MCP `isError:false`; Stopp beim ersten Fehler, kein Batch-Rollback/-Replay.

Jeder begonnene Aufruf hängt einen Audit-v2-Eintrag an: tatsächliche Bestätigung in read-write `none`, tatsächlich in full `policy`,
Vorschauen/Ablehnungen `none`, Erfüllung von Info/Block null. Der exakt ausgelieferte kanonische Text/Hash wird gespeichert;
Elementstatus behalten die Wahrheit der Ausführung, auch wenn Auslassungen ganzer Werte den Auslieferungserfolg auf false setzen.
Siehe den [aktuellen Network-Umschlag](NETWORK_OPERATIONS_SUMMARY.md#network_write-envelope).

## Grenze zu Speichern und Übersetzen

`SubnetLifecycleService` ruft nie `Project.Save()` auf und löst kein Übersetzen aus. Speichern und das öffentliche
Übersetzen der PLC-Software bleiben getrennte, explizite Operationen (`save_project`, `compile_check`).
Das Übersetzen der Hardware ist eine separate TIA-Operation; `compile_check` stellt nicht die Gültigkeit der
Hardwarekonfiguration fest. Weil das Löschen eines verbundenen Subnetzes
absichtlich die entsprechenden Netzwerk-/IO-System-Beziehungen löscht, kann ein anschließendes Übersetzen
Diagnosen melden, die durch diese Trennung verursacht sind; diese Diagnosen bedeuten nicht, dass das Löschen des Subnetzes
selbst gescheitert ist.

## Ausdrückliche Nicht-Ziele

Phase 4 fügt nichts von Folgendem hinzu, und diese Dokumentation beansprucht nichts davon:

- Anbinden/Abtrennen von Knoten, Anlegen/Bearbeiten von IO-Systemen oder Anlegen, Bearbeiten oder
  Löschen von Kommunikationsverbindungen;
- generische Schreibvorgänge von Netzwerkattributen (laut Roadmap auf eine spätere Phase verschoben);
- Behandlung integrierter PROFIBUS- oder PROFIdrive-Netze;
- Speichern des Projekts oder Übersetzen der Hardware als Nebenwirkung einer Subnetzoperation;
- Anlegen, Löschen von Geräten oder jedes andere Verhalten im Gerätelebenszyklus;
- Auswahl des Online-Verbindungspfads, Ermittlung erreichbarer Geräte, Laden, Inbetriebnahme oder
  Validierung der Hardware-Laufzeit;
- automatische Wiederholung irgendeiner Subnetz-Mutation.

### Zurückgestellt: Problem mit GSD-abgeleiteten Hardwarenamen

Die nur lesende Probe aus Phase 3 beobachtete wiederholte Knotennamen von Netzwerkkomponenten für ABB-Frequenzumrichter, die
über GSD-Hardwaredefinitionen installiert waren und sich von ihren Namen im Quellprojekt unterschieden. Das legt nahe, dass
Namen oder Attribute, die von GSD-abgeleiteten Komponenten bereitgestellt werden, möglicherweise nicht über dieselben Pfade lesbar sind wie
native Katalog-Hardware. Diese Beobachtung ist nur für spätere Arbeiten zur Hardware-Introspektion festgehalten.
Phase 4 verwendet nie Namen von Geräteelementen, Knotennamen oder andere GSD-abgeleitete Attribute als Subnetzidentität,
Sicherheitsbeleg, Beleg für Nachbedingungen oder öffentliche Ergebnisdaten -- die Subnetzidentität ist immer die
von Openness vergebene `subnetId`.

## Belegstatus

Die Belege zu Phase 4 haben unterschiedliche Geltungsbereiche:

- **Historische interne Probe-Belege.** Die inzwischen ausgemusterte, nicht öffentliche
  Worker-Operation `probe_subnet_lifecycle_mutations` spielte während des Designs das Anlegen, Bearbeiten und
  Löschen von Subnetzen (einschließlich verbundener Subnetze) direkt gegen ein echtes TIA-Portal-V21-Projekt durch.
  Diese Belege prägten den Locked Public Contract, spielten aber nicht den öffentlichen Codepfad durch.
  Ihre Berichte und Roheingaben sind erhalten; die Worker-Route und ihr Service sind entfernt.
- **Reparierte statische Implementierung.** Gezielte automatisierte Gates bestanden für exakte Zielart,
  erforderliche rohe Ergebnis-Member, Klassifizierung später Drift, transaktionslokale Löschidentität und
  den wiederhergestellten abgesicherten Harness. Der ausführbare Reparatur-Commit
  `6ce302ea6d096aefd92dfcacceff7711a09d8e71` bestand 3098/3098 vollständige Debug-Tests,
  Release-Builds mit echter Referenz und mit Stubs sowie 3098/3098 Release-Coverage-Tests. Das reine Dokumentations-Re-Pin
  vom 2026-09-24 auf `56e2248eacca44ea55c7d246d3c8ceb589b353b9` bestand 298 gezielte Tests
  und einen Debug-Host-Build mit echter Referenz als normaler Benutzer; sein vollständiger Debug-Lauf in der Sandbox ergab 3094/3098
  mit vier Umgebungsfehlern. Die vollständigen Release- und Coverage-Gates wurden für dieses
  Re-Pin nicht wiederholt. Statische Belege beweisen **nicht** das Laufzeitverhalten von Openness über den öffentlichen MCP-Pfad.
- **Historischer Lauf über den öffentlichen Pfad.** Der frühere Lauf von Phase 4 ist nur ein Beleg für den älteren Commit,
  der mit diesem Lauf aufgezeichnet wurde. Er kann das Verhalten des aktuellen PR-1-Baums nach diesen Reparaturen nicht belegen.
- **Erster Versuch über den öffentlichen Pfad mit der aktuellen Revision -- unvollständig am 2026-09-23.** Bestandsaufnahme,
  Vorschau, erwartete negative Kategorien und isoliertes Anlegen/Aktualisieren/Löschen von Ethernet/PROFIBUS
  wurden abgeschlossen. TIA verweigerte das Löschen des verbundenen Ethernet-Subnetzes wegen fehlender Berechtigung zum Ändern des Sicherheitsprogramms
  und übersprang das Löschen des verbundenen PROFIBUS-Subnetzes. Das Portal meldete nicht gespeicherte Änderungen. Der
  Benutzer bestätigte anschließend, dass die Wegwerfkopie ohne Speichern geschlossen und erneut geöffnet wurde.
- **Frische Wiederholung über den öffentlichen Pfad mit der aktuellen Revision -- begrenztes Live-PASS am 2026-09-24.** Nachdem sich der Benutzer
  an der Safety angemeldet hatte, steuerte der abgesicherte Harness das öffentliche `network_read`/`network_write` auf dem
  eingefrorenen Kandidaten. Bestandsaufnahme und Vorschau meldeten `isModified=false`; alle acht
  Lebenszyklusoperationen waren erfolgreich, einschließlich beider ursprünglich verbundener Löschungen. Eine separate abschließende Bestandsaufnahme
  meldete null Subnetze, insgesamt 81 Hardwaregeräte (auch 81 vor dem Anwenden) und
  `isModified=true`. Jedes Operationsergebnis meldete auf oberster Ebene `networkDeviceCount=10` und
  `networkDeviceCountUnchanged=true`, aber dem Harness fehlte eine unabhängige Basislinie der Root-Anzahl vor dem Anwenden.
  Er las die Identitäten der erhaltenen Geräte oder die Attribute von Knoten und IO-Systemen nicht unabhängig zurück.
  Es wird kein Ergebnis zu Speichern, Übersetzen, Laden oder Persistenz beansprucht. Siehe den
  [Live-Bericht zur aktuellen Revision](../superpowers/acceptance/reports/2026-09-21-network-phase4-current-revision-live.md).
