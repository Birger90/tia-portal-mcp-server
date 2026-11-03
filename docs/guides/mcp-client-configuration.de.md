# MCP-Client-Konfiguration

**Sprache:** [English](mcp-client-configuration.md) · Deutsch

Referenz für die Konfiguration des Servers in einem MCP-Client, einschließlich der Adressierung von
Bausteinpfaden. Für die Erstinstallation siehe [Installation](installation.de.md).

## Bausteinpfade

Bevorzuge Bausteinpfade, die in den Werten `Details.Path` der Knoten von `browse_project_tree` zurückgegeben werden. Unterstützte Formen von Bausteinpfaden sind:

```text
BlockName
PLC_1/BlockName
PLC_1/Blocks/Folder/SubFolder/BlockName
PLC_1/Units/UnitName/Blocks/Folder/SubFolder/BlockName
```

Die Legacy-Pfade `BlockName` und `PLC_1/BlockName` werden nur akzeptiert, wenn der Bausteinname eindeutig ist. Wenn mehr als ein Baustein denselben Namen hat, verwende den deterministischen `Path`, den `browse_project_tree` zurückgibt.

## MCP-Client-Konfiguration

Konfiguriere deinen MCP-Client so, dass er den Werkzeugbefehl startet:

```json
{
  "mcpServers": {
    "tia-portal": {
      "command": "tia-mcp"
    }
  }
}
```

Ohne explizite Zugriffsargumente startet der Server in `read-write` mit dreißig Werkzeugen: Änderungen (einschließlich Bibliotheksänderungen),
Übersetzen, Simulationssteuerung für S7-PLCSIM Advanced, alle sieben Lifecycle-Werkzeuge, `plc_download` (in PLCSIM oder in echte Hardware),
`plc_online_write` für konfigurierte S7-1500-CPUs, `plc_tia_online` und `plc_station_upload`. Jeder tatsächliche Lifecycle-, Lade-,
Online-Schreib-, Online-Verbindungs- oder Upload-Aufruf und jeder `library_write`-Aufruf mit einem Lifecycle-Element einer globalen Bibliothek fragt einmal über
Form-Elicitation `accept` mit booleschem `confirm:true` nach, auch ohne Guards oder nur mit Info-Guards. Ein Client
ohne diese Capability erhält `access_denied`; Ablehnen, Abbrechen, Timeout oder Transportfehler
verweigern ebenfalls. Setze `"args": ["--access-mode", "full"]` für dieselben dreißig Werkzeuge mit Bestätigung per Richtlinie (policy) für Lifecycle, Laden, Online-Schreibvorgänge,
Online-Verbindungen und Uploads
und ohne Server-Elicitation; full fügt keine Werkzeuge oder Operationen hinzu.
`tia-mcp install` verwendet davon unabhängig standardmäßig read-only mit elf Werkzeugen, einschließlich `bind_project`, `plcsim_read`, `plc_online_read` und `library_read`.
Block-Guards stoppen den Aufruf in jedem Modus. Siehe [Installation](installation.md#access-modes).

Der Zugriffsmodus kann auch ganz ohne `args` gewählt werden: in „TiaMcp Passwortverwaltung“ (`tia-mcp passwords`, Reiter
**Zugriffsmodus**), das die Einstellungsdatei des Servers `%APPDATA%\TiaMcpServer\settings.json` schreibt (oder die Datei, die
`TIA_MCP_SETTINGS` in `env` nennt). Der obige Eintrag bleibt dann ohne `args`. Vorrang beim Start:

1. CLI-Argument in `args` (`--access-mode <mode>`, `--read-only`, `--read-write`),
2. `TIA_MCP_ACCESS_MODE` in `env`,
3. die Einstellungsdatei (`accessMode`),
4. der Standard `read-write`.

Halte `--access-mode` und `TIA_MCP_ACCESS_MODE` aus der Client-Konfiguration heraus, wenn das Programm entscheiden soll. Der
Server liest die Datei einmal beim Start und schreibt sie nie; starte den MCP-Server nach einer Änderung neu (Claude Desktop beenden und neu starten).
Eine ungültige Einstellungsdatei verhindert den Start. Das Startprotokoll und `tia-mcp doctor` nennen die Quelle
(`cli`, `environment`, `settingsFile`, `default`); siehe
[Zugriffsmodus in der Passwortverwaltung wählen](installation.md#choosing-the-access-mode-in-the-password-manager).

Entferne den alten Startschalter für die Bestätigung; er wird abgelehnt mit:

```text
--confirm-with-user was removed. Confirmation follows the access mode: read-write asks for every lifecycle call; use --access-mode full to run lifecycle tools without prompts.
```

Lifecycle-Eingaben verwenden `dryRun`; entferne frühere Bestätigungs-Arrays des Agenten, das alte öffentliche `confirm`
und das frühere Safety-Token-Argument. Lies die strukturierten Ergebnisse `result` und `verification` und die Bestätigung
in Audit v2 als `user`, `policy` oder `none`. Ein Dry Run löst nie eine Elicitation aus, auch wenn ein Client
weiterhin seine Berechtigungsabfrage für destruktive Werkzeuge anzeigen kann. Kein Werkzeug verwendet Safety-Tokens; ersetze
Aufrufer von `preview_write_batch`/`apply_write_batch` durch `plc_write` und übergib `content` plus
`expectedContentHash` (aus `plc_read`) statt `yamlContent`/`sourceContent`.
`plc_write` und Network nehmen `operations` und optional ein boolesches `dryRun=false`; **ein weggelassenes dryRun führt aus**.
Setze `dryRun:true` bei jedem Aufrufer, der eine Vorschau will. Entferne `confirm`, das frühere Safety-Token-Argument, `acknowledge` und
unbekannte Root-Schlüssel: Jeder Fehler in der Form der Argumente gibt das kanonische Dokument des Werkzeugs zurück (`validation_error`, `isError:true`), ohne Audit-Eintrag.
Ablehnungen bei Validierung/Bindung/Guards nach dem Eintritt in die Pipeline verwenden einen kanonischen Root-Fehler und einen Audit-v2-Eintrag.
In keinem der schreibfähigen Modi findet bei `plc_write` oder Network eine Server-Elicitation statt, auch nicht bei Löschungen.
Lies `batch`, `effects`, `verification` und das explizit auf null gesetzte Root-`omission`; ein Zustellfehler kann
neben erfolgreichen Ausführungszusammenfassungen bestehen. Untersuche die ursprünglichen exakten Selektoren, bevor du es erneut versuchst. Halte destruktive Werkzeuge aus den Auto-Approve-Listen des Clients heraus, wenn du
bei jedem Aufruf eine Berechtigungsabfrage verlangst; der Server kann nicht feststellen, ob ein Mensch einen akzeptierten
Elicitation-Dialog gesehen hat. Die [Lifecycle-Referenz](../SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md#lifecycle-operations)
dokumentiert Anfragen, Guards, typisierte Fehler und Wiederherstellung.

Verwende bei `bind_project` die Standard-/Null-`action` oder `action:"bind"`, um in jedem Modus ein bereits geöffnetes Projekt zu übernehmen oder zu ihm zu wechseln; der Wechsel zu
einem anderen konfigurierten oder zuvor gebundenen Pfad erfordert `forceRebind:true`. Das explizite
`action:"list_portals"` erkennt nur; die Inspektionsaktionen für den Project Server verwenden eine persistente
Anbindung wieder und können sie weder wechseln noch ein Projekt übernehmen. Siehe [Multiuser-Inventar](../SupportedOperations/MULTIUSER_OPERATIONS_SUMMARY.de.md)
für exakte Selektoren, PID-Kontinuität, den Sitzungsumfang des aktuellen Benutzers und die dokumentierten Grenzen der Abnahme.
Verwende für lokale Sitzungen den exakt beobachteten `.amc21`-Engineering-Pfad für Bind/Status und für
Zusicherungen bereits geöffneter Projekte mit `--project`/`TIA_MCP_PROJECT_PATH`, wo nötig mit einer exakten Bindungs-PID.
`.als21` und Inventarverzeichnisse sind keine Selektoren für die Übernahme. Ein schreibfähiges `open_project`
akzeptiert eine unabhängig bekannte, exakte, existierende `.als21`-Datei; setze `dryRun:true` für eine Vorschau.
Eine Kaltübernahme meldet eine null-Herkunft des Öffners; ein erfolgreiches Öffnen erfasst sie erst nach der Verifikation.
Inhaltslesevorgänge (`browse_project_tree`, `plc_read`, `network_read`, `read_cross_references`,
`hmi_read`), `plc_write`, `network_write`, `hmi_write`, `compile_check` und `save_project` funktionieren auf einer gebundenen
lokalen Sitzung. Ein lokales `save_project` speichert nur die Sitzung und checkt nie in den Project
Server ein; Anlegen, Speichern unter, Archivieren und Schließen bleiben `unsupported_capability`. Untersuche nach einem unsicheren
Öffnen den AMC-Status und das Portal-Inventar; nie automatisch wiederholen. Das Öffnen offline in read-write
erforderte im eingegrenzten
[PR4-Lauf](../superpowers/acceptance/reports/2026-10-08-multiuser-pr4-live-verification.md) vom Bediener weggeklickte Siemens-Dialoge.
Lass den Pfad bei der Standard-Bindungsaktion weg, um
das einzige geöffnete Projekt zu wählen oder Kandidaten aufzulisten. Das erneute Anhängen kann eine menschliche Antwort auf den
Openness-Zugriffsdialog von TIA erfordern. Die Worker-Besitzerschaft geht beim Detach verloren. Nur `open_project`, `create_project` und `retrieve_project`
öffnen Projekte; gewöhnliche Lesevorgänge öffnen, binden oder wechseln nie. Read-only öffnet, erstellt, speichert oder
schließt nie. Ein Projektbaum-Cursor, dessen Bindung sich geändert hat, gibt `cursor_binding_mismatch` zurück; beginne das Durchsuchen
ohne ihn neu.

Für die lokale Entwicklung ohne Installation des Werkzeugs richte den Client auf `dotnet`:

```json
{
  "mcpServers": {
    "tia-portal-dev": {
      "command": "dotnet",
      "args": ["run", "--project", "{REPO PATH}\\TiaMcpServer"]
    }
  }
}
```

Mit einer expliziten Projektbindung:

```json
{
  "mcpServers": {
    "tia-portal-dev": {
      "command": "dotnet",
      "args": ["run", "--project", "{REPO PATH}\\TiaMcpServer", "--", "--project", "C:\\Projects\\Sandbox\\Line.ap21"]
    }
  }
}
```

### Passwörter für den PLC-Download

`plc_download` beantwortet Passwortkonfigurationen (zum Beispiel `ModuleWriteAccessPassword` einer
schreibgeschützten CPU) nur aus Umgebungsvariablen des Serverprozesses, die der
Openness-Worker erbt. Lege das Passwort im `env`-Block des Clients ab und übergib im Werkzeugaufruf nur den **Namen** der Variablen,
zum Beispiel `"passwords": { "ModuleWriteAccessPassword": "PLC_WRITE_PW" }`:

```json
{
  "mcpServers": {
    "tia-portal": {
      "command": "tia-mcp",
      "env": { "PLC_WRITE_PW": "<the password>" }
    }
  }
}
```

Starte den Server nach einer Änderung neu. Das Passwort erscheint nie in Werkzeugergebnissen, Logs oder dem
Audit; nur der Variablenname. Variablennamen verwenden ASCII-Buchstaben, Ziffern und Unterstriche und beginnen
nicht mit einer Ziffer. Eine nicht gesetzte Variable wird abgelehnt, bevor irgendetwas geladen wird. Siehe die
[PLC-Download-Referenz](../SupportedOperations/PLC_DOWNLOAD_SUMMARY.md#passwords).

## Grenzen der Antwortgröße

Strukturierte Werkzeugergebnisse sind auf **60.000 Zeichen pro Wert** (ein `plc_read`-Baustein,
ein `network_read`-Ergebnis, ein Schreib-Effekt) und **180.000 Zeichen pro vollständiger Antwort** budgetiert.
Ein Wert über seinem Budget wird als Ganzes weggelassen, mit `reason`, `limitChars`, `originalChars` und Hinweisen
zum erneuten Versuch; er wird nie abgeschnitten. Die Größen werden am escapten JSON gemessen, das der Client erhält, daher zählt ein SCL-
oder XML-Dokument etwas größer als seine Datei.

Ein Baustein- oder Typexport über dem Wertbudget kommt daher ohne `content` oder
`contentHash` zurück, und `update_block_logic`/`update_type_content` können darauf nicht angewendet werden. Erhöhe beide
Budgets beim Serverstart:

| Einstellung | Kommandozeile | Umgebungsvariable | Standard |
| --- | --- | --- | --- |
| Wertbudget | `--max-item-chars <n>` | `TIA_MCP_MAX_ITEM_CHARS` | 60.000 |
| Antwortbudget | `--max-document-chars <n>` | `TIA_MCP_MAX_DOCUMENT_CHARS` | 180.000 |

```json
{
  "mcpServers": {
    "tia-portal": {
      "command": "tia-mcp",
      "args": ["--access-mode", "read-write", "--max-item-chars", "100000", "--max-document-chars", "250000"]
    }
  }
}
```

Dieselben Werte können stattdessen im `env`-Block des Clients gesetzt werden; ein Wert auf der Kommandozeile hat Vorrang
vor seiner Umgebungsvariablen. Regeln:

- Grenzen können nur erhöht werden: Jeder Wert muss zwischen seinem Standard und 1.000.000 liegen. Beide
  Formen `--name n` und `--name=n` werden akzeptiert, jede höchstens einmal.
- Setze das Wertbudget auf oder über das `originalChars` der Auslassung.
- Halte das Antwortbudget über dem Wertbudget, mit Spielraum für den Antwort-Envelope
  (Operations-IDs, Zählungen, Warnungen). Bei gleichen Grenzen kann ein Wert knapp unter der Grenze trotzdem
  als `responseExceededDocumentCharLimit` weggelassen werden. Lies einen großen Baustein pro `plc_read`-Aufruf.
- Ein ungültiger Wert stoppt den Server beim Start mit einem Fehler; die wirksamen Budgets werden
  auf der Standardfehlerausgabe als `TIA MCP response budget: ...` protokolliert.
- `tia-mcp install` und `tia-mcp doctor` schreiben oder melden diese Einstellungen nicht; bearbeite die
  Client-Konfiguration von Hand.

Das Erhöhen der Server-Budgets erhöht keine Grenzen, die der MCP-Client selbst durchsetzt, wie etwa eine
maximale Größe von Werkzeugergebnissen, und ein Inhalts-Schreibvorgang sendet weiterhin das ganze Dokument als ein
Werkzeugargument. Feste Grenzen sind nicht betroffen: Projektbaum-Seiten, Hardware-Seiten von `network_read` und der
Bericht von `compile_check` bleiben bei 60.000 Zeichen.

## PLC-Web-API-Ziele

`plc_online_read` und `plc_online_write` erreichen nur die S7-1500-CPUs, die in einer Zieldatei aufgeführt sind, die du
schreibst; die KI nennt ein Ziel, nie eine Adresse oder Anmeldedaten. Richte den Server mit einem
absoluten Pfad auf die Datei und lege die Anmeldedaten in der Umgebung des Servers ab, unter den Variablennamen, die die Datei
deklariert:

```json
{
  "mcpServers": {
    "tia-portal": {
      "command": "tia-mcp",
      "args": ["--access-mode", "read-write", "--plc-webapi-config", "C:\\TiaMcp\\plc-webapi.json"],
      "env": {
        "TIA_LINE1_WEBAPI_USER": "mcp-operator",
        "TIA_LINE1_WEBAPI_PASSWORD": "<password of that web server user>"
      }
    }
  }
}
```

| Einstellung | Kommandozeile | Umgebungsvariable | Standard |
| --- | --- | --- | --- |
| Zieldatei (absoluter Pfad) | `--plc-webapi-config <path>` | `TIA_MCP_PLC_WEBAPI_CONFIG` | keine (die Online-Werkzeuge antworten `target_not_found`) |

Die Kommandozeile hat Vorrang vor der Umgebung. Die Datei wird einmal beim Start gelesen, und eine ungültige Datei
verhindert den Start; prüfe sie vorher mit `tia-mcp doctor --plc-webapi-config <path>`. Die Standardfehlerausgabe beim Start meldet
`TIA MCP PLC Web API targets: <n> target(s) from <path>`. Anmeldedaten werden aus den benannten Variablen gelesen,
wenn sich der Server anmeldet, also halte sie aus der Datei, aus Prompts und aus der Versionsverwaltung heraus; ein
Client, der `env` in einer gemeinsam genutzten Konfigurationsdatei speichert, speichert dort auch das Passwort. Verwende einen
Webserver-Benutzer, der nur die Rechte hat, die die KI haben soll. Dateiformat, Modi der Zertifikatsprüfung,
Voraussetzungen in TIA Portal und das Bestätigungsmodell stehen in der
[PLC-Online-Referenz](../SupportedOperations/PLC_ONLINE_WEBAPI_SUMMARY.de.md).

## Worker-Timeout

Der Host wartet standardmäßig bis zu fünf Minuten auf jede Worker-Anfrage. Erhöhe dies beim
Serverstart für Operationen, die mehr Zeit benötigen:

| Einstellung | Kommandozeile | Umgebungsvariable | Standard |
| --- | --- | --- | --- |
| Worker-Timeout (Minuten) | `--worker-timeout-minutes <n>` | `TIA_MCP_WORKER_TIMEOUT_MINUTES` | 5 |

Verwende ganze Minuten von **5 bis einschließlich 60**; die Einstellung kann nur erhöht werden. Sowohl
`--worker-timeout-minutes 15` als auch `--worker-timeout-minutes=15` werden akzeptiert. Der Wert wird
einmal beim Start geparst: Die Kommandozeile hat Vorrang vor der Umgebung, danach gilt der Standard.
Ungültige Werte verhindern den Start mit einem Fehler, statt stillschweigend auf einen Ersatzwert zurückzufallen.

Ein Wert gilt für jede Worker-Anfrage, einschließlich des `hello` beim Start. Die Standardfehlerausgabe beim Start
meldet `TIA MCP worker timeout: <n> minutes`; `doctor` meldet
`Worker timeout: <n> minutes (<source>)`, wobei die Quelle `default`, `option` oder
`environment` ist. Ein höherer Wert verzögert auch die Erkennung eines Workers, der während `hello` oder
des Starts hängt. Es gibt keine Überschreibung pro Methode und keine Fortschrittsbenachrichtigungen.

Die Server-Einstellung kann die eigenen Grenzen des MCP-Clients nicht überschreiben. Das
[MCP TypeScript SDK](https://ts.sdk.modelcontextprotocol.io/v2/api/index/@modelcontextprotocol/client/)
verwendet standardmäßig ein Anfrage-Timeout von 60 Sekunden, sofern der Aufrufer es nicht erhöht; das ist kein universeller
Standard von MCP-Clients. Claude Code stellt
[`MCP_TOOL_TIMEOUT`](https://code.claude.com/docs/en/env-vars) in Millisekunden bereit. Gib dem Client
Spielraum über dem Server-Timeout und prüfe etwaige separate Transport- oder Leerlaufgrenzen.

Ein bereits an den Worker übergebener Aufruf kann weiterlaufen, nachdem der Client ihn aufgegeben hat.
Das Lifecycle-/Schreib-Audit behält sein Ergebnis; untersuche das Audit und den aktuellen Projektzustand,
bevor du eine Änderung erneut versuchst.
