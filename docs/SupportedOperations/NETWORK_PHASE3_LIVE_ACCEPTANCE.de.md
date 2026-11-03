# Network Phase 3 – Live-Abnahme

**Sprache:** [English](NETWORK_PHASE3_LIVE_ACCEPTANCE.md) · Deutsch

Status: **Designprüfung erforderlich** nach dem Lauf vom 2026-08-05 gegen die vorbereitete, wegwerfbare
Abnahme-Fixture für TIA Portal V21 auf Quellrevision
`702103a5e5223c23db7e0918cdaf12ffd3de38f1`.

Die geforderte Fixture-Matrix, die Payload-Wiederholbarkeit, die Messung des Listennutzens und die Raw-Probe
wurden alle abgeschlossen. Die separate Beibehaltungsbedingung für `list_network_objects` wurde erfüllt. Die
abschließende Stabilisierung von Phase 3 besteht die strengere Regel des Plans zur Verbindungsidentität noch
nicht, weil nur drei von acht beobachteten Kommunikationsverbindungen vollständige Selektoren hatten. Eine
geprüfte Revision des Abnahmevertrags oder eine Änderung der Selektor-Implementierung ist erforderlich, bevor
diese Seite ein Bestehen festhalten kann.

Dieses Gate deckt den rein lesenden Identitäts- und Introspektionsvertrag von Phase 3 ab. Es autorisiert oder
zertifiziert kein Speichern, Übersetzen, Laden, keinen Netzwerk-Schreibvorgang, keine Inbetriebnahmehandlung und
kein Ergebnis auf realer Hardware. Das Harness hat den exakten V21-Produktbuild nicht erfasst, daher gilt dieses
Ergebnis für V21 und nicht für einen bestimmten Wartungsbuild.

## Ausführung und Herkunft

Die vier Modi des inzwischen entfernten Legacy-Abnahmeverfahrens für Phase 3 wurden nacheinander gegen die
aktive Fixture ausgeführt. Jeder Befehl endete mit Exit-Code 0 und schrieb eine JSON-Nachweisdatei mit
Zeitstempel:

| Modus | Nachweis | Ergebnis |
|---|---|---|
| `Matrix` | `artifacts/live-network-phase3/20260805-224308818-matrix.json` | Geforderte Fixture-Matrix vollständig. |
| `Repeatability` | `artifacts/live-network-phase3/20260805-224143879-repeatability.json` | Discovery-Payload und alle gezielten Inspektionen wiederholbar. |
| `MeasureListValue` | `artifacts/live-network-phase3/20260805-224429434-measurelistvalue.json` | Beibehaltungsbedingung 2 erfüllt. |
| `RawProbe` | `artifacts/live-network-phase3/20260805-224534336-rawprobe.json` | Geforderte Raw-Metadaten-Proben vollständig. |

Jedes Artefakt meldet einen sauberen Quellbaum, dieselbe Revision für Quelle, gestarteten Host und gestarteten
Worker sowie `finalCommittedCodeExercised:true`. Die Artefakte bewahren die Hashes der ausführbaren Dateien und
den vollständigen Nachweis je Anfrage. Sie liegen absichtlich im per gitignore ausgeschlossenen Verzeichnis
`artifacts/live-network-phase3/` und sind nicht als Produktdokumentation committet.

## Geforderte Fixture-Matrix

Die Matrix stellte 32 MCP-Anfragen über 21 Discovery-Seiten und beobachtete jede geforderte Fixture-Art:

| Geforderte Fixture | Öffentliche Art | Inspizierte Attribute |
|---|---|---:|
| Verschachteltes Device Item | `deviceItem` | 24 |
| Netzwerkschnittstelle | `networkInterface` | 18 |
| Ethernet-Knoten | `node` | 13 |
| Ethernet-Subnetz | `subnet` | 8 |
| PROFINET-IO-System | `ioSystem` | 6 |
| Kommunikationsverbindung | `communicationConnection` | 18 |

Jede geforderte Art durchlief damit Discovery gefolgt von gezielter Inspektion über das öffentliche
MCP-Protokoll. Die Verbindungs-Fixture enthielt eine auswählbare HMI-Verbindung. Die Verbindungsidentität war
nicht für jede Verbindung im Projekt vollständig; unvollständige Einträge blieben, wie vom Vertrag gefordert,
sichtbar und nicht auswählbar.

## Wiederholbarkeit

Dieselben Eingaben für Discovery und gezielte Inspektion wurden zweimal ausgeführt, ohne das Projekt zu ändern:

| Verglichenes Dokument | Erster Lauf | Zweiter Lauf | Ergebnis |
|---|---:|---:|---|
| Discovery-Payload (`result`, `omission`, `truncation`) | 161.422 Zeichen | 161.422 Zeichen | Identischer SHA-256 `7e2ef41361cc20a27bd8e46299f329b83b3687aa3c82f090365397fd567ad813` |
| Typisiertes Discovery-Ergebnis | 160.477 Zeichen | 160.477 Zeichen | Identischer SHA-256 `c2761c45a266631b44e283653ea23fa6ceb051b3adbc6b9e94fee396b1ce9696` |
| Sechs gezielte Inspektionen | Dieselben kanonischen Bytes | Dieselben kanonischen Bytes | Alle identisch |

`repeatabilityPassed` ist daher für die Netzwerk-Payload und die Selektoren true. Die vollständigen
MCP-Envelopes hatten 167.450 und 167.407 Zeichen und waren absichtlich nicht byte-identisch: Die erste Seite des
ersten Durchlaufs enthielt die einmalige Anbindungswarnung `Connected to running TIA Portal instance.`, der
zweite Durchlauf nicht. Der Unterschied von 43 Zeichen im Envelope ist genau die serialisierte Warnung. Die
Warnung bleibt öffentlicher Nachweis; sie ist nur von der Entscheidung über die Payload-Wiederholbarkeit
ausgenommen, weil sie die Prozessanbindung beschreibt, nicht den Discovery-Zustand.

## Beibehaltungsentscheidung für `list_network_objects`

Der vollständige Discovery-Durchlauf lieferte 208 Objekte über 21 Seiten: 85 auswählbare Objekte mit
vollständigen Selektoren und 123 ausdrücklich nicht auswählbare Objekte. Alle auswählbaren Einträge trugen
Selektoren. Das vollständige kanonische MCP-Ergebnis hatte 167.450 Zeichen und benötigte in dieser Fixture
67.648 ms.

| Genehmigtes Gate | Messung | Entscheidung |
|---|---|---|
| 1. Vollständige Selektoren, wenn `read_hardware_config` 60.000 Zeichen übersteigt | `read_hardware_config` hatte 139.717 Zeichen, aber Discovery behielt korrekt 123 Objekte ohne vollständige Selektoren. | Nicht erfüllt. |
| 2. Eine repräsentative gezielte Abfrage ist mindestens 50 % kleiner und bewahrt jeden passenden Selektor | Gerät `ET 200SP station_2`: 56.366 gegenüber 167.450 Zeichen, eine Reduktion um 66,34 %; jeder passende Selektor blieb erhalten. | **Erfüllt.** |
| 3. Eine reine Verbindungsabfrage liefert alle Verbindungsselektoren innerhalb des Budgets und vermeidet einen Aufruf des vollständigen Baums | 3 auswählbare Selektoren blieben aus 8 beobachteten Verbindungen erhalten; das Ergebnis hatte höchstens 6.692 Zeichen, aber das gemessene `fullTreeCallsAvoided` war 0. | Nicht erfüllt. |

Da die genehmigte Bedingung 2 erfüllt wurde, wird `list_network_objects` beibehalten. Die Entscheidung hängt
nicht davon ab, unvollständige Identität als vollständig zu behandeln: Der Nutzen der Operation ist die
begrenzte gezielte Abfrage unter Beibehaltung ausdrücklicher `selectable:false`-Einträge und Diagnosen.

## Raw-Attributmetadaten

Die interne, rein lesende Raw-Probe beobachtete `name`, `accessMode`, `supportedClrTypeNames` und
`observedClrValueType` für jede geforderte Fixture-Art. Sowohl rein lesende als auch lesend-schreibende
Metadaten wurden für alle sechs Arten beobachtet. Die Anzahl der Raw-Metadateneinträge betrug 24 für das
verschachtelte Device Item, 18 für die Netzwerkschnittstelle, 13 für den Knoten, 7 für das Subnetz, 6 für das
IO-System und 14 für die Kommunikationsverbindung.

Die öffentlichen Inspektionsergebnisse blieben innerhalb des geschlossenen Wertevokabulars `null`, `string`,
`boolean`, `integer`, `number` und `enum`. Verfügbarkeit und Diagnosen blieben je Attribut; ein nicht lesbarer
oder nicht darstellbarer Wert unterdrückte keine späteren Attribute. Die Raw-Probe ist eine reine
Worker-Abnahmediagnose und ist weder in der MCP-Tool-Discovery noch im öffentlichen Operationskatalog von
`network_read` vorhanden.

## Ausdrückliche Abdeckungsgrenzen

- Es wurde keine PROFIBUS-Schnittstelle, kein DP-Mastersystem und keine andere PROFIBUS/DP-Fixture beobachtet.
  Dieser Lauf belegt diese Pfade weder, noch widerlegt er sie.
- Nur die Verbindungsklasse der HMI-Kommunikationsverbindung erfüllte die geforderte auswählbare
  Verbindungs-Fixture. Die Verbindungsklassen S7, FDL, ISO, ISO-on-TCP, PTP, TCP und UDP wurden nicht live
  getestet.
- Acht Kommunikationsverbindungen wurden beobachtet, aber nur drei hatten einen vollständigen öffentlichen
  Selektornachweis. Die anderen fünf bleiben als `selectable:false` sichtbar; der Vertrag erfindet keine
  fehlende Identität.
- Statische Tests und der Siemens-Stub-Build decken Host-Verträge und Siemens-freie Logik ab. Sie ersetzen
  diesen Live-Openness-Nachweis nicht, und diese Live-Fixture ersetzt weder die Inbetriebnahme noch die Abnahme
  auf physischer Hardware.
