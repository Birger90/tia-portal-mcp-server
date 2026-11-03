# S7-PLCSIM-Advanced-Operationen (`plcsim_read`, `plcsim_write`, `plcsim_test`)

**Sprache:** [English](PLCSIM_OPERATIONS_SUMMARY.md) · Deutsch

> **Nicht live getestet.** Dieser Bereich wurde nur offline gebaut und qualifiziert: Es stand keine
> Installation von S7-PLCSIM Advanced zur Verfügung. Jedes Siemens-Member wird zur Laufzeit per
> Reflection gebunden. Die Member-*Namen* in der Tabelle der API-Oberfläche unten sind gegen das
> Siemens-Handbuch verifiziert, und die als *Referenzsignatur* gekennzeichneten `IInstance`-Signaturen
> gegen die Siemens-Entwicklerreferenz für API 8.0; `probe_api` bleibt die Prüfung für die installierte
> Version. Vor dem Einsatz dieser Tools die
> [Schritte der Live-Abnahme](#schritte-der-live-abnahme-sobald-plcsim-advanced-installiert-ist) - mit
> `probe_api` zuerst - ausführen. Erste Live-Beobachtungen vom 2026-11-02 sind unter
> [Live-Beobachtungen](#live-beobachtungen-2026-11-02) festgehalten; sie ersetzen die Abnahmeschritte
> nicht.

Drei Tools steuern virtuelle S7-1500-CPUs von S7-PLCSIM Advanced ("Instanzen") über einen separaten
PLCSIM-Worker-Prozess. Sie berühren nie TIA Portal, den Openness-Worker oder die Projektbindung.

| Tool | Modi | Zweck |
|---|---|---|
| `plcsim_read` | read-only, read-write, full | Bis zu 50 voneinander unabhängige Lesezugriffe: `probe_api`, `list_instances`, `get_instance`, `list_tags`, `read_tags`. |
| `plcsim_write` | read-write, full | Bis zu 50 geordnete Steueroperationen: `power_on`, `power_off`, `run`, `stop`, `memory_reset`, `update_tag_list`, `write_tags`, `set_scale_factor`; `dryRun` plant nur. |
| `plcsim_test` | read-write, full | Ein Szenario aus bis zu 200 `write`/`wait`/`expect`-Schritten, ausgeführt im Worker; `dryRun` plant nur. |

Die Tool-Anzahlen des Servers, diese drei eingeschlossen, betragen 10 (read-only), 27 (read-write) und
27 (full).

## Voraussetzungen

- 64-Bit-Windows mit .NET Framework 4.8 (der Worker ist ein net48-x64-Prozess).
- S7-PLCSIM Advanced installiert und lizenziert. Der Worker lädt
  `Siemens.Simatic.Simulation.Runtime.Api.x64.dll` aus der Installation; mit diesem Server wird keine
  Siemens-Binärdatei ausgeliefert.
- Eine im **S7-PLCSIM Advanced Control Panel** angelegte Instanz (und, für den Variablenzugriff, ein
  darauf geladenes Programm). Diese Tools legen nie Instanzen an, registrieren sie nie und heben ihre
  Registrierung nie auf.
- Nur S7-1500-CPUs (PLCSIM Advanced simuliert S7-1500/ET-200SP-CPUs).
- Um ein Programm in eine Instanz zu laden, im Control Panel den Online-Zugang auf **PLCSIM**
  (Softbus) stellen und über die PC-Schnittstelle `PLCSIM` laden: mit `plc_download` dieses Servers
  (jede PC-Schnittstelle; die Schnittstelle `PLCSIM` erreicht die Simulation, andere werden als
  Hardware behandelt; siehe die [PLC-Download-Referenz](PLC_DOWNLOAD_SUMMARY.de.md)) oder aus TIA
  Portal selbst.

## Prozessmodell

```text
TiaMcpServer (net10.0 host)
    |-- openness-worker/TiaMcpServer.OpennessWorker.exe   (TIA Portal Openness)
    '-- plcsim-worker/TiaMcpServer.PlcSimWorker.exe       (S7-PLCSIM Advanced, net48 x64)
```

- `PlcSimWorkerClient` startet den PLCSIM-Worker verzögert beim ersten PLCSIM-Aufruf, der die
  Validierung und die Zugriffsprüfungen des Hosts besteht. Tool-Discovery, Ablehnungen von Argumenten,
  Validierungsfehler und Probeläufe (dry runs) starten ihn nie.
- Der Worker verwendet die Openness-Übertragungshülle (`WorkerRequest`/`WorkerResponse`,
  zeilengetrenntes JSON) und `PersistentWorkerTransport` wieder, beantwortet `hello` aber mit seinem
  eigenen Protokoll (`plcsim-v1`, Fähigkeiten `plcsim-reflection-api-surface-v1`,
  `plcsim-exact-instance-name-v1`, `plcsim-test-runner-v1`, `plcsim-restart-flag-v1`; der Host
  verlangt jede davon, sodass ein Worker, der das Neustart-Flag nicht setzt, beim Handshake abgelehnt
  wird). Ein langlebiger Prozess; eine Zeitüberschreitung oder ein Absturz beendet ihn, und der nächste
  Aufruf startet einen neuen. Parameter werden in `WorkerRequest.PlcSim` übertragen.
- Anfragen werden **serialisiert**: Der gemeinsame Transport lässt jeweils einen PLCSIM-Aufruf zu,
  und ein wartender Aufruf hat kein eigenes Warteschlangen-Timeout (nur der laufende Aufruf ist durch
  das Worker-Anfrage-Timeout begrenzt). Ein laufendes `plcsim_test`-Szenario blockiert deshalb jeden
  anderen PLCSIM-Aufruf, auch Lesezugriffe und `probe_api`, für bis zu seine `maxDurationMs` (unterhalb
  des Worker-Timeouts begrenzt); ein langsamer Steueraufruf blockiert sie ebenso. Openness-Tools
  verwenden ihren eigenen Worker und werden nicht blockiert.
- Die ausführbare Datei des Workers ist der paketierte Ordner `plcsim-worker/` neben dem Host. Nur wenn
  er fehlt, wird ein Entwicklungs-Build verwendet, und nur unterhalb des nächstgelegenen
  übergeordneten Verzeichnisses des Host-Verzeichnisses, das `TiaMcpServer.slnx` enthält
  (`TiaMcpServer.PlcSimWorker/bin/Release/net48`, dann `Debug`); kein anderes übergeordnetes
  Verzeichnis wird durchsucht.
- Der Host **ersetzt** den Worker außerdem durch einen neuen, wenn der Worker meldet, dass ein Neustart
  erforderlich ist (`WorkerResponse.workerRestartRequired: true`), und nach einem erfolgreichen
  `probe_api`, dessen typisiertes Ergebnis `apiInitializationFailed: true` meldet. Der Worker setzt das
  Flag bei jeder Antwort, sobald dieser Prozess einen fehlgeschlagenen Typinitialisierer der API
  gesehen hat (zum Beispiel den statischen Initialisierer von `SimulationRuntimeManager`, während der
  Runtime Manager nicht lief), unabhängig davon, ob der Fehler für die ganze Anfrage oder für eine
  Variable, ein Item oder einen Testschritt gemeldet wurde: .NET behält einen fehlgeschlagenen
  Typinitialisierer für die Lebensdauer des Prozesses, sodass nur ein neuer Prozess es erneut versuchen
  kann. Eine fehlende oder nicht ladbare DLL (`plcsim_api_unavailable` ohne das Flag) führt nicht zum
  Ersetzen; derselbe Worker versucht das Laden bei der nächsten Anfrage erneut. Der alte Prozess wird
  beendet, sobald keine gleichzeitige Anfrage ihn mehr verwendet; der nächste Aufruf startet einen
  neuen.
- Das Worker-Anfrage-Timeout ist derselbe Wert `--worker-timeout-minutes` wie beim Openness-Worker.
- Der Worker schreibt Protokollzeilen auf das echte stdout und leitet `Console.Out` auf stderr um,
  sodass Ausgaben der Siemens-API den Protokollstrom nicht verfälschen können.

## Sicherheit und Zugriff

- Worker-Methoden tragen das Präfix `plcsim_` (flacher Namensraum von `OperationPolicyCatalog`).
  Lesezugriffe (`plcsim_probe_api`, `plcsim_list_instances`, `plcsim_get_instance`, `plcsim_list_tags`,
  `plcsim_read_tags`) sind `Observe`. Jede andere Methode (`plcsim_power_on`, `plcsim_power_off`,
  `plcsim_run`, `plcsim_stop`, `plcsim_memory_reset`, `plcsim_update_tag_list`, `plcsim_write_tags`,
  `plcsim_set_scale_factor`, `plcsim_run_test`) ist die neue Fähigkeit `SimulationControl`, als letzte
  an `OperationCapability` angehängt (numerischer Wert 7), erlaubt in read-write und full und
  verweigert in read-only.
- Die Durchsetzung ist wie bei den Openness-Tools geschichtet: Schreib-Tools sind in read-only nicht
  registriert, der Host autorisiert vor dem Start des Workers, und der Worker autorisiert (mit
  `WorkerOperationAuthorization` des Openness-Workers) vor jedem Aufruf der PLCSIM-API. Unbekannte
  Methoden werden verweigert.
- PLCSIM-Methoden sind nicht projektbezogen: `ProjectCapabilityCatalog.OperationFor` gibt für sie null
  zurück, und `RequiresExpectedSessionIdentity` ist für sie false.
- Keine Server-Elicitation und keine Bestätigungsrückfrage in irgendeinem Modus.
- Audit: Jeder `plcsim_write`/`plcsim_test`-Aufruf, der das Tool erreicht - Probeläufe, Validierungs-
  und Zugriffsverweigerungen eingeschlossen - hängt über `IWriteAuditSink` der Pipeline für
  abgesicherte Schreibzugriffe einen Eintrag an (`writes-yyyy-MM-dd.jsonl` im selben
  Audit-Verzeichnis); dabei wird die Form von `WriteAuditRecord` ohne die Projektbindungs-Pipeline
  wiederverwendet: `recordKind:"plcsim"`, `projectPath:null`, `binding:null`, das `target` jedes Items
  ist der Instanzname, die Bestätigung ist `none` (oder `policy` für einen tatsächlichen Aufruf im
  full-Modus). Ablehnungen wegen der Argumentform werden nicht auditiert.
- Instanzen werden über den exakten, ordinalen Namen (Groß-/Kleinschreibung beachtend) ausgewählt: Der
  Worker ruft `CreateInterface(name)` auf und lehnt die Schnittstelle mit `target_not_found` ab, sofern
  ihr `Name` nicht exakt der Anfrage entspricht. Jede Operation gibt die Schnittstelle danach frei
  (`Dispose`).
- Schreibzugriffe werden nicht zurückgerollt. `plcsim_write` hält beim ersten fehlgeschlagenen Item an;
  `write_tags` hält bei seiner ersten fehlgeschlagenen Variable an und gibt trotzdem zurück, welche
  Variablen geschrieben wurden.

## Operationen

Alle Items nehmen `operationId` und `operation`; `instance` ist bei jeder Operation außer `probe_api`
und `list_instances` erforderlich. Nicht anwendbare Member werden abgelehnt. Der Variablen-`dataType`
ist einer von `bool`, `int8`, `int16`, `int32`, `int64`, `uint8`, `uint16`, `uint32`, `uint64`, `float`,
`double`, `string` und wählt die typisierte Methode `Read*`/`Write*`; `string` ist nur lesbar, weil
`WriteString` kein verifiziertes Member ist, und die `uint*`-Typen sind nur lesbar, weil im lesbaren
Teil der Referenz kein Member `WriteUInt*` gefunden wurde. Eine Byte/USInt-Variable benötigt `uint8`
(live 2026-11-02: `ReadInt8` auf einem Byte schlug mit TypeMismatch -29 fehl). Variablennamen sind die Namen,
die die PLCSIM-Advanced-API adressiert (die Form, die `list_tags` liefert), ohne Anführungszeichen: `DB_Line.Speed`, nicht
`"DB_Line".Speed` (K56, live 2026-11-02: die Form mit Anführungszeichen scheiterte mit `DoesNotExist`). Beide werden
angenommen: Der Worker entfernt vor dem API-Aufruf die äußeren Anführungszeichen jedes durch Punkt getrennten Segments
(Array-Indizes bleiben); ein Segment, dessen Text in Anführungszeichen einen Punkt, ein Anführungszeichen oder eine Klammer
enthält, oder unausgeglichene Anführungszeichen werden unverändert weitergegeben. Ergebnisse nennen den Namen wie gesendet. Werte sind striktes
JSON: `true`/`false`, ganze Zahlen im Wertebereich, endliche Zahlen; nichts wird umgewandelt. Ein
`float`-Wert wird zuerst in einfache Genauigkeit umgewandelt und abgelehnt, wenn er überläuft (über
`±3.4028235E+38`) oder wenn ein Wert ungleich null auf null gerundet würde (unterhalb der kleinsten
denormalisierten Zahl); denormalisierte Zahlen und `-0` bleiben erhalten, und jeder `float`, den der
Server meldet, wird unverändert zurückgelesen.

| Operation | Tool | Felder | Ergebnis |
|---|---|---|---|
| `probe_api` | read | - | Ort der API (Quelle `environment`/`registry`/`none`, Versionsordner, DLL-Pfad, Existenz), Ladefehler, Assembly-/Datei-/Produktversion, `IsInitialized`/`IsRuntimeManagerAvailable`, `apiInitializationFailed` (in diesem Worker-Prozess ist ein Typinitialisierer fehlgeschlagen; der Host ersetzt dann den Worker), jedes Katalog-Member mit Kandidaten, aufgelöster Signatur oder dem Grund, warum es fehlt, den Operationen, die es verwenden, gefundene Typen (SInstanceInfo, STagInfo und Eigenschaftstypen mit ihren Membern; für jedes Enum seine Namen in `enumNames` und jeden Namen mit seinem numerischen Wert, als Dezimaltext, in `enumMembers`), das Fehlercode-Member von `SimulationRuntimeException` und `runtimeErrorCode.okMemberFound` (ob das geladene `ERuntimeErrorCode` ein Member mit genau dem Namen `OK` hat; null, wenn das Enum fehlt; was über den Erfolg entscheidet, siehe [Rückgabewerte](#rückgabewerte)), und die Verfügbarkeit pro Operation. Ist auch dann erfolgreich, wenn die API fehlt. |
| `list_instances` | read | - | Per Reflection erstellte Projektionen von `RegisteredInstanceInfo`, dazu die beiden Manager-Flags. |
| `get_instance` | read | `instance` | `name`, `id`, `operatingState`, `operatingMode`, `scaleFactor`, `communicationInterface`, `cpuType`, jeweils per Reflection gelesen; ein fehlendes oder fehlschlagendes Member ist null, mit einer Warnung. |
| `list_tags` | read | `instance`, `nameFilter?`, `max?` (1-5000, Standard 500) | Projektionen von `TagInfos` (ruft nie `UpdateTagList` auf), Anzahlen, `truncated`, `tagListStatus` (out-Werte von GetTagListStatus, oder null). `nameFilter` ist eine Teilzeichenkette ohne Beachtung der Groß-/Kleinschreibung und benötigt `STagInfo.Name`. Der Host behält so viele führende Variablen, wie in das Budget pro Wert passen (`budgetTrimmed`, Warnung). |
| `read_tags` | read | `instance`, `tags` 1-100 `{name, dataType}` | Pro Variable `status` `read`/`failed`, `value`, strukturierter `error`. |
| `power_on`, `power_off`, `run`, `stop`, `memory_reset` | write | `instance` | Aufgerufene Signatur, Timeout-Argument (60.000 ms, wenn die Überladung `(UInt32)` aufgelöst wird), Rückgabetyp, projizierter Rückgabewert, `returnCheck` und `returnCode` (siehe [Rückgabewerte](#rückgabewerte)), `operatingStateBefore`/`After`. Eine fehlgeschlagene Rückgabe lässt das Item mit `plcsim_runtime_error` fehlschlagen, mit diesem Ergebnis angehängt. `power_on` und `run` warnen, wenn die Instanz nicht am Softbus ist. |
| `update_tag_list` | write | `instance` | Wie oben (es ist keine Timeout-Überladung deklariert), dazu eine Warnung: Eine nicht verifizierte Sekundärquelle berichtet, dass `UpdateTagList()` die virtuelle CPU kalt neu starten kann. |
| `write_tags` | write | `instance`, `tags` 1-100 `{name, dataType, value}` | Pro Variable `written`/`failed`/`skipped`, der geschriebene typisierte Wert, aufgerufene Signatur und projizierter Rückgabewert. Ein Schreibzugriff, dessen Rückgabe einen Fehler signalisiert, lässt diese Variable mit `plcsim_runtime_error` fehlschlagen (Codename in `error.runtimeErrorCode`). Eine fehlgeschlagene Variable lässt das Item fehlschlagen (Kategorie des Variablenfehlers), mit dem typisierten Ergebnis angehängt. |
| `set_scale_factor` | write | `instance`, `value` > 0 | `requested`, `previous`, `current`. Ist das geladene `ScaleFactor` vom Typ `Single` (die Referenz deklariert `double`), ist ein Wert außerhalb des Bereichs einfacher Genauigkeit oder einer, der auf null gerundet würde, ein `validation_error`, und nichts wird gesetzt; der Host kann den geladenen Typ nicht kennen, daher ist diese Prüfung Sache des Workers. |

### Rückgabewerte

Steuer- und Schreibergebnisse melden den Rückgabewert der API. Laut der Siemens-Entwicklerreferenz für
API 8.0 gibt nur `PowerOn` `ERuntimeErrorCode` zurück; `PowerOff`, `Run`, `Stop`, `MemoryReset`,
`UpdateTagList` und jedes `WriteX` geben `void` zurück (ihre Fehler kommen als Exceptions an). Die
folgende Auswertung ist generisch, sodass eine Version, deren Methoden Codes zurückgeben, genauso
beurteilt wird:

- Das Enum `ERuntimeErrorCode` der geladenen API wird nach dem **Vorzeichen** des numerischen Werts
  beurteilt, so wie die Referenz die nativen Codes beschreibt ("Errors are represented by negative
  values, warnings by positive values"; bei Erfolg ist der Rückgabewert `SREC_OK`). Hat das geladene
  Enum ein Member mit genau dem Namen `OK`, ist dessen Wert - und nur dessen Wert - Erfolg
  (`returnCheck: "ok"`); null ist dann kein Erfolg, es sei denn, `OK` ist null. Nur wenn kein Member
  `OK` heißt, wird null als Erfolg angenommen, und das Ergebnis trägt dann eine Warnung, die das sagt.
  Andernfalls ist ein negativer Wert ein Fehler (`"failed"`, `plcsim_runtime_error` mit Nennung des
  Codes) und ein positiver Wert eine Warnung (`"warning"`: Der Aufruf war erfolgreich, spätere Items
  laufen weiter, und eine Warnung nennt den Code). Ein Wert, der weder der Erfolgswert noch
  vorzeichenbehaftet ist (jeder Nicht-Erfolgswert eines vorzeichenlosen Enums, oder null, während `OK`
  einen anderen Wert hat), ist `"uninterpretable"` und wird mit einer Warnung gemeldet. `returnCode` ist
  der aus dem geladenen Enum gelesene Member-Name oder die Zahl, wenn der Wert nicht definiert ist. Der
  .NET-Member-Name `OK` wird nur zur Laufzeit verifiziert (`probe_api` meldet `okMemberFound`, und
  `enumMembers` von `ERuntimeErrorCode` zeigt seinen numerischen Wert); der numerische Wert von
  `SREC_OK` war aus der Referenz nicht verfügbar;
- `Boolean`: `false` schlägt fehl (`returnCode: "False"`), `true` ist `"ok"`.

Jeder andere Rückgabetyp und `void` ist `"notInterpreted"` (ein Wert, der nicht void ist, wird mit
einer Warnung gemeldet). Ein Fehler aus einem Rückgabewert ist `plcsim_runtime_error`; der Aufruf wird
nicht zurückgerollt.

**Timeouts und Zustand danach.** `PowerOn`, `PowerOff`, `Run`, `Stop` und `MemoryReset` deklarieren
zuerst die Überladung `(UInt32 in_TimeOut_ms)` (60.000 ms; in der Entwicklerreferenz für API 8.0) und
fallen auf die parameterlose Überladung zurück. Dieser Rückfall kann zurückkehren, bevor die
Zustandsänderung abgeschlossen ist, sodass `operatingStateAfter` hinterherhinken kann; das Ergebnis
trägt dann `timeoutArgumentMs: null` und eine Warnung.

**Kommunikationsschnittstelle.** `power_on`, `run` und `plcsim_test` lesen
`IInstance.CommunicationInterface` und fügen eine Warnung mit dem Rohwert hinzu, wann immer dieser
nicht exakt der Name `Softbus` ist (oder nicht gelesen werden kann): Eine gestartete Instanz, die nicht
am PLCSIM-Softbus ist, kann über den virtuellen Ethernet-Adapter von PLCSIM mit realen Netzwerkgeräten
kommunizieren. Die Entwicklerreferenz listet das native Enum (`SRCI_NONE = 0`, `SRCI_SOFTBUS = 1`,
`SRCI_TCPIP = 2`), aber nicht die .NET-Member-Namen, sodass der Name `Softbus` nicht verifiziert ist
(mit `probe_api` bestätigen, das die Enum-Namen von `type of IInstance.CommunicationInterface` listet);
eine abweichende Schreibweise erzeugt nur die Warnung. Nichts wird blockiert.

### `plcsim_test`

Argumente: `instance`, `steps` (1-200), `continueOnFailure` (Standard false), `maxDurationMs`
(Standard 120.000, 1-600.000), `dryRun`.

| Schritt | Felder |
|---|---|
| `write` | `tag`, `dataType` (schreibbar), `value` |
| `wait` | `ms` 1-60.000 |
| `expect` | `tag`, `dataType`, `comparison` (`equals`, `notEquals`, `greaterThan`, `greaterOrEqual`, `lessThan`, `lessOrEqual`, `between`), `value` oder `min`+`max` (inklusive) für `between`, `timeoutMs` 0-60.000 (Standard 2.000), `pollMs` 10-60.000 (Standard 50) |

Jeder Schritt kann einen `name` tragen. `bool` und `string` erlauben nur `equals`/`notEquals`. Ganze
Zahlen werden exakt verglichen; `float`/`double` werden als IEEE-Doubles des geparsten typisierten
Werts verglichen (keine Toleranz - `between` verwenden). `expect` fragt ab, bis der Vergleich zutrifft
oder `timeoutMs` abgelaufen ist.

- Ein `write`-Schritt, dessen API-Rückgabe einen Fehler signalisiert (siehe
  [Rückgabewerte](#rückgabewerte)), endet in `error` mit einem `plcsim_runtime_error`, der den Code
  nennt; ein nicht interpretierbarer Code fügt eine Warnung hinzu.
- Ergebnisse pro Schritt: `status` `passed`/`failed`/`error`/`skipped`, `lastObserved`, `attempts`,
  `startOffsetMs`, `elapsedMs`, `message`, strukturierter `error`. Die zusammenfassenden Anzahlen
  enthalten die Expects. `result.passed` ist nur dann true, wenn jedes Expect bestanden wurde und kein
  Schritt fehlerhaft war oder übersprungen wurde; `success` auf oberster Ebene entspricht dem. Ein
  Szenario, das gelaufen ist, hat MCP `isError:false`, auch wenn es fehlgeschlagen ist.
- `continueOnFailure:false` überspringt jeden Schritt nach dem ersten Schritt mit `failed` oder
  `error`.
- `maxDurationMs` begrenzt das ganze Szenario; wird es erreicht, wird abgebrochen (`aborted`,
  `abortReason`, der aktuelle Schritt `error`, der Rest `skipped`). Es wird, mit einer Warnung, auf das
  Worker-Anfrage-Timeout minus 30.000 ms begrenzt (270.000 ms beim Standard von 5 Minuten); für längere
  Szenarien `--worker-timeout-minutes` erhöhen. Auch das eigene Anfrage-Timeout des MCP-Clients muss
  die Dauer zulassen.
- **Zeitmodell:** Wanduhrzeit gegen den Standard-Betriebsmodus von PLCSIM. Der Runner verwendet weder
  virtuelle Zeit noch synchronisiert er sich mit dem CPU-Zyklus (`OperatingMode`,
  `RunToNextSyncPoint`, `ScaleFactor` und Sync-Ereignisse werden vom Runner nicht verwendet), sodass
  Erwartungen tolerante Timeouts brauchen; ein `ScaleFactor` ungleich 1 verändert die simulierte Zeit
  relativ zu diesen Wanduhr-Timeouts.
- Passt das Ergebnis nicht in das Antwortbudget, werden zuerst Schrittmeldungen und Beobachtungen
  bestandener und übersprungener Schritte verworfen, dann lange Meldungen gekürzt; Status und Fehler
  bleiben erhalten. Passt selbst das nicht, gibt der Aufruf `result_metadata_too_large` mit der
  Zusammenfassung zurück.

### Antwort-Hüllen

`plcsim_read` verwendet die gemeinsame Batch-Lese-Hülle (`tool`, `contractVersion:"1.0"`, `success`,
`error`, `warnings`, `batch`). `plcsim_write` gibt `{tool, contractVersion, success, phase (preview|applied|error), dryRun, error, warnings, plan, batch}`
zurück; `plcsim_test` gibt `{tool, contractVersion, success, phase (preview|executed|error), dryRun, error, warnings, plan, result}`
zurück. Ablehnungen tragen `error` und MCP `isError:true`. Alle Dokumente sind kanonisches JSON mit
expliziten Nullwerten; Worker-Nutzdaten sind typisiert und werden mit dem Worker-Payload-Reader für
Pflicht-Member plus semantischen Prüfungen dekodiert, sodass eine fehlerhafte Nutzlast
`protocol_error` ist und nie zurückgegeben wird.

## Fehlerkategorien

| Kategorie | Bedeutung |
|---|---|
| `plcsim_api_unavailable` | Die API-DLL wurde nicht gefunden oder konnte nicht geladen werden, oder ein Typinitialisierer der API ist fehlgeschlagen (eine `TypeInitializationException` irgendwo in der Kette der inneren Exceptions, bis zu 32 Ebenen durchlaufen; zum Beispiel der statische Initialisierer von `SimulationRuntimeManager`, während der Runtime Manager nicht läuft); die Meldung nennt das Problem. Nur der Fall des Typinitialisierers setzt `workerRestartRequired`, worauf der Host den PLCSIM-Worker ersetzt. |
| `plcsim_api_member_missing` | Ein Member, das die Operation benötigt, wurde nicht zu einem deklarierten Kandidaten aufgelöst; die Meldung nennt das Member und die Kandidaten. |
| `plcsim_runtime_error` | Die API hat `SimulationRuntimeException` oder `SimulationInitializationException` ausgelöst (ohne fehlgeschlagenen Typinitialisierer); die Meldung enthält den Exception-Typ, `<member> = <ERuntimeErrorCode name> (<number>)`, wenn die Exception genau ein öffentliches Member vom Typ `ERuntimeErrorCode` bereitstellt, und die API-Meldung. Ebenso ein Steuer- oder Schreibzugriff, dessen Rückgabewert einen Fehler signalisiert (ein negativer `ERuntimeErrorCode` oder `false`), mit Nennung des Codes. `error`-Objekte auf Ergebnisebene tragen `exceptionType`, `runtimeErrorCode` und `runtimeErrorCodeMember`. |
| `target_not_found` | Die API hat eine Instanz zurückgegeben, deren Name vom angeforderten Namen abweicht. |
| `worker_operation_failed` | Jede andere Exception (Typ und Meldung) oder ein fehlender/nicht startbarer PLCSIM-Worker. |
| `worker_timeout`, `worker_crashed` | Transportfehler; bei Steueroperationen kann sich die simulierte CPU verändert haben. |
| `validation_error`, `access_denied`, `protocol_error`, `result_metadata_too_large` | Wie bei den anderen Tools. |

Meldungen werden nie geparst: Die Kategorie ergibt sich allein aus dem Exception-Typ.

## API-Auflösung

1. **Verzeichnis.** `TIA_MCP_PLCSIM_API_DIR` ist, wenn gesetzt und nicht leer, das Verzeichnis, das
   die DLL enthält, und schaltet die Registry-Suche ab. Es muss ein absoluter (vollqualifizierter)
   Pfad sein; ein relativer Wert, auch ein laufwerksrelatives `C:dir` oder ein wurzelrelatives `\dir`,
   wird mit einer Meldung, die den Wert nennt, abgelehnt, von `doctor` und vom Worker gleichermaßen
   (keine Suche, kein Laden). Ein `Path` in der Registry, der nicht absolut ist, wird ebenso abgelehnt.
   Andernfalls der Wert `Path` von
   `HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Siemens\Shared Tools\PLCSIMADV_SimRT` plus
   `API\<highest version subfolder>`; Ordnernamen werden als Versionen verglichen (`6.0`, `5.0` oder
   eine einfache Ganzzahl), andere Namen werden ignoriert, und es gibt keinen Rückfall auf eine
   niedrigere Version. `PlcSimApiLocator` (in `TiaMcpServer.Contracts`) wird von Worker und `doctor`
   gemeinsam genutzt.
2. **Laden.** `Assembly.LoadFile(<dll>)`, wie es das Siemens-Handbuch vorschreibt. Der
   `AssemblyResolve`-Handler des Workers beantwortet nur Bindungen, deren anfordernde Assembly zum
   API-Verzeichnis gehört (die API-DLL, eine Abhängigkeit, die der Handler bereits aufgelöst hat, oder
   eine andere Assembly, deren Datei in diesem Verzeichnis liegt), und nur mit einer Datei des
   angeforderten einfachen Namens in diesem Verzeichnis, deren Name außerdem zu angeforderter Version,
   Kultur und öffentlichem Schlüsseltoken passt, soweit die Anforderung sie angibt; eine Bindung ohne
   anfordernde Assembly und jede andere fehlgeschlagene Bindung im Prozess wird .NET überlassen.
   Ein fehlgeschlagenes Laden wird bei der nächsten Anfrage erneut versucht. Ein fehlgeschlagener
   Typinitialisierer kann im selben Prozess nicht erneut versucht werden; er ist
   `plcsim_api_unavailable`, und der Host startet den Worker neu.
3. **Bindung.** `PlcSimApiSurface` löst den einen `PlcSimApiCatalog` einmal auf, über den exakten
   ordinalen Namen und die exakten Parametertypen, anhand der deklarierten Kandidaten in Reihenfolge.
   Schnittstellen-Member werden auf `IInstance` und seinen Basisschnittstellen gesucht. Ein Kandidat
   "C#-Aufruf" `Name()` passt zuerst auf eine parameterlose Überladung, andernfalls auf eine Überladung,
   deren Parameter alle optional sind (aufgerufen mit den deklarierten Standardwerten, so wie
   `x.Name()` kompilieren würde). Mehrere passende Überladungen sind mehrdeutig und lassen das Member
   unaufgelöst. Außerhalb des Katalogs wird nichts nachgeschlagen. Eine Bindung, die eine Exception
   auslöst (zum Beispiel eine `TypeLoadException` für eine fehlende Abhängigkeit), wird wie ein
   fehlgeschlagenes Laden gemeldet (`probe_api` `loadError`, `plcsim_api_unavailable`, bei der nächsten
   Anfrage erneut versucht); nur ein fehlgeschlagener Typinitialisierer setzt das Neustart-Flag.
4. **Werte.** Enum-Werte werden mit Namen gemeldet; der Server wählt nie einen Enum-Wert. `SInstanceInfo`,
   `STagInfo`, Rückgabewerte und out-Parameter werden generisch projiziert: alle öffentlichen Felder und
   lesbaren Eigenschaften, primitive Werte, Enum- und String-Werte, verschachtelte Objekte bis Tiefe 2,
   eindimensionale Arrays solcher Objekte als JSON-Arrays dieser Objekte (jedes Element projiziert über
   die Member, die sein Laufzeittyp deklariert, in der eigenen Tiefe des Arrays, höchstens 64 Elemente,
   dann ein String `<moreElements:N>`; ein leeres Array ist `[]`, ein Null-Array `null`), sonst
   Markierungen (`<unsupported:Type>` für andere Arrays und Collections, `<depthLimit:Type>`,
   `<unreadable:Exception>`). `STagInfo.Dimension` ist ein solches Array (`SDimension[]`, angezeigt von
   `probe_api`); live am 2026-11-02 wurde es noch für jede Variable als `<unsupported:SDimension[]>`
   gemeldet (Defekt K17). Die Member-Namen von `SDimension` werden nirgends vorausgesetzt; die
   Array-Projektion ist nur mit Ersatztypen offline getestet und nicht live getestet. Ein Getter, der
   mit einem Typinitialisierer fehlschlägt, ergibt weiterhin `<unreadable:...>`, setzt aber zusätzlich
   das Neustart-Flag des Workers. `list_tags` filtert vor dem Projizieren nach Namen und projiziert nur
   die Einträge, die es zurückgibt; die lesbaren Member jedes Typs werden einmal pro Prozess ermittelt.
5. **Exceptions.** `TargetInvocationException` und `TypeInitializationException` werden vor der
   Kategorisierung entpackt; eine `TypeInitializationException` irgendwo in der Kette der inneren
   Exceptions (begrenzt auf 32 Ebenen) macht den Fehler zu `plcsim_api_unavailable` und den Worker
   neustartbedürftig. Rückgabewerte werden wie unter [Rückgabewerte](#rückgabewerte) beschrieben
   ausgewertet. Der Server ruft nie `SimulationRuntimeManager.Shutdown()` auf, dessen Wirkung auf den
   Runtime Manager nicht verifiziert ist.

## API-Oberfläche

Herkunft: **Handbuchname** - der Member-Name erscheint in den Übersichtstabellen des Siemens
Programmierhandbuchs "S7-PLCSIM Advanced API" V6.0 UPD1 (03/2024, A5E53246567-AB); die Parameterliste
war nicht zu beschaffen, sodass die Signatur nicht verifiziert ist und zur Laufzeit von `probe_api`
aufgelöst wird. **Handbuchsignatur** - Name und Parameterliste erscheinen beide dort.
**Referenzsignatur** - Name, Rückgabetyp und Parameterliste erscheinen auf der `IInstance`-Seite der
Siemens-Entwicklerreferenz für S7-PLCSIM Advanced, API 8.0 (`developer.siemens.com/s7-plcsim-advanced`,
C++/CLI-Notation, `DPRODUCT_VERSION 0x08000000`, DLL-Unterordner `API\8.0`); Signatur gegen die
Siemens-Entwicklerreferenz (API 8.0) verifiziert, während `probe_api` zur Laufzeit die Prüfung für die
installierte Version bleibt. **Sekundärhinweis** - nicht in diesen Quellen; einer nicht verifizierten
Sekundärquelle entnommen (ein MIT-lizenzierter PLCSIM-Unit-Test-Worker) und nur verwendet, wenn
vorhanden.

| Member | Art | Herkunft | Deklarierte Kandidaten (in Reihenfolge) | Verwendet von |
|---|---|---|---|---|
| `SimulationRuntimeManager.IsInitialized` | statische Eigenschaft | Handbuchname | `{ get; }` | probe_api, list_instances (optional) |
| `SimulationRuntimeManager.IsRuntimeManagerAvailable` | statische Eigenschaft | Handbuchname | `{ get; }` | probe_api, list_instances (optional) |
| `SimulationRuntimeManager.RegisteredInstanceInfo` | statische Eigenschaft | Handbuchname (Typ `SInstanceInfo[]`; Element-Member unbekannt) | `{ get; }` | list_instances |
| `SimulationRuntimeManager.CreateInterface` | statische Methode | Handbuchsignatur | `CreateInterface(String)` | jede Instanzoperation |
| `IInstance.Name` | Eigenschaft | Referenzsignatur (`String^ Name { get }`) | `{ get; }` | jede Instanzoperation (Prüfung des exakten Namens) |
| `IInstance.Dispose` | Methode | Referenzsignatur (IInstance erbt `System::IDisposable`) | `Dispose()` | jede Instanzoperation (optional; eine Warnung, wenn es fehlt) |
| `IInstance.ID` | Eigenschaft | Referenzsignatur (`INT32 ID { get }`) | `{ get; }` | get_instance (optional) |
| `IInstance.OperatingState` | Eigenschaft | Referenzsignatur (`EOperatingState { get }`) | `{ get; }` | get_instance, Steuerung vorher/nachher (optional) |
| `IInstance.OperatingMode` | Eigenschaft | Referenzsignatur (`EOperatingMode`, "Gets or sets") | `{ get; }` | get_instance (optional) |
| `IInstance.ScaleFactor` | Eigenschaft | Referenzsignatur (`double`, "Gets or sets"; der Setter wird zur Laufzeit aufgelöst) | `{ get; set; }` (Double oder Single) | set_scale_factor; get_instance (optional) |
| `IInstance.CommunicationInterface` | Eigenschaft | Referenzsignatur (`ECommunicationInterface { get }`; .NET-Wertname `Softbus` nicht verifiziert, nativ `SRCI_SOFTBUS = 1`) | `{ get; }` | get_instance (optional); Softbus-Warnung von power_on, run, plcsim_test |
| `IInstance.CPUType` | Eigenschaft | Referenzsignatur (`ECPUType { get }`) | `{ get; }` | get_instance (optional) |
| `IInstance.TagInfos` | Eigenschaft | Referenzsignatur (`array<STagInfo>^ { get }`) | `{ get; }` (Array oder `IEnumerable<T>`) | list_tags |
| `STagInfo.Name` | Datenmember des Elementtyps von TagInfos | Sekundärhinweis | `String`-Feld oder -Eigenschaft | nur list_tags `nameFilter` |
| `IInstance.GetTagListStatus` | Methode | Referenzsignatur (`void GetTagListStatus(ETagListDetails% out_TagListDetails, bool% out_IsHMIVisibleOnly)`) | `GetTagListStatus(ref ETagListDetails, ref Boolean)` | list_tags `tagListStatus` (optional) |
| `IInstance.PowerOn` | Methode | Referenzsignatur (`ERuntimeErrorCode PowerOn()` und `PowerOn(UINT32 in_TimeOut_ms)`) | `PowerOn(UInt32)` (60.000 ms), dann C#-Aufruf `PowerOn()` | power_on |
| `IInstance.PowerOff` | Methode | Referenzsignatur (`void PowerOff()` und `PowerOff(UINT32 in_TimeOut_ms)`) | `PowerOff(UInt32)` (60.000 ms), dann C#-Aufruf `PowerOff()` | power_off |
| `IInstance.Run` | Methode | Referenzsignatur (`void Run()` und `Run(UINT32 in_TimeOut_ms)`) | `Run(UInt32)` (60.000 ms), dann C#-Aufruf `Run()` | run |
| `IInstance.Stop` | Methode | Referenzsignatur (`void Stop()` und `Stop(UINT32 in_TimeOut_ms)`) | `Stop(UInt32)` (60.000 ms), dann C#-Aufruf `Stop()` | stop |
| `IInstance.MemoryReset` | Methode | Referenzsignatur (`void MemoryReset()` und `MemoryReset(UINT32 in_TimeOut_ms)`) | `MemoryReset(UInt32)` (60.000 ms), dann C#-Aufruf `MemoryReset()` | memory_reset |
| `IInstance.UpdateTagList` | Methode | Referenzsignatur (`void UpdateTagList()`, dazu Überladungen mit `ETagListDetails`, `bool in_IsHMIVisibleOnly`, `String^ in_DataBlockFilterList`) | C#-Aufruf `UpdateTagList()` (die parameterlose Überladung) | update_tag_list |
| `IInstance.ReadBool` / `ReadInt8` / `ReadInt16` / `ReadInt32` / `ReadInt64` / `ReadFloat` / `ReadDouble` / `ReadString` | Methoden | Referenzsignatur (`bool`/`INT8`/`INT16`/`INT32`/`INT64`/`float`/`double`/`String^` `ReadX(String^ in_Tag)`) | `ReadX(String)` mit Rückgabe `Boolean`/`SByte`/`Int16`/`Int32`/`Int64`/`Single`/`Double`/`String` | read_tags, plcsim_test expect |
| `IInstance.ReadUInt8` / `ReadUInt16` / `ReadUInt32` / `ReadUInt64` | Methoden | Siemens-Entwicklerreferenz, .NET-`IInstance` (`UINT8`/`UINT16`/`UINT32`/`UINT64 ReadUIntX(String^ in_Tag)`, geprüft 2026-11-02) | `ReadUIntX(String)` mit Rückgabe `Byte`/`UInt16`/`UInt32`/`UInt64` | read_tags, plcsim_test expect (nur lesbar: im lesbaren Teil der Referenz wurde kein Member `WriteUInt*` gefunden; K32: live schlug das Lesen eines Byte mit `ReadInt8` mit TypeMismatch -29 fehl) |
| `IInstance.WriteBool` / `WriteInt8` / `WriteInt16` / `WriteInt32` / `WriteInt64` / `WriteFloat` / `WriteDouble` | Methoden | Referenzsignatur (`void WriteX(String^ in_Tag, <type> in_Value)`) | `WriteX(String, <type>)` (jeder Rückgabetyp wird akzeptiert) | write_tags, plcsim_test write |
| `SimulationRuntimeException` | Typ | Handbuchname (Name der Fehlercode-Eigenschaft nicht verifiziert) | das einzige öffentliche Member vom Typ `ERuntimeErrorCode` | Fehlerzuordnung |
| `SimulationInitializationException` | Typ | Handbuchname | - | Fehlerzuordnung |
| `ERuntimeErrorCode` | Typ | Handbuchname (über die Exception); Referenz: negativ = Fehler, positiv = Warnung, Erfolg = `SREC_OK` (nativ; Wert nicht erfasst). Der .NET-Name `OK` wird nur zur Laufzeit verifiziert (`okMemberFound`) | Enum-Namen und numerische Werte, von probe_api gelistet (`enumMembers`) | Fehlerzuordnung; Rückgabewerte von Steuer- und Schreibzugriffen (laut Referenz gibt nur `PowerOn` ihn zurück) |

Die Member von `SimulationRuntimeManager`, `STagInfo.Name` und die Exception-Member wurden nicht gegen
die Entwicklerreferenz geprüft. Im Handbuch genannte, aber nicht verwendete Member: `Shutdown()`,
`RegisterInstance(...)`, `CreateInterface(Int32)`, `UnregisterInstance()`, `Info`, `Read()`, `Write()`,
die Familie `ReadUInt*` (nicht verifiziert), `RunToNextSyncPoint()`,
`IsSendSyncEventInDefaultModeEnabled`, `StoragePath` sowie die Setter von `OperatingMode` und
`CPUType`.

## Diagnose

`tia-mcp doctor` meldet eine Prüfung `plcsim-advanced`: die Quelle der Auflösung des API-Verzeichnisses,
den Registry-Wert und die Versionsordner, den DLL-Pfad und ob er existiert, sowie ob die ausführbare
Datei des PLCSIM-Workers vorhanden ist. Sie lädt nie die DLL und startet nie einen Worker. Ein Rechner
ohne PLCSIM Advanced besteht (die Komponente ist optional); ein gesetzter Override oder ein
Registry-Eintrag ohne die DLL, ein relatives `TIA_MCP_PLCSIM_API_DIR` oder ein relativer `Path` in der
Registry (abgelehnt, wie vom Worker) oder ein fehlender Worker ergibt eine Warnung.

## Außerhalb des Umfangs

Anlegen, Registrieren, Aufheben der Registrierung oder Konfigurieren von Instanzen; Downloads über
diese drei Tools (`plc_download` über die Schnittstelle `PLCSIM` verwenden); Tests mit virtueller Zeit
oder zyklussynchronisierte Tests; Ereignisse; Bereichszugriff (E/A/M/DB-Offset); Schreiben von UInt-
und String-Werten; S7-1200 (von PLCSIM Advanced nicht simuliert).

## Live-Beobachtungen (2026-11-02)

Live beobachtet am 2026-11-02 mit TIA Portal V21 und S7-PLCSIM Advanced 8.0, Instanz `MCP_Test`
(CPU 1516, Softbus), nach `plc_download` von `PLC_1` über die Schnittstelle `PLCSIM`. Dies sind
Beobachtungen, kein vollständiger Durchlauf der Abnahmeschritte unten.

- **Variablenliste nach einem Download (K26).** Direkt nach `plc_download` gab `list_tags` keine
  Variablen zurück, bis `plcsim_write update_tag_list` lief; danach wurden die Variablen gelistet. Die
  Warnung von `list_tags`, die auf `update_tag_list` verweist, erschien und ist die dokumentierte
  Abhilfe; der Server ruft `UpdateTagList()` nicht selbst auf (es soll die CPU kalt neu starten können,
  siehe `update_tag_list` oben).
- **Offsets in optimierten DBs (K26).** Für Member eines optimierten DB (symbolischer Zugriff) meldete
  PLCSIM für jedes Member `Offset` 0. Das ist der Wert, den PLCSIM Advanced zurückgibt, kein
  Projektionsfehler des Servers; `Offset` nicht zur Adressierung von Membern optimierter DBs verwenden.
- **`Dimension` (K17).** `list_tags` meldete für jede Variable `Dimension: "<unsupported:SDimension[]>"`;
  die Array-Projektion oben behebt das und braucht noch eine Live-Prüfung.
- `plcsim_write` `stop`/`run` und ein `plcsim_test`-Szenario (einen Sollwert von 42.5 schreiben und
  zurücklesen, `Taster_Start` → `Motor_Freigabe`) waren erfolgreich.

## Schritte der Live-Abnahme (sobald PLCSIM Advanced installiert ist)

1. S7-PLCSIM Advanced (V6.0 oder neuer) installieren und lizenzieren. `tia-mcp doctor --verbose`
   ausführen; die Nachweise von `plcsim-advanced` festhalten (Quelle, Versionsordner, DLL-Pfad,
   Worker-Pfad).
2. Im Control Panel den Online-Zugang auf **PLCSIM** stellen, eine S7-1500-Instanz anlegen (zum
   Beispiel `MCP_Test`) und ein kleines Testprogramm über die Schnittstelle `PLCSIM` laden
   (`plc_download` oder TIA Portal):
   eine Bool-Variable `Start`, eine Bool-Variable `Running`, die von einem 500-ms-TON nach `Start`
   gesetzt wird, sowie eine Int16-, eine Real-, eine LReal-, eine SInt-, eine DInt-, eine LInt- und
   eine String-Variable.
3. **Zuerst** `plcsim_read probe_api`. Das vollständige Dokument archivieren. Jedes erforderliche
   Member muss aufgelöst sein; jede aufgelöste Signatur festhalten, die Member von
   SInstanceInfo/STagInfo, die Enum-Namen und -Werte (`enumMembers`, insbesondere den Wert von
   `ERuntimeErrorCode.OK`) (einschließlich des Werts von `CommunicationInterface` für den Softbus), das
   Fehlercode-Member von `SimulationRuntimeException`, `okMemberFound` und die Datei-/Produktversion der
   DLL. Ist ein Member nicht aufgelöst, anhalten und die Kandidaten in `PlcSimApiCatalog` anhand dieser
   Nachweise korrigieren, bevor es weitergeht.
4. `list_instances`; `get_instance` mit dem exakten Namen und mit einer Variante in anderer
   Groß-/Kleinschreibung (`target_not_found` erwarten) und mit einem unbekannten Namen (den Code von
   `plcsim_runtime_error` festhalten).
5. `plcsim_write` mit `dryRun:true` für jede Operation (kein Worker-Aufruf; Audit `phase:"preview"`).
6. `stop`, `run`, `power_off`, `power_on`, `memory_reset`: `invokedSignature`, `timeoutArgumentMs`,
   `returnValue`, `returnCheck` und `operatingStateBefore/After` festhalten; im Control Panel
   bestätigen. Einen fehlschlagenden Aufruf provozieren (zum Beispiel `run` auf einer ausgeschalteten
   Instanz) und festhalten, ob er eine Exception auslöst oder einen Code zurückgibt, und einen
   Warncode von `PowerOn` (positiv) festhalten, falls einer auftritt.
7. `update_tag_list`: beobachten, ob die CPU kalt neu startet (Betriebszustand, remanente Werte); den
   Befund festhalten und die Warnung überarbeiten.
8. `list_tags` mit und ohne `nameFilter` (`tagListStatus` vor und nach Schritt 7 prüfen); `read_tags`
   für jeden Datentyp; `write_tags` für jeden schreibbaren Typ und die Werte zurücklesen; ein
   Schreibzugriff auf eine fehlende Variable (Nachweis für das Anhalten beim ersten Fehler).
9. `set_scale_factor` 0.5 und zurück auf 1.
10. `plcsim_test`: `Start` true, dann `expect Running equals true` mit `timeoutMs` 2000 (bestanden),
    ein Expect, das fehlschlagen muss, `continueOnFailure` in beiden Varianten und
    `maxDurationMs: 600000` beim Standard-Worker-Timeout (Begrenzungswarnung).
11. Modus read-only: Nur `plcsim_read` wird gelistet und funktioniert.
12. Audit-Datei: eine Zeile `recordKind:"plcsim"` pro Schreib-/Testaufruf mit der Instanz als Ziel.
13. Den PLCSIM Runtime Manager während einer Sitzung stoppen und die Fehler festhalten; einen neuen
    Server bei gestopptem Runtime Manager starten, `probe_api` ausführen (`apiInitializationFailed` nur
    erwarten, wenn der statische Initialisierer fehlschlägt), den Runtime Manager starten und
    bestätigen, dass der nächste Aufruf einen neuen Worker startet und erfolgreich ist.
14. Mit einer Instanz, deren Kommunikationsschnittstelle TCP/IP ist, bestätigen, dass die Warnung von
    `power_on`/`run` den Rohwert anzeigt.
