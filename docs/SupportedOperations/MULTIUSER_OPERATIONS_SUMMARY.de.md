# Multiuser: lokale Sitzungen, Inventar und Abnahmegrenze

**Sprache:** [English](MULTIUSER_OPERATIONS_SUMMARY.md) · Deutsch

`bind_project` stellt in allen Modi eine explizite Portal-Ermittlung und fünf Inventaraktionen für den Project Server
bereit. Weggelassenes/null `action` oder `action:"bind"` wählt bereits geöffnete Standalone-Besitzer (`.ap21`)
oder typisierte Besitzer lokaler Sitzungen (`.amc21`) aus. Die aktuelle Discovery umfasst **13/33/33** Tools in
read-only/read-write/full. PR4 fügt in schreibfähigen Modi das explizite Öffnen existierender `.als21` und
einen grundlegenden lokalen Status hinzu; PR5 fügt lokale Inhaltslesevorgänge hinzu und PR6 lokale Inhaltsschreibvorgänge, Übersetzen und
`LocalSession.Save()` (siehe [Interner Kontext](#interner-kontext)). Die Besitzverträge aus PR1/PR2 bleiben die Grundlage; Issue #65 ist unvollständig.
PR 3 hat eine [Live-Abnahme mit dem installierten Tool](../superpowers/acceptance/reports/2026-10-06-multiuser-pr3-live-verification.md)
für alle sechs Inspektionsaktionen gegen autorisierte Sitzungskopien und eine Standalone-`.ap21`,
einschließlich entfernter Lesevorgänge mit null geöffneten Projekten. Der Maintainer akzeptierte die Live-Tests am 6. Oktober als abgeschlossen
und bestanden; der Bericht behält nicht blockierende, nicht ausgeführte Fälle als Grenzen der Belege.

## Inspektionsaktionen und Selektoren

Ein Aufruf führt eine Aktion aus. Aktionsnamen und Server-Alias, Gruppenname und Projektname werden
ordinal-exakt abgeglichen, ohne Trimmen oder Angleichen der Groß-/Kleinschreibung. Null Treffer liefern `target_not_found`;
mehrere Treffer liefern `target_ambiguous`, ohne Rückfall auf das erste Ziel.

| `action` | Zulässige Selektoren | Ergebnis |
| --- | --- | --- |
| `list_portals` | Keine | Frische Kandidaten in `result.value.portals` (mit `otherClientCount`); hängt sich nie an ein gelistetes Portal an und übernimmt nie das einzige geöffnete Projekt. |
| `list_server_connections` | Optional `portalProcessId` | Konfigurierter Alias, Host und Port; die Konfiguration beweist keine Erreichbarkeit/Authentifizierung. |
| `list_server_groups` | Optional PID; erforderlich `serverAlias` | Gruppen und Verbindungsbeobachtung des exakten Endpunkts. |
| `list_server_projects` | Optional PID; erforderlich Alias und `group` | Projekte innerhalb der exakten Root-Gruppe oder der benannten Gruppe. |
| `list_local_sessions` | Optional PID; erforderlich Alias, Gruppe und `serverProjectName` | Ganzzahlige `sessionId` (keine Einschränkung auf positive Werte) und kanonische Pfade auf diesem Rechner für den aktuellen Benutzer. |
| `get_lock_state` | Optional PID; erforderlich Alias, Gruppe und Projekt | Beobachtetes Flag, nullbarer Besitzer und Zeitstempel. |

Eine explizit angegebene PID muss eine positive ganze Zahl sein, nicht null, Text oder ein Bruch. Die Root-Gruppe
ist `{"isRoot":true,"name":null}`; eine benannte Gruppe ist `{"isRoot":false,"name":"Exact name"}`.
Beide Gruppen-Member sind erforderlich, einschließlich des expliziten Root-`name:null`. Unbekannte Member, leere
Namen, fehlende Selektoren und aktionsfremde Schlüssel werden vor jeder Worker-Aktivität abgelehnt. Inspektionen lehnen
angegebene Schlüssel `projectPath`/`forceRebind` ab, selbst wenn null/false. `list_portals` akzeptiert nur `action`;
`bind` lehnt Selektoren des Server-Inventars ab; eine optionale exakte Portal-PID ist beim Binden erlaubt.

```json
{ "action": "list_portals" }
```

```json
{ "action": "list_server_connections", "portalProcessId": 1234 }
```

```json
{ "action": "list_server_groups", "portalProcessId": 1234, "serverAlias": "Fixture" }
```

```json
{
  "action": "list_server_projects",
  "portalProcessId": 1234,
  "serverAlias": "Fixture",
  "group": { "isRoot": true, "name": null }
}
```

```json
{
  "action": "list_local_sessions",
  "portalProcessId": 1234,
  "serverAlias": "Fixture",
  "group": { "isRoot": false, "name": "Exact group" },
  "serverProjectName": "Exact project"
}
```

```json
{
  "action": "get_lock_state",
  "portalProcessId": 1234,
  "serverAlias": "Fixture",
  "group": { "isRoot": false, "name": "Exact group" },
  "serverProjectName": "Exact project"
}
```

## Attachment und Kontinuität des Bindings

Die Ermittlung hängt sich nicht an. Das Inventar verwendet ein gesundes persistentes Attachment wieder. Ohne Attachment
wählt es die exakt angeforderte PID oder verlangt genau einen frischen Kandidaten und hängt sich dann an, ohne
ein Projekt zu übernehmen. Eine Inspektion kann ein bestehendes Attachment nicht wechseln. Ein verifiziertes Binding setzt
die PID-Zusicherung standardmäßig auf seine verifizierte PID; eine explizite fremde PID liefert ein lokales `binding_conflict`
vor dem Dispatch und erhält dabei Binding-Revision und Cursor. Eine ungebundene/konfigurierte, nicht verifizierte
Sitzung bleibt es nach einer gesunden Inspektion; konfigurierte oder zuletzt gebundene Pfade/PIDs werden nicht hochgestuft.

Eine Inspektion öffnet, übernimmt, wechselt, speichert, schließt, verwirft oder committet nie ein Projekt/eine Sitzung.
Es gibt kein temporäres Abhängen, keinen zweiten Worker, keine Lifecycle-Elicitation und kein Schreib-Audit. Ein
Openness-Attachment kann weiterhin den von einem Menschen zu beantwortenden Zugriffsdialog von TIA erfordern. Ein gewöhnlicher Server-/Selektorfehler
erhält einen gesunden Kontext und den Besitz. Ein echter Verlust der Portal-/Projektidentität, ein Absturz oder
Timeout des Workers macht Binding-Belege und Cursor ungültig. Das gilt auch für einen Identitätsverlust, der von
`list_portals` oder einer ergänzenden Kandidatenauflistung beobachtet wird; `isBound` spiegelt den resultierenden Snapshot wider.
Ein fehlgeschlagenes Inventar wird nie automatisch wiederholt.
Explizites `bind` mit seinen bestehenden Guards verwenden, um eine andere Instanz auszuwählen.

Eine fatale `NonRecoverableException` aus dem Inventar gelangt zur gemeinsamen
`portal_lost`-Zuordnung des Workers; abschließende Attachment-Prüfungen können die ursprüngliche fatale
Ausnahme des Rumpfs nicht ersetzen. Gewöhnliche Serverfehler erhalten weiterhin ein gesundes Binding, und
bestehende Ablehnungen nur wegen Identität/Disposal behalten `binding_conflict`. Der
[B-16/Q3-Bericht](../superpowers/acceptance/reports/2026-10-20-b16-q3-worker-portal-loss-live-verification.md) verzeichnet
Offline-Abdeckung und vom Maintainer akzeptierte Live-Lesetests im festgelegten Umfang. Eine gezielte Injektion nativer
Inventarfehler bleibt ungetestet; frühere Beobachtungen lokaler Sitzungen
sind auf den aufgezeichneten Kandidaten und die Fixtures beschränkt.

## Typisierte Ergebnisse und Grenzen der Beobachtung

Der [Binding-Umschlag](PROJECT_OPERATIONS_SUMMARY.md#explicit-binding-bind_project) bleibt
Vertragsversion `1.0`, mit einem kanonischen Text-/Strukturdokument. Eine Inspektion liefert
`transition:"none"`, `project:null`, Binding-Snapshots vorher/nachher und das bedingte
`result.value.inspection`. Das Standard-Binding lässt `inspection` weg. Erforderliche Member der Inspektion sind
`action`, die nullbare beobachtete `portalProcessId` und fünf explizit nullbare Slots:
`serverConnections`, `serverGroups`, `serverProjects`, `localSessions`, `lockState`. Ein erfolgreiches
Inventar füllt genau den Slot seiner Aktion und meldet die tatsächlich angehängte PID. Die Ermittlung verwendet
`portals` und füllt keinen Slot; eine fehlgeschlagene Inspektion füllt keinen Slot. Ungültige Eingaben/lokale Ablehnung haben
einen Fehler auf oberster Ebene und MCP `isError:true`; ein abgeschlossener Fehlschlag behält ein typisiertes fehlgeschlagenes Ergebnis mit
`error:null`, `isError:false`. Ein fehlerhafter/inkohärenter Worker-Payload wird zu einem bereinigten `protocol_error`,
ohne den Payload wiederzugeben.

Sitzungen deklarieren `scope:"currentMachineCurrentUser"`. Eine leere erfolgreiche Liste belegt keine
Abwesenheit für andere Benutzer/Rechner; eine fehlgeschlagene/unvollständige Aufzählung ist nie ein leerer Erfolg. Das Lesen von Sperren
ist nicht atomar: Beobachtungen ohne Sperre haben `owner:null`; Lesevorgänge mit Sperre holen den Besitzer und
prüfen das Flag erneut. Ein geändertes Flag scheitert mit `state_changed`; Erfolg ist kein Erwerb einer Sperre und keine
Garantie eines fortbestehenden Zustands. Verbindungsbeobachtungen sind beschreibend, keine Autorisierung oder kein Beweis
künftiger Erreichbarkeit. Generische Multiuser-Fehler belegen keinen Authentifizierungsfehler/keine Nichtverfügbarkeit des
Servers. Der Verlauf vergleicht nur dasselbe tatsächliche Attachment und denselben Endpunkt und wird nach
Änderungen von Attachment/Konfiguration zurückgesetzt.

Der `projectPath` jeder Sitzung ist das normalisierte `LocalSessionInfo.ProjectFileInfo.FullName`,
das Siemens liefert. Die autorisierte Live-Fixture lieferte Sitzungsverzeichnisse statt
`.als21`-Dateipfaden. Dieses Feld als Inventarbeleg behandeln; es ist kein garantierter Selektor zum Öffnen oder
Binden, und PR 3 ermöglicht weder das Öffnen noch die Übernahme von `.als21`.
PR4 akzeptiert für `open_project` eine unabhängig bekannte, exakt existierende `.als21`-Datei; es leitet
diesen Dateinamen nie aus diesem Inventarfeld ab.

Standalone-Budgets sind 60.000 Zeichen pro ganzem Wert und 180.000 pro vollständigem Dokument.
Ein übergroßes Inventar wird als Ganzes weggelassen, einschließlich `inspection`, mit Auslassungs-Metadaten und dem Hinweis,
eine engere zutreffende exakte Gruppe/ein engeres Projekt zu inspizieren oder TIA Portal zu verwenden. Arrays werden nie stillschweigend
gekürzt; die Wiederholung derselben übergroßen Anfrage kann das Limit weiterhin überschreiten.

## Interner Kontext

`TiaPortalSession` speichert einen `ActiveProjectContext`, dessen `ProjectBase`-Root von seinem
typisierten Besitzer stammt. `StandaloneProjectOwner` behält ein `Project`; `LocalSessionOwner` behält eine
`LocalSession` und ihr `MultiuserProject`. Die produktive Auswahl löst den exakten typisierten Besitzer
und seinen absoluten `.amc21`-Engineering-Pfad auf. Der grundlegende lokale Status und das explizite Öffnen von `.als21`
verwenden diesen Besitzer. Die Inhaltslesevorgänge aus PR5 (`browse_project_tree`, `plc_read`, `network_read`,
`read_cross_references`, `hmi_read`) laufen über den gemeinsamen `ProjectBase`-Root gegen das `MultiuserProject` der Sitzung
und werden als `projectContent` angeboten. PR6 fügt `plc_write` (alle
Operationen), `network_write` (alle Operationen), `compile_check` und `save_project` auf demselben Root hinzu.
Die neun `hmi_write`-Operationen verwenden ebenfalls diesen lokalen Root; siehe die [HMI-Referenz](HMI_OPERATIONS_SUMMARY.de.md)
und die [Schreib-Abnahme in beiden Modi](../superpowers/acceptance/reports/2026-10-14-hmi-write-live-acceptance.md).
Ein lokales `save_project` ruft `LocalSession.Save()` auf: Es persistiert nur die lokale Sitzung und
aktualisiert nie vom Project Server oder checkt dort ein; die Bestätigung in read-write sagt das. Anlegen,
Speichern unter, Archivieren, generisches Schließen und die internen Qualifizierungsproben für IO-Systeme/Subnetze werden mit
`unsupported_capability` vor der Mutation abgelehnt, auch Vorschauen; Kontexte von Serverprojekten liefern keine
Inhaltsoperation. Der lokale Besitzer stellt keine Methode zum Schließen, Verwerfen oder Committen bereit.

Schreibvorgänge wirken nur auf die lokale Sitzung. Sie prüfen keine Markierungen oder Sperren anderer Benutzer, sodass eine
konfligierende Serveränderung erst bei einem späteren Aktualisieren oder Einchecken sichtbar wird.

Lesevorgänge beobachten nur die lokale Sitzung. Sie melden keine Markierungen anderer Benutzer, keine Aktualität und keinen
Zustand veralteter Objekte, und sie belegen keine Erreichbarkeit des Project Servers. Der
[PR5-Live-Bericht](../superpowers/acceptance/reports/2026-10-09-multiuser-pr5-live-verification.md)
verzeichnet eine kalt übernommene lokale Sitzung (gemeldeter Modus und Verbindung `unknown`) und eine
Standalone-Regression; bewusst als Exclusive oder offline geöffnete Sitzungen wurden nicht durchgespielt. Der
[Abschlussbericht zu hmi_read](../superpowers/acceptance/reports/2026-10-12-hmi-read-completion-live-acceptance.md)
ergänzt Cursor-Paging von `hmi_read` auf einer kalt übernommenen lokalen Sitzung: Fortsetzungen verwendeten den
Worker-Snapshot über Aufrufe hinweg wieder. Der vorläufige `read_cross_references`-Selektor `{hmi}` wurde nur auf
Standalone-Projekten verifiziert. Der
[PR6-Live-Bericht](../superpowers/acceptance/reports/2026-10-09-multiuser-pr6-live-verification.md)
verzeichnet jeden Schreib-, Übersetzungs- und Speicherpfad auf kalt übernommenen Multiuser- und Exclusive-Sitzungen im
Modus `full`. Bestätigung in read-write, vom Worker geöffnete und Offline-Sitzungen wurden nicht durchgespielt.

Die Identität bleibt eine Projektion der maßgeblichen Sitzung. Das erneute Umhüllen desselben Roots
erhöht die Generation nicht; ein Ersatz-Root oder eine akzeptierte Pfadänderung tut es. Abhängen gibt
den Besitz frei, und eine Übernahme stellt ihn nie wieder her. Das Herunterfahren gibt Portal-Ressourcen frei, ohne
ein Speichern des Projekts, ein Verwerfen der Sitzung oder einen Revisions-Commit zu beanspruchen. Siehe
[Architektur](../ARCHITECTURE.md#internal-active-project-context).

Der lokale Status fügt das bedingte `context` hinzu; Standalone-/geschlossener Status lässt es weg. Sein grundlegender Status
hat `metadata:null`. Der Kontext enthält beschreibende Capabilities, den exakten `engineeringProjectPath`,
den nullbaren `sessionContainerPath` und `openedByWorker`. Eine kalte Übernahme verzeichnet keine ALS-Herkunft
und keinen Worker-Besitz. Ein erfolgreicher exakter ALS-Öffner verzeichnet die Herkunft erst nach der Verifikation des typisierten
Besitzers/Roots; eine bekannte Wiederverwendung derselben ALS braucht eine durchgehende Besitzeridentität. Eine Aktualisierung der Beobachtung
beim selben Besitzer erhält Generation/Binding-Revision; der Ersatz des Besitzers am selben Pfad
macht die Identität ungültig. Abhängen gibt Besitz und bekannte Öffner-Herkunft auf.

Das Produkt meldet `sessionMode:"unknown"`, `remoteIdentity:null` und eine Beobachtung der aktiven Verbindung
von `unknown`. Anlagemodi der Fixtures und die Zuordnung von Endpunkten sind unabhängige Belege des Bedieners;
Dateinamen, Verzeichnisse und Inventar-IDs können keinen nicht unterstützten umgekehrten Join füllen.
Explizite Inventarbeobachtungen von Endpunkten haben ihren eigenen Attachment-/Endpunktverlauf und beweisen nicht
die Erreichbarkeit der aktiven Sitzung, Aktualität, Markierungen oder entfernte Sitzungsidentität. Capabilities
beschreiben die Anwendbarkeit; Zugriffsmodi und abgesicherte Ausführung entscheiden weiterhin über die Autorisierung.

Worker-eigene lokale Quellen blockieren ein anderes Öffnen mit
`local_session_requires_terminal_operation`, selbst wenn sie sauber sind. Geliehene Quellen erfordern einen nachgewiesenen
Erhalt bei Koexistenz/Duplikat; Unsicherheit blockiert mit
`local_session_source_preservation_unproved`. Force kann keinen der beiden Guards übersteuern. Generisches
`close_project` ist keine abschließende Operation lokaler Sitzungen; das Herunterfahren gibt Portal-Ressourcen frei,
ohne Speichern, Verwerfen oder Commit der Sitzung. Siehe die [Projektreferenz](PROJECT_OPERATIONS_SUMMARY.md#local-session-identity-and-basic-status).

## Noch ausstehende öffentliche Multiuser-Arbeit

`get_session_state`, `get_markings`, Markieren/Entmarkieren, sperrbewusste Vorbedingungen für Schreibvorgänge, Verwerfen,
Commit sind weiterhin nicht geliefert. Die Serververwaltung (Verbindungen, Hochladen von Projekten, Anlegen und Löschen lokaler Sitzungen) ist das eigene Portal-Tool `project_server_write` (nicht live getestet; [Referenz](PROJECT_SERVER_WRITE_SUMMARY.de.md)); `bind_project` bleibt lesend. `.als21` bleibt eine Eingabe zum Öffnen,
nie eine Zusicherung für Bind/Status/Start; diese verwenden die beobachtete `.amc21`-Identität. Das Inventar
öffnet/bindet nicht implizit, um eine Vorbedingung eines geöffneten Projekts zu erfüllen; Live-Belege des aktuellen PR
müssen die Vorbedingung und den Umfang feststellen. Ein generischer Standalone-Lebenszyklus kann keine synthetische
lokale/Server-Quelle ersetzen. Spätere PRs müssen jeweils Capability, typisierte Verträge,
abgesicherte Ausführung und operationsspezifische Live-Belege feststellen, bevor sie ihre öffentlichen Pfade freischalten.

## Abnahmegrenze

Der PR4-Implementierungskandidat `3772edad0ce9e90390bc36deb71dbd8140511424` wurde offline qualifiziert
und vom Maintainer am 8. Oktober **im dokumentierten Umfang** abgenommen. Die
[Definition](../superpowers/acceptance/2026-10-07-multiuser-pr4-live-definition.md) behält die
ursprüngliche Matrix; der [Live-Bericht](../superpowers/acceptance/reports/2026-10-08-multiuser-pr4-live-verification.md)
verzeichnet 73 installierte öffentliche Aufrufe mit kanonischer Gleichheit und 21 exakte Übereinstimmungen im Lifecycle-Audit.
Online-Wiederherstellung/-Öffnen bestand in zwei beibehaltenen UI-Portalen in full/read-write. Offline-Vorschauen in read-write
bestanden; beide tatsächlichen Öffnungen wurden erst nach vom Bediener geschlossenen Siemens-Dialogen abgeschlossen, sodass
die ursprüngliche Erwartung eines nicht interaktiven Offline-Ablaufs scheiterte. Offline im Modus full, headless/Race und
andere Lücken der Matrix bleiben unausgeführt. Das ist eine Maintainer-Abnahme im festgelegten Umfang, nicht alle L01–L19 PASS.
Erneute Tests von read-only und des allgemeinen Standalone-Lebenszyklus wurden vom Maintainer ausdrücklich ausgeschlossen.
Kein Speichern, Commit, generisches lokales Schließen, automatisches Aufräumen oder Replay war Teil dieses Laufs.

Mit dem Quellcode verknüpfte In-Memory-Siemens-Doubles spielen die tatsächliche Kontext-/Sitzungsquelle offline durch;
FakeWorker-Tests belegen das Host-Protokoll, Binding, Bestätigung und Audit-Verhalten. Diese
Prüfungen können das Laufzeitverhalten des installierten Openness nicht beweisen. Generierte Referenzen sind nur zum Kompilieren;
das Kompilieren gegen installierte Referenzen und der Ausschluss aus dem Paket sind separate Qualifizierungsprüfungen.

Der [PR-3-Offline-Bericht](../superpowers/acceptance/reports/2026-10-06-multiuser-pr3-offline-validation.md)
verzeichnet den kombinierten Kandidaten und die Qualifizierung. Der
[Live-Bericht mit dem installierten Tool](../superpowers/acceptance/reports/2026-10-06-multiuser-pr3-live-verification.md) vom 6. Oktober
verzeichnet die autorisierte Fixture mit drei Sitzungen: Alle sechs Inspektionsaktionen waren erfolgreich, während der Host
ungebunden blieb, einschließlich der Sitzungsverzeichnisse des aktuellen Benutzers und Beobachtungen mit/ohne Sperre.
Das Inventar wurde mit geöffneten `.als21`-Sitzungen durchgespielt. Die separate Phase mit null Projekten/Standalone-`.ap21`
zeigte alle fünf Inventare ohne geöffnete `.als21`, sowohl mit einem geöffneten Standalone-Projekt
als auch mit null geöffneten Projekten. Gesundes verifiziertes Binding/Cursor, Ungültigwerden bei Worker-Verlust und
explizite Wiederherstellung wurden beobachtet. Tatsächliche Fälle benannter/doppelter Gruppen/Projekte, leere Sitzungslisten,
Live-Hosts in read-only/read-write, abgesichertes Wechseln zu einem zweiten Standalone-Ziel und andere Grenzen des Berichts
bleiben ungetestet. Am 6. Oktober akzeptierte der Maintainer die Live-Tests von PR 3 als abgeschlossen und
bestanden und schloss damit dessen Abnahme-Gate mit diesen nicht blockierenden Grenzen der Belege. Diese Entscheidung
beansprucht nicht, dass die nicht ausgeführten Fälle liefen, und schaltet keine Nachfolgeoperationen frei. Eine historische
Standalone-Abnahme ist keine Inventarabnahme.

Der eingefrorene PR-2-Kandidat `a47bf0dbc3efba4ff654464b6393d01163dc7900` bestand eine begrenzte
Standalone-Live-Abnahme von Auswahl/Wechsel und Lebenszyklus, einschließlich Fällen mit headless letztem Client und widrigem Zustand:
131 aufgezeichnete Tool-Aufrufe, 64 Lifecycle-Aufrufe, übereinstimmende kanonische Dokumente/Audits und
wiederhergestellte Fixtures. Der [Abnahmebericht](../superpowers/acceptance/reports/2026-10-05-multiuser-pr2-active-project-context-live.md)
verzeichnet exakte Belege, Fehler und Grenzen: Verschmutzung durch Projektkommentare, unabhängige externe
Openness-Änderungen und skriptgesteuerte Elicitation. Diese Ergebnisse qualifizieren das Standalone-Verhalten der internen Migration;
sie qualifizieren keine öffentlichen Multiuser-Operationen. Der
[Implementierungsplan](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-09-28-multiuser-pr1-reference-contract-foundation.md) legt diese Grenze fest.
