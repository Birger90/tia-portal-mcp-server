# PLC-Download (`plc_download`, `plc_read list_download_targets`)

**Sprache:** [English](PLC_DOWNLOAD_SUMMARY.md) · Deutsch

`plc_download` lädt die PLC des gebundenen Einzelplatzprojekts entweder über die PC-Schnittstelle
`PLCSIM` in eine **PLC-Simulation** oder über jede andere PC-Schnittstelle, die TIA Portal auflistet,
in eine **reale Steuerung**. Der Mensch steuert dies über den Zugriffsmodus, mit dem der Server
gestartet wird: `read-only` bietet keinen Download an, `read-write` fragt den Benutzer bei jedem
tatsächlichen Download einmal (MCP-Formular-Elicitation), und `full` lädt ohne Rückfragen. Es gibt
keine harte Sperre für Downloads auf Hardware; die einzigen Ablehnungen betreffen ungültige,
mehrdeutige oder nicht auflösbare Eingaben. Der Offline-Download-Pfad wird von der
`plc_read`-Operation `list_download_targets` aufgelistet.

> **Status: nicht live getestet.** Diese Funktion ist nur offline qualifiziert (Build gegen die
> reinen Compile-Referenz-Stubs, Unit-Tests, FakeWorker-Protokolltests). Sie lief nie gegen
> TIA Portal, PLCSIM oder eine reale CPU. Zum Testen werden TIA Portal V21 mit installiertem PLCSIM
> oder PLCSIM Advanced benötigt (und für Hardware eine entbehrliche CPU, die gestoppt werden darf).
> Die mechanischen Signaturprüfungen sind `scripts/verify-reference-stubs.ps1` (das auch die
> Referenz-Probe `reference-stubs/TiaMcpServer.OpennessReferenceProbe/DownloadReferenceSurface.cs`
> übersetzt) und ein Build von Worker und Probe gegen das installierte
> `C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48` unter Windows. Jedes unten als
> *angenommen* markierte Member muss diesen Compile gegen die echte DLL oder die erste
> Live-Prüfung bestehen, bevor man sich darauf verlässt. Die ersten Live-Prüfungen stehen unter
> [Live-Abnahmeprüfungen](#live-abnahmeprüfungen).

## Tool-Vertrag

### `plc_read`-Operation `list_download_targets`

Verfügbar in jedem Zugriffsmodus (`Observe`) auf einem verifizierten Einzelplatzprojekt. Optional
`plcName` (Software- oder Gerätename, ohne Beachtung der Groß-/Kleinschreibung); ohne Angabe wird
jede PLC gelesen. Das Ergebnis ist
`{ plcs: [{ plcName, deviceName, downloadable, modes: [{ name, pcInterfaces: [{ name, number,
targetInterfaces: [{ name }] }] }] }] }`. Gelesen wird nur `DownloadProvider.Configuration`
(Modi → PC-Schnittstellen → Zielschnittstellen) aus dem Offline-Projekt. Die Operation ruft nie
`GetAccessibleDevices()` (Live-Netzwerksuche) auf, wendet nie eine Konfiguration an und geht nie
online. Eine PLC, deren CPU-Geräteelement keinen `DownloadProvider` anbietet, meldet
`downloadable: false` und keine Modi. Eine Nutzlast, die sich nicht dekodieren lässt, ist
`protocol_error`.

### `plc_download`

Nur in read-write und full registriert (`OnlineControl`); fehlt in der Discovery von read-only und
wird in read-only von der Host- und der Worker-Autorisierung verweigert. Nur Einzelplatzprojekte:
Eine lokale Multiuser-Sitzung liefert `unsupported_capability`.

| Argument | Standard | Bedeutung |
| --- | --- | --- |
| `plcName` | keiner | PLC-Software- oder Gerätename. Optional (weggelassen oder explizit `null`), wenn das Projekt genau eine PLC hat; mehrere Treffer sind `target_ambiguous` mit den Kandidaten. |
| `pcInterface` | `{ name: "PLCSIM", number: 1 }` | Exakte PC-Schnittstelle aus `list_download_targets`. `PLCSIM` ist die Simulationsschnittstelle; **jeder andere Name ist reale Hardware**. Wenn angegeben, sind beide Member Pflicht, `number` muss eine JSON-Ganzzahl sein, unbekannte Member werden abgelehnt. Ein explizites `"pcInterface": null` ist ein Argumentformfehler (das Eingabeschema bewirbt ein Nicht-null-Objekt): Nur Weglassen wählt den Standard. Wird es weggelassen und bietet der Modus mehr als eine PC-Schnittstelle namens `PLCSIM` (unabhängig von ihren Nummern), ist der Aufruf `target_ambiguous`, bevor irgendetwas geladen wird. |
| `targetInterface` | keiner | Exakte Zielschnittstelle. Optional (weggelassen oder explizit `null`) nur, wenn die PC-Schnittstelle genau eine anbietet; andernfalls listet `target_ambiguous` die Kandidaten. |
| `mode` | `PN/IE` | Exakter Name des Verbindungsmodus. |
| `scope` | `hardware_and_software` | `hardware_and_software` → `DownloadOptions` `Hardware, Software`; `software` → `Software`; `software_only_changes` → `SoftwareOnlyChanges`. `Software` wird nie mit `SoftwareOnlyChanges` kombiniert; `None` wird nie verwendet. |
| `startAfterDownload` | `true` | Beantwortet die `StartModules`-Entscheidung nach dem Download mit `StartModule` (true) oder `NoAction` (false, die CPU bleibt in STOP). |
| `passwords`-Quellen | | Jeder Wert ist ein Umgebungsvariablen-NAME, `{ "env": "NAME" }` oder `{ "credential": "TiaMcp/..." }` (generische Anmeldeinformation der Windows-Anmeldeinformationsverwaltung); siehe [Passwortquellen](../guides/password-sources.de.md). Die Passwort-Map des Servers kann Einträge ergänzen, die der Aufruf weglässt. |
| `decisions` | keiner | Antworten des Aufrufers für Download-Konfigurationen, die die eingebauten Standardantworten überschreiben; siehe [Callback-Konfigurationen](#callback-konfigurationen-standardantworten-und-aufruferentscheidungen). Objekt: Schlüssel = Kurzname des Konfigurationstyps in `Siemens.Engineering.Download.Configurations` (exakt, Groß-/Kleinschreibung beachtet; der in den Nachweisen gemeldete `decisionKey`), Wert = der Name des Auswahl-Enum-Members (exakt, Groß-/Kleinschreibung beachtet) für eine Auswahlkonfiguration oder `true` für eine Prüfkonfiguration. Explizites `null` bedeutet weggelassen. |
| `passwords` | keiner | Passwortkonfigurationen, die aus Umgebungsvariablen beantwortet werden. Objekt: Schlüssel = Kurzname des Passwortkonfigurationstyps (`ModuleWriteAccessPassword`, `ModuleReadAccessPassword`, `BlockBindingPassword`, `PlcMasterSecretPassword` oder jede andere Passwortkonfiguration), Wert = der **Name** einer Umgebungsvariablen, die das Passwort enthält, nie das Passwort selbst. Explizites `null` bedeutet weggelassen. Siehe [Passwörter](#passwörter). |
| `portalConfirmations` | `decline` | TIA-Portal-`Confirmation`-Dialoge, die während des Downloads auftreten: `decline` beantwortet jeden mit Nein, protokolliert ihn und lässt den Aufruf fehlschlagen; `accept` beantwortet jeden mit Ja und protokolliert ihn. Siehe [Bestätigungsdialoge](#tia-portal-bestätigungsdialoge-während-eines-downloads). |
| `trustedCertificates` | keiner | SHA-256-Fingerabdrücke der TLS-Zertifikate des Geräts, die **der Benutzer** geprüft und freigegeben hat: ein JSON-Array von Strings mit je 64 Hexadezimalzeichen (Groß-/Kleinschreibung egal; Doppelpunkte und Leerzeichen werden ignoriert), höchstens 16, keine Wiederholungen nach der Normalisierung. Der einzige Weg, auf dem die TLS-Zertifikatsprüfung der Verbindung des Downloads (`TlsVerificationConfiguration`, Online-Legitimation) mit `Trusted` beantwortet wird; weggelassen, explizit `null` oder leer vertraut nichts. Siehe [TLS-Zertifikatsvertrauen](#tls-zertifikatsvertrauen-online-legitimation). Nicht live getestet. |
| `rhTarget` | keiner | Nur R/H-System (Issue #16, nicht live getestet): `primary` oder `backup` lädt über `RHDownloadProvider.DownloadToPrimary`/`DownloadToBackup(IConfiguration, DownloadConfigurationDelegate, DownloadConfigurationDelegate, DownloadOptions)` in diese CPU; der Provider wird unter den `GetServiceInfos()` des CPU-Device-Items über den exakten vollen Namen gefunden und zur Laufzeit gebunden, der Pfad auf `RHDownloadProvider.Configuration` neu aufgelöst. Dieselben Entscheidungen, Passwörter, Guards und Bestätigung wie beim normalen Download; Prompt und `effects.rhTarget` nennen die CPU. Eine PLC ohne den Provider scheitert mit `unsupported_capability`, bevor etwas geladen wird. Weggelassen: `DownloadProvider.Download`. |
| `allowInsecurePasswordTransfer` | `false` | Audit T1 (nicht live getestet): `false` sendet ein Passwort nur, wenn die Passwortkonfiguration `IsSecureCommunication` true meldet; false oder nicht lesbar bricht mit `insecure_communication` ab, und nichts wird gesendet. `true` sendet es trotzdem über die nicht sichere (Legacy-)Verbindung, wie TIA Portal es erlaubt; nur setzen, nachdem der Benutzer zugestimmt hat. Die Rückfrage nennt die geltende Regel; `effects.allowInsecurePasswordTransfer` ist nur dann `true`, wenn gesetzt. Siehe [Passwörter](#passwörter). |
| `dryRun` | `false` | PLC, Pfad und Guards auflösen und `phase: preview` zurückgeben, ohne das Ziel zu berühren oder nachzufragen. |

Argumentformfehler (unbekannte Root- oder verschachtelte Member, nulls für nicht nullbare Werte, ein
explizites `pcInterface: null`, falsche Typen, ein `decisions`-Wert, der weder String noch `true`
ist, ein `passwords`-Wert, der kein String ist, ein doppelter Schlüssel in einem der beiden Objekte,
verschachtelte Werte, ein `trustedCertificates`, das kein Array ist oder ein Nicht-String- oder
`null`-Element enthält) sind das kanonische `validation_error`-Dokument ohne Audit-Eintrag. Ein
explizites `null` für `plcName`, `targetInterface`, `decisions`, `passwords` oder
`trustedCertificates` bedeutet weggelassen, wie bei den optionalen Selektoren der anderen Tools
(zum Beispiel `plcName` von `compile_check`); ein `null` *innerhalb* eines angegebenen `pcInterface`
(`{ name: null, number: 1 }`) ist ein `validation_error` nach dem Eintritt in die Pipeline. Ablehnungen nach dem Eintritt in die Pipeline
(fachliche Validierung, Bindung, Planung) sind kanonische Root-Fehler mit einem Audit-Eintrag.

Validierung nach dem Eintritt in die Pipeline von `decisions` und `passwords` (mit dem Worker geteilt als
`PlcDownloadPolicy.ValidateDecisions`): Schlüssel sind Bezeichner (Buchstaben, Ziffern, Unterstriche,
höchstens 256 Zeichen) und über beide Objekte hinweg eindeutig; ein vollständiger Typname wird
abgelehnt, unter Nennung des zu verwendenden Kurznamens; höchstens 64 Einträge je Objekt; eine
Auswahl ist ein nicht leerer Bezeichner; `StartModules` wird abgelehnt (`startAfterDownload`
verwenden); `TlsVerificationConfiguration` wird in beiden Objekten abgelehnt (einem
Gerätezertifikat wird nur über `trustedCertificates` vertraut, nie über eine Entscheidung oder ein
Passwort); ein Passwortwert ist ein Umgebungsvariablenname (ASCII-Buchstaben, Ziffern und
Unterstriche, nicht mit einer Ziffer beginnend). **Unbekannte Schlüssel sind erlaubt**:
Konfigurationstypen können erst zur Laufzeit auftauchen. Ein ungültiger `passwords`-Wert wird nie
zurückgegeben: Er wird beim Binden der Argumente durch einen festen Platzhalter ersetzt und erreicht
daher weder die Antwort noch das Audit. Ein gültiger Variablenname, der in der Umgebung des Servers
nicht gesetzt (oder leer) ist, kann ebenfalls ein eingefügtes Passwort sein: Er wird beim Binden
durch einen zweiten Platzhalter ersetzt und abgelehnt, bevor irgendetwas aufgelistet oder geladen
wird, wobei nur die Konfiguration genannt wird (`passwords.<type>: the environment variable named for this
configuration is not set`). Ein Variablenname erreicht die Antwort und das Audit nur, wenn er gesetzt
ist. `trustedCertificates`-Einträge, die keine Fingerabdrücke sind (`trustedCertificates[i] is not a SHA-256
fingerprint`), Wiederholungen nach der Normalisierung (`trustedCertificates[i] repeats trustedCertificates[j]`)
und mehr als 16 Einträge sind `validation_error`s nach dem Eintritt in die Pipeline (`PlcTlsTrustPolicy.NormalizeList`,
Contracts).

Die Auflösung ist exakt und ohne Fallback: Jede Ebene vergleicht ordinale Namen (und die Nummer der
PC-Schnittstelle); eine fehlende Ebene ist `target_not_found` mit den konfigurierten Kandidaten, ein
Duplikat oder eine nicht gewählte Zielschnittstelle bei mehreren Zielen ist `target_ambiguous`. Eine
Standardnummer wird nie verwendet, um eine von mehreren gleichnamigen PC-Schnittstellen auszuwählen
(`PlcDownloadPolicy.IsImplicitInterfaceNumberAmbiguous`, das für jeden Schnittstellennamen gilt; da
ein angegebenes `pcInterface` immer seine Nummer mitführt, kann heute nur der weggelassene
`PLCSIM`-Standard diesen Fall erreichen). Die Planung verwendet nur `list_download_targets`. Der
Worker löst anschließend genau dieselben Namen auf **derselben `DownloadProvider`-Instanz** erneut
auf, die `Download(...)` ausführt, wiederholt die Prüfung auf gleiche Namen (die Anfrage führt mit,
ob der Aufrufer die Nummer angegeben hat), verlangt genau einen Treffer pro Ebene und schlägt mit
`state_changed` oder `target_ambiguous` fehl (nichts geladen), wenn sich der Pfad geändert hat. Er
weicht nie auf eine andere Schnittstelle oder ein anderes Ziel aus.

### Was die `PLCSIM`-Namensprüfung beweist und was nicht

Der Guard `download_to_hardware`, der Effekt `simulationTarget` und die Standardantwort für
`TargetForSoftware` vergleichen den **Namen** des gewählten PC-Schnittstelleneintrags der
Offline-Download-Konfiguration von TIA Portal (`ConfigurationPcInterface.Name`, ordinal `PLCSIM`).
Das beweist nur, dass TIA Portal einen PC-Schnittstelleneintrag mit genau diesem Namen aufgelistet
hat. Es beweist **nicht**:

- dass der Eintrag die Simulationsschnittstelle von TIA Portal ist und nicht ein anderer Adapter mit
  demselben Namen (dass PC-Schnittstellennamen von TIA Portal vergeben werden und nicht vom Benutzer
  bearbeitbar sind, ist angenommen, nicht verifiziert);
- dass ein Simulator (S7-PLCSIM oder S7-PLCSIM Advanced) läuft oder welche Instanz an der
  Zielschnittstelle antwortet; online wird nichts ermittelt (`GetAccessibleDevices()` wird nie
  aufgerufen);
- dass die erreichte Instanz nicht selbst mit realen Geräten kommunizieren kann: Eine S7-PLCSIM
  Advanced-Instanz, deren Kommunikationsschnittstelle nicht der Softbus ist, wird über einen
  (virtuellen) Ethernet-Adapter statt über diese Schnittstelle erreicht, und `plcsim_write`
  `power_on`/`run` sowie `plcsim_test` warnen, wenn eine Instanz nicht am Softbus hängt (siehe die
  [PLCSIM-Referenz](PLCSIM_OPERATIONS_SUMMARY.de.md));
- umgekehrt, dass eine Nicht-`PLCSIM`-Schnittstelle eine reale Steuerung erreicht (sie kann eine
  PLCSIM Advanced-Instanz an einem virtuellen Adapter erreichen); der Hardware-Guard ist konservativ.

Antwort (Vertrag `2.0`): `{ tool, contractVersion, success, error, warnings, phase, guards,
effects, result }`. `effects` enthält die aufgelöste PLC, das Gerät, `path { mode, pcInterface { name,
number }, targetInterface }`, ob die Zielschnittstelle automatisch gewählt wurde, den Umfang
(scope), die `DownloadOptions`-Namen, `startAfterDownload`, `simulationTarget` (die PC-Schnittstelle
ist genau `PLCSIM`), die `decisions` des Aufrufers (`[{ type, selection, check }]`, ordinal nach
Typ), `passwords` (`[{ type, variable }]`, nur Variablen**namen**), `portalConfirmations` und
`trustedCertificates` (die normalisierten Fingerabdrücke, in Großbuchstaben ohne Trennzeichen, in
der Reihenfolge des Aufrufers; leer, wenn keine). `result` ist ein
`{ status, value, failure, omission }`-Ergebnis, dessen `value` die typisierte Projektion des
Downloads ist:

- `state` (`DownloadResultState`-Name), `errorCount`, `warningCount` — null, wenn `Download`
  kein Ergebnis zurückgab (abgebrochen oder fehlgeschlagen);
- `messages` — rekursiv `{ message, state, messages[] }`, höchstens 8 Ebenen und 500 Meldungen,
  Text bei 1.000 Zeichen abgeschnitten, mit `messagesComplete` und `messageOmission`;
- `configurationsSeen` — jede Callback-Konfiguration `{ phase, type, canonicalType, decisionKey,
  kind, message, availableSelections, decision, selection, source, passwordVariable }` (plus `tls`
  für die TLS-Zertifikatsprüfung), beantwortet oder nicht. `phase` ist `pre_download`,
  `post_download` oder `online_legitimation` (über die `OnlineLegitimation` der Verbindung
  vorgelegt; `message` ist dort immer null). `type` ist der Laufzeittypname; `canonicalType` der
  öffentliche Typ aus `Siemens.Engineering.Download.Configurations` (Online-Legitimation: Typ aus
  `Siemens.Engineering.Online.Configurations`), der er exakt ist (oder null); `decisionKey` dessen
  Kurzname, der Schlüssel für `decisions`/`passwords`; `kind` ist `selection`, `check`, `password`
  oder `other` (was das Live-Objekt anbietet); `availableSelections` die Live-Enum-Member-Namen von
  `CurrentSelection` einer Auswahlkonfiguration (höchstens 64; sonst leer); `decision` ist
  `selected`, `checked`, `password_set` oder `aborted`; `selection` das gesetzte Member; `source`
  `default`, `caller` oder `policy` (eine mit `NonTrusted` abgelehnte TLS-Frage, K27);
  `passwordVariable` der Umgebungsvariablen**name**, aus dem ein Passwort stammt;
- `abort` — `{ phase, type, canonicalType, decisionKey, kind, message, availableSelections,
  reason, detail }` (plus `tls`, wenn es die TLS-Zertifikatsprüfung ist) der Konfiguration, die den
  Download gestoppt hat, oder null (siehe [Abbruchgründe](#abbruchgründe));
- `portalConfirmationPolicy` (`decline` oder `accept`), `portalConfirmations` — die
  TIA-Portal-`Confirmation`-Dialoge, die während des Downloads auftraten `{ type, text, answer }`,
  `answer` `No` bei decline und `Yes` bei accept (leer, wenn keine; die ersten 100 werden
  aufgelistet, Text bei 1.000 Zeichen abgeschnitten; `text` ist null, sofern die Ereignisargumente
  keinen lesbaren öffentlichen `Text`-String anbieten, ein nicht verifiziertes Member);
  `portalConfirmationsObserved` ist die Gesamtzahl der beantworteten Dialoge, und
  `portalConfirmationsComplete` ist false, wenn mehr beantwortet als aufgelistet wurden;
- `plcName`, `deviceName`, `path`, `scope`, `downloadOptions`, `startAfterDownload`;
- `completionVerified: false` und `completionNote`. Siemens dokumentiert, dass `Download()` in V21
  zurückkehren kann, bevor das Gerät die Verarbeitung des Downloads abgeschlossen hat; das Tool
  behauptet nie, dass die PLC läuft. Eine Worker-Nutzlast, die `completionVerified: true` behauptet,
  wird als `protocol_error` abgelehnt.

Root-`success` ist nur dann true, wenn der Download zurückkehrte, `state` nicht `Error` ist,
`errorCount` 0 ist, keine Konfiguration abgebrochen und keine TIA-Portal-Bestätigung abgelehnt wurde
(angenommene Bestätigungen lassen den Aufruf nicht fehlschlagen; sie fügen eine Warnung hinzu). Ein
versuchter Download, der fehlschlug, ist `phase: applied`, `success: false`, `error: null`,
`isError: false`, mit den Nachweisen in `result.value` und dem Fehler in `result.failure`; vor einem
erneuten Versuch PLC oder Simulator prüfen. Es gibt kein Rollback und keine Wiederholung. Überschreitet
der Wert das konfigurierte Wertbudget, werden zuerst die Download-Meldungen verworfen (Zustand,
Zähler, Konfigurationen und Abbrucheintrag bleiben erhalten); erst danach wird der ganze Wert
weggelassen.

## Sicherheitsmodell

- **Der Mensch steuert die KI über den Zugriffsmodus.** `read-only`: kein Download (das Tool ist
  nicht registriert, und Host und Worker verweigern `plc_download`). `read-write`: Jeder tatsächliche
  Download fragt den Benutzer einmal. `full`: keine Rückfragen. Nichts anderes sperrt einen Download
  auf Hardware.
- **Capability.** Die Worker-Operation `plc_download` ist `OperationCapability.OnlineControl` und
  nur in read-write und full erlaubt. `list_download_targets` ist `Observe`. Beide verlangen die
  exakte verifizierte Sitzungsidentität.
- **Pipeline.** `PlcDownloadWriteDomain` läuft durch `WriteExecution` als ein einzelner geschützter
  Schreibvorgang mit einem Element: Validierung, verifizierte Bindung, festgehaltene Lease, Planung,
  Guards, Halt beim Dry Run, Bestätigung, erneute Planung nach der Rückfrage, Mutation,
  Zusammenstellung, ein Audit-v2-Eintrag pro Aufruf (jede Phase).
- **Bestätigung.** `ConfirmsEveryCall = true`, wie beim Lifecycle: Jeder tatsächliche
  read-write-Aufruf fragt den Benutzer per Formular-Elicitation (`accept` mit `confirm: true`);
  Ablehnung, Abbruch, Zeitüberschreitung, Transportfehler oder ein Client ohne Elicitation
  verweigern mit `access_denied`, bevor irgendetwas geladen wird. Die Rückfrage beginnt (nach den
  Zeilen der Acknowledge-Guards) mit der Art des Ziels, bei einer Hardware-Schnittstelle
  **"Confirm plc_download to a REAL controller (NOT the PLCSIM simulation interface; the
  machine it controls may stop or move)"**, und damit, ob die CPU neu gestartet wird; danach nennt sie
  PLC, Gerät und exakten Pfad (in Anführungszeichen und maskiert, wie jeder Name in einer
  Bestätigungsrückfrage), listet die eingebauten Antworten (mit der wirksamen Antwort für
  `TargetForSoftware`), jede Entscheidung des Aufrufers, jeden Passwort-Variablennamen, den TLS-Satz
  (ohne `trustedCertificates`: "A TLS certificate check of the device (TlsVerificationConfiguration) aborts the
  download: no certificate is trusted"; mit ihnen: jeden normalisierten Fingerabdruck und dass eine
  Prüfung nur dann mit `Trusted` beantwortet wird, wenn jedes vorgelegte Zertifikat aufgelistet ist,
  andernfalls `NonTrusted` und Abbruch) und wie TIA-Portal-Bestätigungen beantwortet werden. Nach der
  Annahme wird der Pfad neu geplant und muss unverändert sein, sonst endet der Aufruf als
  `binding_conflict`, ohne zu laden. Full läuft unter Policy ohne Server-Elicitation. Dry Runs fragen
  nie nach.
- **Guards** (geschlossener Produktionskatalog; drei Acknowledge-Guards und ein Block-Guard, beide für die F-Programm-Konfiguration hinzugefügt):

  | Guard | Schweregrad | Löst aus, wenn |
  | --- | --- | --- |
  | `download_to_hardware` | acknowledge | Der aufgelöste PC-Schnittstellenname ist nicht genau `PLCSIM` (ordinal): Der Download geht an eine reale Steuerung, und die Maschine, die sie steuert, kann anhalten oder sich bewegen; `TargetForSoftware` wird mit `CPU` beantwortet, sofern der Aufrufer nichts anderes entscheidet. Erfüllt durch die read-write-Rückfrage oder die Policy des full-Modus. |
  | `download_stops_plc` | acknowledge | Jeder Download: Die CPU kann gestoppt werden (StopModules wird mit `StopAll` beantwortet, sofern der Aufrufer nichts anderes entscheidet) und wird nur neu gestartet, wenn `startAfterDownload` true ist. Erfüllt durch die read-write-Rückfrage oder die Policy des full-Modus. |
  | `download_includes_safety_program` | acknowledge | `decisions` beantwortet `SafetyProgram` (die F-Programm-Konfiguration), auf PLCSIM oder einer realen Steuerung (die zusätzlich `download_to_hardware` auslöst); die Meldung enthält den Hinweis zur Sicherheitsabnahme ([PLC-Safety-Referenz](PLC_SAFETY_SUMMARY.de.md)). Es gibt keinen Block-Guard: Ein F-Programm-Download auf eine F-CPU wird wie jeder Hardware-Download bestätigt (Entscheidung des Eigentümers 2026-11-01). |

  Die Acknowledge-Guards sind nur zulässig, weil diese Domäne jeden Aufruf bestätigt; ihre
  Definitionen werden wie die des Lifecycle registriert, nicht über
  `WriteGuardCatalog.NonInteractive` (das den Schweregrad acknowledge ablehnt). Ein Dry Run meldet
  in read-write beide mit `acknowledged: false`.
- **Gestaffelte Abwehr im Worker.** Der Worker lehnt vor dem Aufruf von `Download` ab: eine leere
  PLC/ein leeres Gerät/einen leeren Modus/eine leere Schnittstelle/ein leeres Ziel, einen
  unbekannten Umfang oder eine unbekannte Portal-Policy, fehlerhafte Entscheidungen oder Passwörter
  (alles über das Siemens-freie, unit-getestete `PlcDownloadPolicy.ValidateWorkerRequest`, das auch
  der FakeWorker verwendet), mehrere gleichnamige PC-Schnittstellen ohne explizite Nummer oder einen
  Pfad, der sich nicht mehr exakt auflösen lässt. Er verlangt die Worker-interne
  Bestätigungsschranke. Er prüft nicht mehr auf `PLCSIM`. `Download` läuft nie innerhalb einer
  `Transaction` oder von `ExclusiveAccess`.
- **Fehler.** Eine Siemens-`EngineeringException` aus `Download` wird von
  `WorkerExecution.Classify` mit gesetztem Mutationskennzeichen klassifiziert (`OnlineControl` zählt
  als mutierend): `EngineeringTargetInvocationException` → `engineering_rejected`, andere
  Engineering-Fehler → `worker_operation_failed`, beide mit `engineeringError`.
  `NonRecoverableException` oder ein beobachtetes Disposal des Portals → `portal_lost`, das Vorrang
  hat und die Bindung ungültig macht. Eine `portal_lost`-Fehlermeldung eines Downloads sagt außerdem,
  dass TIA Portal beendet worden sein kann (K27: nach einem solchen Fehler am 2026-11-02 war kein
  TIA-Portal-Prozess mehr vorhanden), dass dies mit `bind_project`-Aktion `list_portals` zu prüfen
  ist und dass TIA Portal zu starten, das Projekt erneut zu öffnen (`open_project`) und erneut zu
  binden ist; nichts wird automatisch neu gestartet oder erneut geöffnet. Jeder Fehler nach dem
  Aufruf von `Download` führt die typisierten Nachweise in `WorkerResponse.PlcDownloadOutcome` mit
  (der Nutzlasttext wird hostseitig vom strikten Pflicht-Member-Leser dekodiert; fehlerhafte
  Nachweise sind `protocol_error` und werden nicht zurückgegeben).

## Callback-Konfigurationen: Standardantworten und Aufruferentscheidungen

Beide Delegates und der Online-Legitimation-Handler der Verbindung arbeiten fail-closed und
durchlaufen dieselbe Siemens-freie Entscheidungsschleife, `PlcDownloadCallbackRecorder` (Contracts),
die sich Worker und FakeWorker teilen und die ohne Siemens-Assemblies unit-getestet ist. Die
Antworten stammen aus `PlcDownloadDecisionSet`; die Live-Objekte werden von
`PlcDownloadReflectionAccess` gelesen und beantwortet (Contracts, Siemens-frei; der Worker injiziert
nur das kompilierte Lesen von `DownloadConfiguration.Message` und `Environment.GetEnvironmentVariable`).

- **Exakter Typabgleich.** Der kanonische Typ ist der erste *öffentliche* Typ in der
  Laufzeit-Typkette der Konfiguration (`GetType()`, dann `BaseType`), dessen Namespace
  `Siemens.Engineering.Download.Configurations` ist (verschachtelte Typen passen nie). Sein Kurzname
  ist der `decisionKey`. Die einzige Ausnahme ist der exakte öffentliche Typ `Siemens.Engineering.Safety.Download.Configurations.SafetyProgram`
  (Baseline der installierten V21-API), dessen Schlüssel `SafetyProgram` ist und der keine
  Standardantwort hat. Nicht öffentliche Implementierungstypen oder solche aus anderen Namespaces
  werden übersprungen; eine öffentliche Unterklasse eines Typs mit Standardantwort in diesem Namespace
  ist ein eigener Typ (eine `is`-Prüfung hätte ihn getroffen) und braucht eine eigene Entscheidung.
  Dass die Typkette der Live-Objekte den öffentlichen V21-Typ enthält, ist angenommen — live
  verifizieren. Eine Konfiguration ohne einen solchen Typ hat keinen Schlüssel und bricht ab
  (`unsupported_configuration`).
- **Reihenfolge der Antworten.** Ein `passwords`-Eintrag für den Schlüssel, sonst ein
  `decisions`-Eintrag für den Schlüssel (in jeder Phase), sonst die eingebaute Standardantwort der
  Phase, sonst Abbruch (`decision_required`). Entscheidungen des Aufrufers überschreiben die
  Standardantworten. Die TLS-Zertifikatsprüfung der Phase `online_legitimation` folgt nie dieser
  Reihenfolge (siehe [TLS-Zertifikatsvertrauen](#tls-zertifikatsvertrauen-online-legitimation)).
- **Online-Legitimation.** Die `OnlineLegitimation`-Konfigurationen der Verbindung sind eine dritte
  Phase derselben Schleife (`online_legitimation`; kanonische Typen aus
  `Siemens.Engineering.Online.Configurations`).
- **Anwenden einer Antwort, generisch, per Reflection auf dem Laufzeitobjekt** (nie per kompiliertem Cast):
  - ein Auswahlname: Das Objekt muss genau eine öffentliche, les- und schreibbare Enum-Eigenschaft
    `CurrentSelection` anbieten; der Name wird gegen **den eigenen Enum-Typ dieser Eigenschaft** über
    den exakten Member-Namen mit Beachtung der Groß-/Kleinschreibung geparst (`Enum.IsDefined`; Zahlen
    und Flag-Kombinationen werden abgelehnt) und gesetzt;
  - `true`: Das Objekt muss genau eine öffentliche, les- und schreibbare `bool`-Eigenschaft
    `Checked` anbieten, die auf true gesetzt wird (`DownloadCheckConfiguration.Checked`);
  - ein Passwort: siehe [Passwörter](#passwörter).
  Ein fehlendes oder mehrdeutiges Member schlägt fail-closed fehl (`decision_not_applicable` mit
  einem `detail`); ein unbekannter Auswahlname schlägt fail-closed fehl (`selection_unavailable`, der
  Abbruch listet `availableSelections`); eine Ausnahme aus dem Setter oder aus `SetPassword` wird aus
  der Reflection ausgepackt und als `decision_rejected` protokolliert, wobei der Engineering-Fehler
  des Setters (`engineeringError`) gemeldet wird, wenn es einer war; eine `NonRecoverableException`
  oder eine Ausnahme wegen eines verworfenen (disposed) Objekts wird nach dem Protokollieren
  unverändert erneut geworfen, sodass die gemeinsame Klassifizierung weiter gilt (`portal_lost` bei
  einem verlorenen Portal), mit angehängten Nachweisen.
- **Nachweise.** Jede Konfiguration wird mit ihrem `kind` (Reflection: `SetPassword(SecureString)`
  → `password`, `bool Checked` → `check`, Enum `CurrentSelection` → `selection`, sonst `other`), ihren
  Live-`availableSelections` und jede angewendete Antwort mit ihrer `source` protokolliert (`default` | `caller` | `policy`, Letzteres nur für eine mit `NonTrusted` beantwortete TLS-Frage).
- **Nach einem Abbruch** wird jeder spätere Callback als abgebrochen mit dem Grund des ersten
  Abbruchs protokolliert und wirft erneut; es wird nichts mehr angewendet (eine spätere TLS-Frage ist
  die Ausnahme: `NonTrusted` ohne Werfen, siehe [TLS-Zertifikatsvertrauen](#tls-zertifikatsvertrauen-online-legitimation)).
  **Risiko (K27):** Ein Werfen aus dem Online-Legitimation-Callback von TIA Portal wurde live
  (2026-11-02) unmittelbar vor der Beendigung von TIA Portal beobachtet; ob ein Werfen aus den
  Delegates vor und nach dem Download (eine unbeantwortete Auswahl-, Prüf- oder
  Passwortkonfiguration) TIA Portal ebenfalls beenden kann, ist durch keine Live-Nachweise belegt. Es
  gibt keinen dokumentierten Weg, diese Konfigurationen ohne Werfen abzubrechen, daher bleibt ihr
  Verhalten unverändert; jede Konfiguration, die der Download vorlegt, beantworten (`decisions`,
  `passwords`), um das Werfen zu vermeiden. Der erste Abbruch bleibt in `abort`. Ein Download, der
  mehr als 100 Konfigurationen vorlegt, wird abgebrochen (`configuration_limit_exceeded`; höchstens
  101 Einträge werden protokolliert).

Eingebaute Standardantworten (unveränderte Allowlist, jetzt überschreibbar):

| Phase | Konfigurationstyp | Standardantwort |
| --- | --- | --- |
| vor dem Download | `StopModules` | `StopAll` |
| vor dem Download | `ConsistentBlocksDownload` | `ConsistentDownload` |
| vor dem Download | `AlarmTextLibrariesDownload` | `ConsistentDownload` |
| vor dem Download | `OverwriteSystemData` | `Overwrite` |
| vor dem Download | `TargetForSoftware` | `PlcSimulationAdvanced`, wenn die PC-Schnittstelle genau `PLCSIM` ist, sonst `CPU` |
| nach dem Download | `StartModules` | `StartModule` bei `startAfterDownload`, sonst `NoAction` (nicht über `decisions` überschreibbar) |
| beide | alles andere oder ein Typ mit Standardantwort in der jeweils anderen Phase | Entscheidung des Aufrufers oder Passworteintrag; ohne einen solchen bricht der Download ab (`decision_required`) |
| Online-Legitimation | `TlsVerificationConfiguration` | keine: `Trusted` nur aus `trustedCertificates` (jedes vorgelegte Zertifikat aufgelistet), sonst `NonTrusted` und Abbruch; siehe [TLS-Zertifikatsvertrauen](#tls-zertifikatsvertrauen-online-legitimation) |
| Online-Legitimation | jede andere Online-Konfiguration | keine: ein `passwords`-Eintrag für eine Passwortkonfiguration, sonst Abbruch |

Beispiele für Konfigurationstypen, die ein Aufrufer entscheiden kann (Katalog in Quelle (b);
Enum-Member werden aus dem Live-Objekt gelesen, daher ist `availableSelections` maßgeblich):
`DataBlockReinitialization` (`StopPlcAndReinitialize`, `NoAction`),
`DataBlockReinitializationOrKeepActualValues`, `DifferentTargetConfiguration` (`AcceptAll`,
`NoAction`), `ResetModule` (`DeleteAll`, `NoAction`), `InitializeMemory`, `OverwriteOnMemoryCard`,
`ExpandDownload`, `ProtectionLevelChanged`, `UserManagementDownload`, `WaitOnReboot`;
Prüfkonfigurationen wie `CheckBeforeDownload`, `UpgradeTargetDevice`, `DowngradeTargetDevice`,
`OverwriteHmiData`, `FitHmiComponents`, `DownloadCertificate` nehmen `true`. Mehrere davon haben
schwerwiegende Folgen (Rücksetzen, Speicherinitialisierung, reinitialisierte Datenbausteine,
Firmwarewechsel): In read-write sieht der Mensch jede Entscheidung des Aufrufers in der Rückfrage,
bevor irgendetwas geschieht.

### Abbruchgründe

| Grund | Bedeutung | Erneut versuchen mit |
| --- | --- | --- |
| `decision_required` | Die Konfiguration hat einen `decisionKey`, aber keine Standardantwort, keine Entscheidung und keinen Passworteintrag. | `decisions: { "<decisionKey>": "<one of availableSelections>" }` (Auswahl), `true` (Prüfung) oder `passwords` (Art Passwort) |
| `unsupported_configuration` | Ihre Typkette enthält keinen öffentlichen Typ des Konfigurations-Namespace, daher existiert kein Schlüssel. | nichts; melden |
| `selection_unavailable` | Die Auswahl (Standard oder Aufrufer) ist kein Member des Live-Enums. | einem der `availableSelections` |
| `decision_not_applicable` | Dem Live-Objekt fehlt das Member, das die Antwort braucht (`CurrentSelection`, `Checked`, `SetPassword`); `detail` sagt, welches. | einer Antwort der richtigen Art |
| `password_unavailable` | Die genannte Umgebungsvariable ist im Worker nicht gesetzt oder leer; `detail` wiederholt den Namen nicht (er kann ein eingefügtes Passwort sein). | die Variable in der MCP-Client-Konfiguration setzen und neu starten |
| `decision_rejected` | Das Setzen der Antwort warf eine Ausnahme (TIA Portal hat sie abgelehnt). | `engineeringError` prüfen |
| `configuration_limit_exceeded` | Mehr als 100 Konfigurationen. | nichts; melden |
| `certificate_not_trusted` | Nur `online_legitimation`: Die TLS-Zertifikatsprüfung legte kein Zertifikat vor, mehr als 16 oder mindestens eines, dessen SHA-256 nicht in `trustedCertificates` steht; `NonTrusted` wurde gesetzt, und `abort.tls` enthält die Nachweise. | `trustedCertificates` mit jedem vorgelegten Fingerabdruck, **erst nachdem der Benutzer sie geprüft hat** |
| `insecure_communication` | Eine Passwortkonfiguration meldete `IsSecureCommunication` false oder nicht lesbar, und der Aufruf hatte kein `allowInsecurePasswordTransfer: true`; das Passwort wurde nicht gesendet (Audit T1). | `allowInsecurePasswordTransfer: true`, **erst nachdem der Benutzer zugestimmt hat** |

**Kein Werfen, wo V21 `NoAction` dokumentiert (Audit T2, nicht live getestet).** Eine Auswahlkonfiguration, die mit
`decision_required`, `selection_unavailable` oder `decision_not_applicable` abbrechen würde oder nach dem Abbruch kommt
und deren Live-Enum das exakte Member `NoAction` hat (V21 dokumentiert es, „No action“, z. B. an `StopModulesSelections`,
`OverwriteSystemDataSelections`, `DifferentTargetConfigurationSelections`, `DataBlockReinitializationSelections`,
`ResetModuleSelections`, `StartModulesSelections`, `UploadMissingProductsSelections`), wird mit `NoAction` beantwortet: Der
Eintrag ist `selected` `NoAction` mit Quelle `policy`, der Abbruch wird wie bisher festgehalten, und der Callback kehrt
normal zurück, statt zu werfen (K27: Auf ein Werfen aus dem TLS-Callback folgte das Beenden von TIA Portal). TIA Portal
lässt den Schritt dann selbst scheitern oder überspringt ihn; der Aufruf scheitert mit dem festgehaltenen Abbruch, was
`Download` auch zurückgab. Konfigurationen ohne `NoAction` (Prüf- und Passwortkonfigurationen, `TargetForSoftware`,
`ConsistentBlocksDownload`, `ProtectionLevelChanged`, `UserManagementPreDownload`, ...), eine abgelehnte Antwort und die
Konfigurationsgrenze brechen weiter durch Werfen ab; ob dieses Werfen TIA Portal beenden kann, ist nicht bekannt.

Bei der TLS-Zertifikatsprüfung bedeutet `decision_not_applicable`, dass ihre Nachweise nicht
vollständig gelesen werden konnten oder dass `CurrentSelection` fehlt oder kein
`TlsVerificationConfigurationSelection` ist (es wurde nichts gesetzt), `selection_unavailable`, dass
dem Live-Enum `Trusted` fehlt, und `decision_rejected`, dass das Setzen von `Trusted` oder
`NonTrusted` eine Ausnahme warf; keiner davon kann mit einem Argument erneut versucht werden.

Die Fehlermeldung (`PlcDownloadPolicy.AbortMessage`, gemeinsam für Worker, FakeWorker und Host)
formuliert den erneuten Versuch aus, zum Beispiel `Retry with decisions: { "DataBlockReinitialization": "<selection>" }
using one of: StopPlcAndReinitialize, NoAction`. Ein Abbruch wird als versuchter Fehlschlag
(`worker_operation_failed`) mit den Nachweisen gemeldet; ein Abbruch nach dem Download bedeutet, dass
der Download bereits übertragen wurde und die CPU gestoppt sein kann.

Der Host lehnt unabhängig davon (`protocol_error`) Nachweise ab, in denen ein angewendeter Eintrag
nicht genau dem entspricht, was das geplante Entscheidungsset für seinen kanonischen Typ und seine
Phase vorschreibt (Auswahl, Art der Antwort, `source`, `passwordVariable`; bei `StartModules` die
Antwort, die `startAfterDownload` verlangt; eine Standardantwort, wo der Aufrufer entschieden hat),
ein angewendeter Eintrag auf einen abgebrochenen folgt, ein abgebrochener Eintrag eine Antwort trägt,
`decisionKey` nicht der Kurzname von `canonicalType` ist, ein `canonicalType` außerhalb des Namespace
liegt, `availableSelections` für eine andere Art aufgelistet ist, der `abort` nicht den ersten
abgebrochenen Eintrag beschreibt oder sein Grund nicht zum Plan passt (zum Beispiel
`selection_unavailable`, wo nichts entschieden wurde, `unsupported_configuration` mit einem Schlüssel
oder `configuration_limit_exceeded` vor dem 101. Eintrag). Für die TLS-Zertifikatsprüfung lehnt er
außerdem Nachweise ab, in denen ein anderer Eintrag als eine `TlsVerificationConfiguration` der
Phase `online_legitimation` `tls` trägt, ein `approved`-Kennzeichen von der Zugehörigkeit zu den
geplanten `trustedCertificates` abweicht, `Trusted` ohne Quelle `caller`, nach einem Abbruch oder mit
Nachweisen protokolliert ist, die es nicht zulassen (ein unlesbares Member, keine oder zu viele
Zertifikate, eines nicht freigegeben), eine `NonTrusted`-Antwort ohne Quelle `policy`, ohne
`tls.selectionSet: NonTrusted` oder ohne `abort` protokolliert ist (sie verankert den Abbruch wie der
erste abgebrochene Eintrag; danach darf keine Download-Konfiguration mehr beantwortet werden), das
`tls` des Abbruchs von dem des ersten abgebrochenen (oder abgelehnten) Eintrags abweicht,
`certificate_not_trusted` mit Nachweisen kommt, die `Trusted` zulassen würden, oder ein
`online_legitimation`-Eintrag eine `message` trägt.

## Passwörter

`passwords` ordnet einem Kurznamen eines Passwortkonfigurationstyps den **Namen** einer
Umgebungsvariablen zu. Der Worker-Prozess erbt die Umgebung des Servers, die der Benutzer in der
MCP-Client-Konfiguration setzt (`"env": { "PLC_WRITE_PW": "..." }` neben `"command"`), sodass das
Passwort nie durch das Modell oder die Tool-Argumente läuft. Für eine passende Konfiguration liest
der Worker die Variable aus seiner eigenen Umgebung, kopiert sie in einen schreibgeschützten
`SecureString`, ruft `SetPassword(SecureString)` auf dem Live-Objekt auf (per Reflection, genau eine
öffentliche Instanzmethode mit diesem einen Parameter) und verwirft den `SecureString`. Eine fehlende
oder leere Variable bricht mit `password_unavailable` ab und nennt die Variable (nie einen Wert); der
Host lehnt dies bereits bei der Validierung ab. Passwörter erscheinen nie in Ergebnissen, Logs oder
im Audit: Die Nachweise protokollieren nur `passwordVariable` (den Namen), und die Effekte listen
`{ type, variable }`. Ohne passenden `passwords`-Eintrag bricht eine Passwortkonfiguration ab
(`decision_required`, Art `password`). Der Umgebungs-String selbst kann im verwalteten Speicher nicht
genullt werden (siehe Grenzen).

**Zuerst der sichere Kanal (Audit T1, nicht live getestet).** Vor `SetPassword` liest der Recorder
`IsSecureCommunication` (V21: `DownloadPasswordConfiguration`, `OnlinePasswordConfiguration`,
`OnlineAuthenticationConfiguration`, `UploadPasswordConfiguration`, „Is secure communication used.“) per Reflection
(genau eine öffentliche lesbare `bool`-Eigenschaft) und hält es als `secureCommunication` am Eintrag fest (weggelassen,
wenn nicht lesbar). `true`: Das Passwort wird gesendet. `false` oder nicht lesbar: Das Passwort wird **nicht** gesendet,
und der Download bricht mit `insecure_communication` ab, außer der Aufruf hat `allowInsecurePasswordTransfer: true`.
TIA Portal selbst erlaubt den nicht sicheren Kanal, deshalb lehnt der MCP ihn nicht grundsätzlich ab (Eigentümerprinzip),
sondern macht ihn zu einer ausdrücklichen, bestätigten Entscheidung. Der Host weist Nachweise zurück, die `password_set`
über einen nicht als sicher gemeldeten Kanal ohne die Zustimmung behaupten. Dieselbe Regel gilt für die
Online-Legitimationspasswörter der Download-Verbindung.

## TLS-Zertifikatsvertrauen (Online-Legitimation)

> **Nicht live getestet.** Gebaut für einen Live-Befund (TIA Portal V21, PLCSIM Advanced 8.0):
> `plc_download` nach PLCSIM schlug mit "Verbindung mit Baugruppe PLC_1 fehlgeschlagen" und ohne
> `configurationsSeen` fehl, während der eigene Download von TIA Portal zuerst fragt, ob dem
> unbekannten selbstsignierten Zertifikat des Geräts vertraut werden soll. Der Download hatte
> `ConnectionConfiguration.OnlineLegitimation` nie abonniert. Ob V21 diese Frage während
> `Download(...)` über die `OnlineLegitimation` des Download-Providers stellt, sagt keine Quelle; die
> [erste Live-Prüfung](#live-abnahmeprüfungen) entscheidet. Offline-Qualifizierung: Unit-Tests
> (`PlcTlsTrustPolicyTests`, `PlcCallbackRecorderTlsTests`) und das FakeWorker-Szenario
> `plc-download-tls` (`PlcTlsTrustMcpTests`, eine Ersatz-`TlsVerificationConfiguration` mit zwei
> festen Testzertifikaten, vorgelegt über den produktiven Recorder).

- **Registrierung.** Der Worker liest `DownloadProvider.Configuration` einmal; genau diese
  `ConnectionConfiguration`-Instanz löst den Pfad auf und trägt den Handler. Ein
  `OnlineConfigurationDelegate` wird unmittelbar vor `Download(...)` auf ihr Ereignis
  `OnlineLegitimation` abonniert, und derselbe Delegate wird in `finally` entfernt (Quelle:
  online-status.md, "TLS legitimation and online authentication"). Jede dort vorgelegte Konfiguration
  läuft durch denselben `PlcDownloadCallbackRecorder` wie die Download-Konfigurationen, Phase
  `online_legitimation`, und wird in `configurationsSeen` protokolliert. Ihr kanonischer Typ ist der
  erste öffentliche Typ aus `Siemens.Engineering.Online.Configurations` in der Laufzeit-Typkette; der
  Kurzname ist der `decisionKey`. Online-Konfigurationen dokumentieren keine `Message`, daher ist
  `message` in dieser Phase null.
- **Die Vertrauensfrage** (`TlsVerificationConfiguration`) wird nie durch eine Standardantwort, eine
  Entscheidung oder ein Passwort beantwortet. Die gemeinsame Siemens-freie `PlcTlsTrustPolicy`
  (Contracts) liest zuerst die Nachweise per Reflection auf dem Laufzeitobjekt: `PlcName` und
  `VerificationInfo` als Text (ihre Typen sind undokumentiert) und für jedes Element von
  `Certificates` dessen `Certificate` (`X509Certificate2`): den SHA-256 über `RawData`, den
  SHA-1-`Thumbprint`, `Subject`, `Issuer`, `NotBefore` und `NotAfter` (UTC). Sie prüft außerdem, dass
  `CurrentSelection` genau eine öffentliche, les- und schreibbare Eigenschaft des dokumentierten
  Enum-Typs `TlsVerificationConfigurationSelection` ist. `CurrentSelection` wird nur dann auf
  `Trusted` gesetzt (Quelle `caller`), wenn jedes Member gelesen werden konnte, mindestens ein und
  höchstens 16 Zertifikate vorgelegt wurden und der SHA-256-Fingerabdruck **jedes** vorgelegten
  Zertifikats in `trustedCertificates` steht. Andernfalls wird `NonTrusted` gesetzt (nur wenn
  `CurrentSelection` seinen dokumentierten Typ hat; in eine fehlende oder fremde Eigenschaft wird
  nichts geschrieben). **Wurde `NonTrusted` gesetzt, kehrt der Callback normal zurück (K27, seit
  2026-11-02):** Der Eintrag wird als `selected` `NonTrusted` mit Quelle `policy` protokolliert,
  `abort` wird mit den Nachweisen protokolliert (`certificate_not_trusted`, oder
  `decision_not_applicable` / `selection_unavailable` / `decision_rejected`, wenn die Nachweise oder
  die `Trusted`-Antwort fehlschlugen), und TIA Portal lässt die Verbindung selbst scheitern, wie
  "Verbindung abbrechen" in seiner eigenen Vertrauensrückfrage. Was auch immer `Download(...)` danach
  tut (zurückkehren, eine `EngineeringException` werfen), der protokollierte Abbruch entscheidet: Der
  Worker meldet `worker_operation_failed` mit `abort.tls`, nie Erfolg. Nur wenn `NonTrusted` nicht
  gesetzt werden konnte, wirft der Callback weiterhin (Quelle d: "A callback exception cancels the
  operation"), die einzige verbleibende Ablehnung und ein bekanntes Risiko (unten). Eine spätere
  TLS-Frage nach einem Abbruch wird nie vertraut und, wo möglich, erneut ohne Werfen mit `NonTrusted`
  beantwortet.

  > **Live-Beobachtung K27 (2026-11-02, TIA Portal V21, PLCSIM Advanced 8.0, Projekt `MCP_Safety_01`,
  > PLC_F1 = CPU 1516F-3 PN/DP 6ES7 516-3FN02-0AB0 V2.9, PC-Schnittstelle PLCSIM #1, Ziel `1 X1`, frische
  > PLCSIM-Instanz `MCP_Safety`, CPU1500_Unspecified, Stop).** Mit dem früheren Werfen aus dem
  > TLS-Callback endeten `plc_download` ohne Entscheidungen, `plc_download` mit
  > `{ SafetyProgram: ConsistentDownload }` und `plc_tia_online go_online` (überhaupt kein Download)
  > jeweils in `portal_lost` ("Access to a disposed object of type
  > 'Siemens.Engineering.Connection.ConnectionConfiguration'"), und TIA Portal lief nicht mehr
  > (`bind_project list_portals` → keine Portale); die Instanz war unverändert (nichts übertragen).
  > Dasselbe "Online verbinden" in der TIA-Oberfläche zeigte die Vertrauensrückfrage ("'PLC_F1' ist
  > möglicherweise kein vertrauenswürdiges Gerät": IP-Adresse passt nicht zum Zertifikat, unbekanntes
  > selbstsigniertes Zertifikat); "Verbindung abbrechen" ließ TIA Portal weiterlaufen, und der
  > Online-Status besagte, dass das Gerät nicht vertrauenswürdig ist. Die Standard-CPU PLC_1 von
  > `MCP_Livetest_01` zeigte die Rückfrage nie. Dass das Werfen die Beendigung verursachte, ist die
  > wahrscheinlichste Erklärung, keine bewiesene Ursache.
  >
  > **Erneuter Test mit der Ablehnung ohne Werfen (2026-11-02, Build d111e8c):** `plc_tia_online go_online`
  > auf PLC_F1 ohne `trustedCertificates` lieferte `worker_operation_failed` mit
  > `abort.tls` (den Fingerabdrücken), und TIA Portal lief weiter; mit dem vom Benutzer geprüften
  > Fingerabdruck (`trustedCertificates`) wurde der Verbindung vertraut. **Ein fehlersicherer Download
  > über Openness wird von TIA Portal selbst abgelehnt:** `plc_download` von PLC_F1 nach PLCSIM mit dem
  > vertrauten Zertifikat schlug mit einer `EngineeringTargetInvocationException` aus
  > `DownloadProvider.Download` fehl, deren Meldung "Laden oder Überladen fehlersicherer Daten mit
  > Openness ist nicht erlaubt." lautete (das Laden fehlersicherer Daten mit Openness ist nicht
  > erlaubt). Daher wurde `decisions: { SafetyProgram: ... }` auf V21 für diese F-CPU nie erreicht; das
  > F-Programm musste in der TIA-Portal-Oberfläche geladen werden ("Erweitertes Laden", PLCSIM, F-CPU
  > anhand ihrer Seriennummer identifiziert), danach führte das MCP den Test (`plcsim_test`,
  > `compare_to_online` identisch, F-Gesamtsignatur online = offline). Ob andere F-CPUs,
  > Firmwareversionen oder eine reine Standardänderung einer F-CPU über Openness geladen werden
  > können, ist nicht getestet. Es gibt kein Standardvertrauen, keine Option „allem vertrauen“ und
  keinen Weg, die Prüfung zu überspringen; `EnableLegacyCommunication` wird nie gelesen oder gesetzt
  (die Quelle: Setzen deaktiviert TLS).
- **Online-Passwörter**, die in dieser Phase vorgelegt werden (`OnlinePasswordConfiguration`,
  `OnlineReadAccessPassword`: `SetPassword(SecureString)`), werden wie Download-Passwörter aus
  `passwords` beantwortet (Schlüssel = Kurzname des Typs, Wert = Umgebungsvariablenname). Für keine
  Online-Konfiguration gibt es eine Standardantwort; alles andere, zum Beispiel
  `OnlineAuthenticationConfiguration` (Anmeldedaten über `OnlineCredentials`, von diesem Tool nicht
  beantwortet), bricht mit `decision_required` oder `decision_not_applicable` ab.

Die `tls`-Nachweise (am Eintrag in `configurationsSeen` und, bei einer Ablehnung, an `abort`):

| Member | Bedeutung |
| --- | --- |
| `plcName`, `verificationInfo` | `PlcName` und `VerificationInfo` als Text (null, wenn null oder unlesbar) |
| `certificates[]` | je lesbarem Zertifikat, in Vorlagereihenfolge: `sha256` (64 Hexadezimalzeichen in Großbuchstaben über die DER-Bytes: der Wert, den `trustedCertificates` nimmt), `sha1Thumbprint` (nur für Anzeigen, die nichts anderes zeigen; nie als Fingerabdruck akzeptiert), `subject`, `issuer`, `notBefore`, `notAfter` (ISO 8601 UTC; ein abgelaufenes Zertifikat wird gemeldet, nicht abgelehnt: Der Benutzer entscheidet), `approved` (sein `sha256` steht in `trustedCertificates`) |
| `certificatesPresented`, `certificatesComplete` | wie viele das Gerät vorgelegt hat (eins mehr als 16, wenn es mehr vorgelegt hat) und ob jedes gelesen und aufgelistet wurde |
| `unreadableMembers` | Member, die fehlten, mehrdeutig waren, eine Ausnahme warfen oder (`CurrentSelection`) nicht den dokumentierten Typ haben; Vertrauen wird dann nie gewährt |
| `selectionSet`, `source` | `Trusted` mit Quelle `caller`, `NonTrusted` mit Quelle null (der `configurationsSeen`-Eintrag hat dann Auswahl `NonTrusted`, Quelle `policy`; K27) oder null, wenn nichts gesetzt werden konnte (der Callback warf) |

Die Abbruchmeldung (`PlcDownloadPolicy.AbortMessage`) listet jedes Zertifikat mit SHA-256, Subject,
Issuer und Gültigkeit, sagt, ob `NonTrusted` gesetzt wurde, weist den Agenten an, nicht selbst zu
entscheiden, sondern dem Benutzer die Fingerabdrücke zu zeigen und sie mit dem eigenen Zertifikat
des Geräts vergleichen zu lassen (zum Beispiel in der Vertrauensrückfrage von TIA Portal mit
"Zertifikate anzeigen" / "Show certificates"; wo eine Anzeige nur einen SHA-1-Thumbprint zeigt, mit
`sha1Thumbprint`), und nennt den exakten erneuten Versuch
`trustedCertificates: ["<sha256 of certificate 1>", "<sha256 of certificate 2>"]` (jedes vorgelegte
Zertifikat muss aufgelistet sein). Der vorgesehene Ablauf ist: ohne `trustedCertificates` aufrufen,
`result.value.abort.tls` lesen, den Benutzer prüfen lassen, mit jedem Fingerabdruck erneut versuchen.
In read-write listet die Rückfrage des erneuten Versuchs die Fingerabdrücke, sodass der Benutzer sie
noch einmal freigibt, bevor irgendetwas eine Verbindung aufbaut; der Audit-Eintrag bewahrt die
angeforderten Argumente und die Antwort, beide mit den Fingerabdrücken (Fingerabdrücke sind keine
Geheimnisse).

## TIA-Portal-Bestätigungsdialoge während eines Downloads

Die `TiaPortalSession` des Workers beantwortet TIA-Portal-`Confirmation`-Ereignisse sitzungsweit:
`Yes` in read-write und full, `No` in read-only. Während `plc_download` läuft, beantwortet ein um die
Worker-Methode geöffneter Scope (nie verschachtelt; als Sicherheitsnetz in einem `finally`
verworfen) **jede** Bestätigung mit der Policy der Anfrage, bevor er sie in `portalConfirmations`
protokolliert: `No` für `decline` (Standard) und `Yes` für `accept` (`Yes` nur, wenn der
Zugriffsmodus des Workers TIA-Bestätigungen erlaubt, was jeder Modus tut, der laden kann). Ein
Dialog, den TIA Portal auslöst, bevor `Download` zurückkehrt und die Nachweise gesammelt sind,
erhält daher die Policy des Aufrufers, nie die sitzungsweite Antwort; ein späterer erhält dieselbe
Antwort, bis die nächste Worker-Anfrage beginnt (siehe das Ende dieses Abschnitts).

Bevor die Nachweise kopiert werden, schließt der Download den Scope; das Schließen wartet auf eine
laufende Protokollierung, sodass jede im Scope beantwortete Bestätigung in den Nachweisen steht und
in `portalConfirmationsObserved` gezählt wird. Unter `decline` lässt jede Bestätigung den Aufruf
fehlschlagen (`worker_operation_failed` mit den Nachweisen), selbst wenn der Download selbst
`state: Success` ohne Fehler gemeldet hat; die Fehlermeldung sagt, warum, und schlägt
`portalConfirmations: "accept"` vor. Die Antwort `No` kann dazu geführt haben, dass TIA Portal einen
Teil des Downloads übersprungen oder abgebrochen hat, daher PLC oder Simulator prüfen. Unter `accept`
schlägt der Aufruf ihretwegen nicht fehl; eine Warnung meldet, wie viele mit Ja beantwortet wurden.
Ob V21 solche Dialoge während `Download` auslöst, ist nicht verifiziert (siehe die
[erste Live-Prüfung](#live-abnahmeprüfungen)). Eine Bestätigung, die **nach** dem Schließen des
Scopes ausgelöst wird (`Download()` in V21 kann zurückkehren, bevor das Gerät fertig ist, daher ist
ein später Dialog desselben Downloads möglich), steht nicht in den Nachweisen, wird aber nie mit dem
sitzungsweiten `Yes` beantwortet: Der geschlossene Scope bleibt mit der Antwort des Aufrufs bestehen,
bis die nächste Worker-Anfrage beginnt (`TiaPortalSession.EndLingeringConfirmations`, vor jeder
Anfrage aufgerufen; ein neuer Scope beendet ihn ebenfalls), protokolliert jeden späten Dialog sofort
(zwischen Anfragen erreicht der stderr des Workers das Server-Log) und meldet sie als Warnung in der
Antwort dieser nächsten Anfrage, gleich zu welchem Tool sie gehört. Operationen ohne Scope behalten
die sitzungsweite Policy unverändert.

## Herkunft jedes verwendeten Siemens-Members

Quellen (schreibgeschützte Referenzdaten, nie ausgeführt): **(a)** Siemens `tia-portal-ai-extensions`
`openness_development/skills/online-and-download/SKILL.md`; **(b)** `totally-integrated-claude`
`skills/tia-plc-operations/references/download-upload.md` und
`skills/tia-networks/references/online-connection-configuration.md` (laut eigener Angabe gegen die
installierte V21-XML-Dokumentation geprüft); **(c)** die aus echten DLL-Metadaten extrahierte
V21-Typnamen-Baseline (`api-baselines/v21/base.json`, `step7.json`; nur Typnamen und Assemblies, keine
Signaturen); **(d)** `totally-integrated-claude` `skills/tia-plc-operations/references/online-status.md`,
Abschnitte "TLS legitimation and online authentication" und "Installed V21 online configuration type
catalogue" (die einzige Quelle, die für die Online-Legitimation-Member verwendet wurde).

Statuswerte: **Typname in V21-Baseline verifiziert** (c listet den Typ in dieser Assembly);
**Signatur aus V21-XML-geprüfter Referenz** (b zeigt das Member oder die Aufrufform);
**angenommen — durch Compile gegen echte DLL verifizieren** (weder a noch b zeigt das Member; nur in
die Stubs aufgenommen, weil der Auftrag es festlegt); **Reflection** (zur Laufzeit am Live-Objekt
gebunden, nie kompiliert; ein fehlendes Member schlägt fail-closed mit einem Abbruchgrund fehl, sodass
eine falsche Annahme den Worker nicht zum Absturz bringen, aber einen Abbruch auslösen kann).

| Siemens-Member | Assembly (c) | Quelle | Status |
| --- | --- | --- | --- |
| `Siemens.Engineering.Download.DownloadProvider` (Typ, ein `IEngineeringService`) | Base | a, b, c | Typname in V21-Baseline verifiziert; Service-Rolle aus `GetService<DownloadProvider>()` in a/b |
| `DeviceItem.GetService<DownloadProvider>()` auf dem CPU-Geräteelement der PLC (bestehendes `HardwareObject.GetService<T>`; das Geräteelement ist dasjenige, dessen `SoftwareContainer` die `PlcSoftware` trägt, der bestehende Locator-Weg des Repos) | Base | a, b | Signatur aus V21-XML-geprüfter Referenz |
| `DownloadProvider.Configuration` → `ConnectionConfiguration` | Base | b | Signatur aus V21-XML-geprüfter Referenz |
| `DownloadProvider.Download(IConfiguration, DownloadConfigurationDelegate, DownloadConfigurationDelegate, DownloadOptions)` → `DownloadResult` | Base | a, b, c | Signatur aus V21-XML-geprüfter Referenz |
| `DownloadConfigurationDelegate` = `void (DownloadConfiguration)` | Base | b, c | Typname in V21-Baseline verifiziert; Signatur aus V21-XML-geprüfter Referenz (Methodengruppen-Callbacks in b) |
| `ConnectionConfiguration.Modes` (`ConfigurationModeComposition`, enumeriert) | Base | b, c | Signatur aus V21-XML-geprüfter Referenz |
| `ConfigurationMode.Name`, `.PcInterfaces` (`ConfigurationPcInterfaceComposition`, enumeriert) | Base | b, c | Signatur aus V21-XML-geprüfter Referenz |
| `ConfigurationPcInterface.Name`, `.Number` (`int`), `.TargetInterfaces` (`ConfigurationTargetInterfaceComposition`, enumeriert) | Base | b, c | Signatur aus V21-XML-geprüfter Referenz |
| `ConfigurationTargetInterface.Name`; konvertiert zu `IConfiguration` | Base | a, b, c | Signatur aus V21-XML-geprüfter Referenz |
| `IConfiguration` (Marker-Interface; keine Member verwendet) | Base | b, c | Typname in V21-Baseline verifiziert |
| `DownloadOptions`-Member `Hardware`, `Software`, `SoftwareOnlyChanges` (nach Name geparst) | Base | a, b, c | Typname in V21-Baseline verifiziert; Member-Namen aus V21-XML-geprüfter Referenz |
| `DownloadResult.State` (`DownloadResultState`), `.ErrorCount`, `.WarningCount`, `.Messages` (`DownloadResultMessageComposition`) | Base | b, c | Signatur aus V21-XML-geprüfter Referenz |
| `DownloadResultState.Error` (nach Name verglichen) | Base | b, c | Signatur aus V21-XML-geprüfter Referenz |
| `DownloadResultMessageComposition` enumeriert `DownloadResultMessage` | Base | c | Typnamen in V21-Baseline verifiziert; Enumeration angenommen — durch Compile gegen echte DLL verifizieren |
| `DownloadResultMessage.Message` (`string`), `.State` (`DownloadResultState`), `.Messages` (`DownloadResultMessageComposition`) | Base | c (nur Typ) | angenommen — durch Compile gegen echte DLL verifizieren |
| `Download.Configurations.DownloadConfiguration` (Callback-Basis) `.Message` (kompilierter Cast im Worker) | Base | b, c | Signatur aus V21-XML-geprüfter Referenz |
| `CurrentSelection` (öffentliche, les- und schreibbare Enum-Eigenschaft) auf **jeder** Live-Konfiguration, gesetzt über den exakten Enum-Member-Namen des eigenen Typs der Eigenschaft | Base/Step7 | b | **Reflection**. In b gezeigt für `StopModules`, `ConsistentBlocksDownload`, `TargetForSoftware`, `StartModules` (und für die sechs Typen mit Standardantwort von der Referenz-Probe kompiliert); für jeden anderen Auswahltyp aus dem Auswahlmuster des Katalogs von b angenommen — ein Typ ohne sie bricht mit `decision_not_applicable` ab |
| `StopModulesSelections.StopAll`, `StartModulesSelections.StartModule`/`NoAction`, `ConsistentBlocksDownloadSelections.ConsistentDownload`, `AlarmTextLibrariesDownloadSelections.ConsistentDownload`, `OverwriteSystemDataSelections.Overwrite`, `TargetForSoftwareSelections.PlcSimulationAdvanced` und `.CPU` (Standardantworten, nach Name) | Base/Step7 | b, c | Member-Namen aus V21-XML-geprüfter Referenz (b zeigt `TargetForSoftwareSelections.CPU` in seinem Callback-Beispiel und beide Member in seinem Katalog); zur Laufzeit mit `Enum.IsDefined` geprüft (sonst `selection_unavailable`) |
| `DownloadCheckConfiguration.Checked` (öffentliches, les- und schreibbares `bool`), auf true gesetzt | Base | b | **Reflection**; Member-Name aus V21-XML-geprüfter Referenz ("Base check configuration exposing `Checked`"); der Typ `bool` ist angenommen — ein Nicht-`bool`- oder fehlendes Member bricht mit `decision_not_applicable` ab |
| `DownloadPasswordConfiguration.SetPassword(SecureString)` (öffentliche Instanzmethode, ein `System.Security.SecureString`-Parameter; Rückgabetyp nicht eingeschränkt) | Base | b | **Reflection**; Signatur aus V21-XML-geprüfter Referenz ("Base password configuration with `IsSecureCommunication` and `SetPassword(SecureString)`"); eine fehlende Methode bricht mit `decision_not_applicable` ab |
| `IsSecureCommunication` von `DownloadPasswordConfiguration`, `OnlinePasswordConfiguration`, `UploadPasswordConfiguration` (öffentliches lesbares `bool`, „Is secure communication used.“) | Base | members.txt | **Reflection** (`PlcPasswordChannel`, Audit T1); vor jedem `SetPassword` gelesen; false oder nicht lesbar ohne `allowInsecurePasswordTransfer` bricht mit `insecure_communication` ab; nicht live getestet |
| Die sechs Typen mit Standardantwort und die generischen Antworten brauchen keinen Cast: Der kanonische Typ wird nach Name aus der Typkette gelesen, und jede Antwort wird per Reflection auf dem Laufzeitobjekt angewendet | — | — | Design (die früheren kompilierten Casts auf die sechs Typen sind entfallen; die Probe kompiliert weiterhin deren `CurrentSelection`) |
| Die Laufzeit-Typkette der Live-Konfiguration enthält ihren öffentlichen V21-Typ in `Siemens.Engineering.Download.Configurations` (exakter Typabgleich) | — | keine | angenommen — live verifizieren; eine Abweichung bricht ab (fail-closed), sie antwortet nie |
| `EngineeringException`, `EngineeringObjectDisposedException`, `NonRecoverableException` | Base | bestehende Stubs | unveränderte bestehende Deklarationen |
| Ereignis `TiaPortal.Confirmation`, `ConfirmationEventArgs.Result` (auf `ConfirmationResult.No` oder `.Yes` gesetzt) | Base | bestehende Stubs | unveränderte bestehende Deklarationen, vom sitzungsweiten Handler verwendet; der Download-Scope ändert nur die Antwort |
| Öffentliche `Text`-Eigenschaft (`string`) von `ConfirmationEventArgs`, per Reflection für `portalConfirmations.text` gelesen | Base | keine | **in den Stubs nicht deklariert; nicht verifiziert**. Nie einkompiliert: fehlend oder unlesbar ergibt `text: null` |
| `ConnectionConfiguration.OnlineLegitimation` (Ereignis vom Typ `OnlineConfigurationDelegate`), auf der von `DownloadProvider.Configuration` gelieferten Instanz vor `Download` abonniert und in `finally` entfernt | Base | d | Signatur aus Quelle (`config.OnlineLegitimation += legitimation;` und `-=` in `finally`); bestehende Stub-Deklaration (zuvor von `plc_tia_online` verwendet). Dass der Download seine Legitimation über die Konfiguration **dieses** Providers auslöst, sagt d nicht: aus dem gemeinsamen Typ `ConnectionConfiguration` angenommen — live verifizieren |
| `Online.OnlineConfigurationDelegate` = `void (OnlineConfiguration)`; `Online.Configurations.OnlineConfiguration` (Basis-Callback-Konfiguration) | Base | c, d | Typnamen in V21-Baseline verifiziert; bestehende Stub-Deklarationen |
| `Online.Configurations.TlsVerificationConfiguration` (kanonischer Typ, nach vollständigem Namen in der Laufzeit-Typkette abgeglichen; nie kompiliert) | Base | c, d | Typname in V21-Baseline verifiziert; **Reflection** |
| `TlsVerificationConfiguration.PlcName`, `.VerificationInfo` | Base | d | **Reflection**; Namen aus d ("bind `PlcName`, `VerificationInfo`"), Typen undokumentiert → als Text gelesen (`string`, sonst `ToString()`); fehlend oder werfend schlägt fail-closed fehl (kein Vertrauen) |
| `TlsVerificationConfiguration.Certificates`, enumeriert (`Online.Security.VerificationCertificateComposition` aus `VerificationCertificate`) | Base | c, d | **Reflection**; Name aus d (`tls.Certificates` im Callback-Beispiel), Typnamen der Composition in V21-Baseline verifiziert ("read-only collection of verification certificates"); dass sie als `IEnumerable` enumeriert, ist angenommen — nicht enumerierbar schlägt fail-closed fehl |
| `VerificationCertificate.Certificate` → `System.Security.Cryptography.X509Certificates.X509Certificate2` | Base | c, d | **Reflection**; Member und Typ aus d ("wrapper exposing the target `X509Certificate2` through `Certificate`"); ein anderer Typ oder null schlägt fail-closed fehl |
| `X509Certificate2.RawData`, `.Thumbprint`, `.Subject`, `.Issuer`, `.NotBefore`, `.NotAfter` | .NET | — | .NET-Basisklassenbibliothek, nicht Siemens |
| `TlsVerificationConfiguration.CurrentSelection` vom Typ `Online.Configurations.TlsVerificationConfigurationSelection`; Member `Trusted` und `NonTrusted` nach Name gesetzt (`NonVerified` nie) | Base | c, d | **Reflection**; Eigenschaft, Typ und Member aus d (Callback-Beispiel `tls.CurrentSelection = … Trusted : … NonTrusted`; Katalog "`NonVerified`, `Trusted`, or `NonTrusted`"), Typname in V21-Baseline verifiziert; der vollständige Name des Eigenschaftstyps wird verglichen und die Member mit `Enum.IsDefined` geprüft; eine fehlende oder fremde Eigenschaft wird nie geschrieben |
| `Online.Configurations.OnlinePasswordConfiguration.SetPassword(SecureString)`, `OnlineReadAccessPassword` (seine Spezialisierung) | Base | c, d | **Reflection** (der generische Passwortzugriff); Typnamen in V21-Baseline verifiziert |
| `OnlineAuthenticationConfiguration` (`GetSupportedAuthenticationTypes()`, `OnlineCredentials`, `IsSecureCommunication`), `OnlineCredentials`, `UserType`, `AuthenticationType` | Base | c, d | **nicht verwendet**: Eine solche Konfiguration wird protokolliert und bricht ab |
| `EnableLegacyCommunication` | Base | d | **nie gelesen oder gesetzt** (d: `true` deaktiviert TLS) |

Für die Entscheidungsunterstützung dieser Funktion wurde kein Stub hinzugefügt oder geändert:
`Checked`, `SetPassword` und das generische `CurrentSelection` werden per Reflection gebunden, nicht
kompiliert, daher stehen sie nicht in den Stubs, und der Compile gegen die echte DLL prüft sie nicht;
das tun die ersten Live-Prüfungen. Auch das TLS-Vertrauen fügt keinen Stub hinzu: Es kompiliert nur
das bestehende Ereignis `OnlineLegitimation` und `OnlineConfigurationDelegate`; jedes Member von
`TlsVerificationConfiguration` wird per Reflection gelesen oder gesetzt.

Stub-Konventionen, die keine Siemens-Fakten sind: Die Stub-Enums deklarieren **keine Member und
keine Werte** (der Worker kompiliert keine Enum-Konstante; er verwendet zur Laufzeit
`Enum.IsDefined`/`Enum.Parse`/`ToString()` nach Name), ihr zugrunde liegender Typ `int` folgt der
Konvention der Stub-Datei, und die Interface-Listen `IEngineeringObject`/`IEngineeringComposition`
folgen dem bestehenden Stub-Stil. Nichts davon beeinflusst die kompilierten Aufrufe des Workers. Die
Parameternamen in der Stub-Signatur von `Download` sind Platzhalter.

Bewusst nicht verwendet: `GetAccessibleDevices()`, `ApplyConfiguration`, die `Find`-Methoden der
Compositions (stattdessen wird exakt enumeriert, sodass die ordinale Eindeutigkeit nicht von der
Semantik von `Find` abhängt), die `Download`-Überladung mit einer `ConfigurationAddress`,
`DeviceItemClassifications.CPU` (das Stub-Enum hat keine Member-Werte, und das Kompilieren einer
erfundenen Konstante wäre zur Laufzeit falsch),
`DownloadSelectionConfiguration`, `OnlineProvider`, `RHDownloadProvider`, `Download(DirectoryInfo, ...)`,
`EnableLegacyCommunication`, `OnlineAuthenticationConfiguration`/`OnlineCredentials`
(Online-Authentifizierung wird nicht beantwortet) und `TlsVerificationConfigurationSelection.NonVerified`.

## Bekannte Grenzen und offene Risiken

- `rhTarget` (Issue #16) ist nicht live getestet: Der Pfad wird aus `list_download_targets` (dem Inventar des `DownloadProvider`)
  geplant und muss sich mit denselben Namen auf `RHDownloadProvider.Configuration` auflösen; ob eine R/H-CPU beide Provider anbietet,
  ist nicht geprüft. Laden in einen Ordner ist `plc_write download_to_folder` ([PLC-Operationen](PLC_OPERATIONS_SUMMARY.de.md)).
  Audit T3 (nicht live getestet): V21 dokumentiert nicht, welches Objekt `RHDownloadProvider` trägt; ein Fremdhinweis nennt das
  R/H-System-`Device`, nicht eine Mitglieds-CPU. `rhTarget` und `download_to_folder rhSystem` suchen deshalb im CPU-Geräteelement,
  dann in jedem Elternobjekt bis einschließlich seines `Device` (`RhSystemProviders`, exakter voller Name in `GetServiceInfos()`);
  die Meldung `unsupported_capability` nennt jedes durchsuchte Objekt. Ein normaler Download einer CPU ohne `DownloadProvider`
  scheitert mit `target_kind_unsupported` und einem Hinweis auf `rhTarget`.

- Nicht live getestet, weder gegen PLCSIM noch gegen Hardware; jedes oben *angenommene* und jedes
  **Reflection**-Member kann live fehlschlagen (Reflection schlägt fail-closed mit einem Abbruchgrund
  fehl).
- **Downloads auf Hardware sind erlaubt.** Der einzige Schutz ist der vom Menschen gewählte
  Zugriffsmodus und, in read-write, die Rückfrage. In `full` kann ein Agent ohne jede menschliche
  Interaktion auf eine reale Steuerung laden, folgenreiche Entscheidungen treffen (Rücksetzen,
  Reinitialisieren, Firmware-Upgrade) und TIA-Portal-Dialoge annehmen. `plc_download` aus den
  Auto-Approve-Listen des Clients heraushalten, wenn die Rückfrage des Clients ebenfalls gewünscht ist.
- Der Siemens-SKILL merkt an, dass Zielschnittstellen an die Provider-Instanz gebunden sind; der
  Worker löst auf einer Instanz auf und lädt auf ihr, aber die Planung liest eine andere Instanz (nur
  Namen überschreiten diese Grenze). Eine Änderung in der Portal-Oberfläche zwischen Planung und
  Download wird nur durch die exakte erneute Auflösung im Worker erkannt.
- `Download()` in V21 kann zurückkehren, bevor das Gerät fertig ist; es gibt keine
  Abschlussprüfung, und das Worker-Timeout (`--worker-timeout-minutes`, Standard 5) begrenzt einen
  langen Download. Ein Timeout oder Absturz während eines Downloads hinterlässt einen unbekannten
  Zielzustand.
- TIA-Portal-Bestätigungen während eines Downloads werden nach der Policy beantwortet; ob V21 sie
  während `Download` auslöst, was ihr Ablehnen (oder Annehmen) mit dem Download macht, und das
  Member für den Dialogtext sind nicht verifiziert. Eine Bestätigung, die nach dem Schließen des
  Scopes ausgelöst wird (zum Beispiel nach einer frühen Rückkehr von `Download`), behält die Antwort
  des Aufrufs, bis die nächste Anfrage beginnt; sie wird als Warnung in dieser nächsten Antwort
  gemeldet (die zu einem internen oder zum Aufruf eines anderen Tools gehören kann, daher ist das
  Server-Log die verlässliche Aufzeichnung), nicht in den Nachweisen des Downloads.
- Die `PLCSIM`-Prüfung ist nur eine Namensprüfung; siehe
  [was sie beweist und was nicht](#was-die-plcsim-namensprüfung-beweist-und-was-nicht).
- `TargetForSoftware` ist auf `PLCSIM` standardmäßig `PlcSimulationAdvanced`; das Verhalten mit
  einfachem PLCSIM (nicht Advanced) ist nicht verifiziert (eine Entscheidung des Aufrufers
  `TargetForSoftware: "CPU"` überschreibt es).
- Entscheidungen des Aufrufers gelten für jede Konfiguration dieses Typs in beiden Phasen; ein Typ,
  der mehrmals vorgelegt wird, erhält jedes Mal dieselbe Antwort.
- Ein Passwort liegt als verwalteter `string` (der Umgebungswert) vor, bevor es in den
  `SecureString` kopiert wird; dieser String kann nicht genullt werden und lebt im Worker bis zur
  Garbage Collection. Ein Passwort, das versehentlich als *gültig aussehender* Variablenname übergeben
  wird, wird wie ein ungültiger geschwärzt, es sei denn, eine Variable dieses Namens ist zufällig in
  der Umgebung des Servers gesetzt (dann wird es als Name dieser Variablen protokolliert).
- Reflection auf Live-Openness-Objekten (möglicherweise Remoting-Proxys) ist nicht verifiziert; das
  bestehende Lesen von `Text` der Bestätigung stützt sich im selben Prozess bereits auf Reflection.
- **TLS-Vertrauen ist nicht live getestet.** Ob V21 die Vertrauensfrage während `Download(...)` über
  `DownloadProvider.Configuration.OnlineLegitimation` stellt (statt über einen anderen Weg, zum
  Beispiel einen Dialog in der Oberfläche, oder überhaupt nicht über Openness), ist nicht verifiziert,
  ebenso wenig die Laufzeitformen von `PlcName`, `VerificationInfo` und `Certificates`. Wird sie dort
  nicht gestellt, schlägt der Download wie zuvor fehl, nun mit dem, was `configurationsSeen` zeigt.
- Die Fingerabdrücke sind die einzige Bindung an das Gerät: `PlcName` und `VerificationInfo` werden
  für den Menschen gemeldet, nicht mit der PLC des Projekts verglichen. Das Tool kann nicht
  verifizieren, dass ein Mensch die Fingerabdrücke geprüft hat: In `full` gibt es keine Rückfrage,
  sodass ein Agent sie aus den Abbruchnachweisen kopieren und erneut versuchen kann; in read-write
  listet die Rückfrage sie auf. `plc_download` aus den Auto-Approve-Listen des Clients heraushalten.
- Ob TIA Portal das Vertrauen nach dem Aufruf behält (zum Beispiel in einem Zertifikatsspeicher des
  Projekts oder der Engineering-Station), ist nicht dokumentiert.
- Eine Callback-Ausnahme, die Siemens verschluckt (der Download läuft weiter), wird trotzdem als
  Abbruch gemeldet, aber der Zielzustand hängt dann von Siemens ab; die Nachweise protokollieren es.
  Ein Fehler wegen eines verworfenen Objekts nach dem eigenen Abbruch des Tools behält die gemeinsame
  Klassifizierung (`portal_lost`, wenn das Portal verloren ging), und seine Meldung beginnt weiterhin
  mit der Erklärung des Abbruchs.

## Live-Abnahmeprüfungen

Diese zuerst, in dieser Reihenfolge, auf einem entbehrlichen Projekt mit PLCSIM oder PLCSIM Advanced
ausführen und erst danach auf einer entbehrlichen Hardware-CPU:

1. **Bestätigungsdialoge während eines normalen Downloads.** Eine minimale PLC mit
   Standardargumenten in read-write laden. Ist das Ergebnis `success: false` mit `state: Success`,
   `errorCount: 0` und `portalConfirmationsObserved > 0`, löst V21 routinemäßig Bestätigungen während
   `Download` aus: jeden `portalConfirmations`-Eintrag (Typ, Text) und den Zustand des Simulators
   festhalten und dann entscheiden, ob `portalConfirmations: "accept"` für diesen Dialog angemessen ist.
2. Den Laufzeittyp jeder Callback-Konfiguration (`configurationsSeen[].type`) zusammen mit ihrem
   `canonicalType`, `kind` und `availableSelections` festhalten, um den exakten Typabgleich und die
   Reflection-Member zu bestätigen; ein unerwartetes `unsupported_configuration` oder
   `decision_not_applicable` bedeutet hier, dass die Live-Objekte von den Annahmen abweichen.
3. Bestätigen, dass `list_download_targets` genau eine `PLCSIM`-PC-Schnittstelle zeigt; bei mehreren
   muss ein weggelassenes `pcInterface` mit `target_ambiguous` fehlschlagen.
4. Eine Konfiguration ohne Standardantwort provozieren (zum Beispiel ein geändertes
   Datenbaustein-Layout → `DataBlockReinitialization`), bestätigen, dass der Abbruch die
   Live-Enum-Member auflistet, mit einer Entscheidung erneut versuchen und `source: caller` bestätigen.
5. Auf einer CPU mit Schreibschutzpasswort: bestätigen, dass der Abbruch nach
   `passwords: { "ModuleWriteAccessPassword": ... }` fragt, dann mit einer in der Client-Konfiguration
   gesetzten Umgebungsvariablen erneut versuchen; bestätigen, dass das Passwort nirgends in der
   Antwort, im stderr des Workers oder in der Audit-Datei erscheint.
6. Hardware: zuerst ein Dry Run (beide Acknowledge-Guards, `TargetForSoftware`-Standard `CPU` in der
   Rückfrage), dann ein tatsächlicher read-write-Download mit angenommener Rückfrage.
7. **TLS-Vertrauen** (PLCSIM Advanced 8.0-Instanz mit selbstsigniertem Zertifikat, der Live-Befund;
   K27: zuerst den Fall vom 2026-11-02, PLC_F1 von `MCP_Safety_01` auf PLCSIM #1 / `1 X1`, wiederholen
   und bestätigen, dass TIA Portal **weiterläuft** (`bind_project list_portals` listet es weiterhin)
   und der Aufruf sauber mit `abort.tls` fehlschlägt, der TLS-Eintrag `selected` `NonTrusted` mit
   Quelle `policy`; dasselbe für `plc_tia_online go_online` tun):
   ohne `trustedCertificates` laden und bestätigen, dass `configurationsSeen` nun einen
   `online_legitimation`-Eintrag `TlsVerificationConfiguration` zeigt und der Abbruch `tls` mit
   lesbarem `plcName`, `verificationInfo` und Zertifikaten (leere `unreadableMembers`) und
   `selectionSet: NonTrusted` trägt; `sha256`/`sha1Thumbprint` mit "Zertifikate anzeigen" von TIA
   Portal vergleichen; mit jedem Fingerabdruck erneut versuchen und `Trusted` (Quelle `caller`) sowie
   den Download bestätigen. Erscheint kein solcher Eintrag, den Fehlschlag und das Verhalten der
   TIA-Portal-Oberfläche festhalten: Die Frage wird nicht über dieses Ereignis gestellt.
   Außerdem festhalten, ob das Vertrauen bestehen bleibt (ein zweiter Download ohne
   `trustedCertificates`).
