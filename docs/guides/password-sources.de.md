# Passwortquellen (Umgebungsvariablen und Windows Credential Manager)

**Sprache:** [English](password-sources.md) · Deutsch

Kein Werkzeug nimmt ein Passwort entgegen. Jede Passworteingabe nennt, wo das Passwort liegt:

| Eingabe | Werkzeuge |
| --- | --- |
| `passwords` (Objekt: Kurzname des Konfigurationstyps -> Quelle) | `plc_download`, `plc_tia_online` (`go_online`), `plc_station_upload` |
| `passwordVariable` (eine Quelle) | `plc_write` `safety_login`, `set_safety_password`, `revoke_safety_password`, `protect_plc_confidential_data`, `change_plc_confidential_data_password` (aktuelles), `disable_plc_confidential_data_protection` (optional) |
| `newPasswordVariable` (eine Quelle) | `plc_write` `change_plc_confidential_data_password` |
| `accessLevelPasswords` (Stufe → Quelle) | `plc_write` `set_plc_access_level` |
| `passwords` (Typ → Quelle) in einem `plc_read`-Element | `compare_to_online` |

Eine Quelle ist eine der folgenden:

| Form | Bedeutung |
| --- | --- |
| `"PLC1_PW"` oder `{ "env": "PLC1_PW" }` | eine Umgebungsvariable des Serverprozesses (in der MCP-Client-Konfiguration unter `env` setzen; der Worker erbt sie). Der einfache String ist die ursprüngliche Form und bleibt gültig. |
| `{ "credential": "TiaMcp/<project>/<PLC>/<purpose>" }` | eine **generische** Anmeldeinformation des Windows Credential Managers, gelesen mit dem dokumentierten Win32-`CredReadW` (advapi32, `CRED_TYPE_GENERIC`), und zwar erst beim Aufruf |

Eine gemeinsame Regel (`PasswordSources` in Contracts) bedient alle Eingaben. Der Host prüft die Form und schwärzt, während
die Anfrage gelesen wird: Ein Wert, der weder ein Variablenname noch ein `TiaMcp/`-Ziel ist, wird zu einem festen Platzhalter und
wird abgelehnt, ohne wiedergegeben zu werden; eine nicht gesetzte Umgebungsvariable wird ebenso abgelehnt. Credential-Ziele müssen mit
dem aktiven Präfix beginnen, standardmäßig `TiaMcp/` (siehe unten; höchstens 256 Zeichen, keine Steuerzeichen und kein `*`), damit der Server nicht auf eine fremde
Anmeldeinformation des Benutzers gerichtet werden kann. Der Worker löst die Quelle in einen schreibgeschützten `SecureString` auf; ein Credential-Blob (UTF-16LE, wie
`cmdkey /pass` ihn speichert) wird Zeichen für Zeichen kopiert und dann vor `CredFree` mit Nullen überschrieben. Antworten, Logs und das Audit
erfassen nur das Quell-Token (`credential:TiaMcp/...` oder den Variablennamen), niemals einen Wert. Auf einem anderen Betriebssystem
als Windows schlägt eine Credential-Quelle mit einer klaren Meldung fehl (`password_unavailable` bei Downloads und Callbacks,
`unsupported_capability` bei den fehlersicheren Schreibvorgängen).

## Credential-Präfix (Einstellung des Besitzers)

`TiaMcp/` ist das Standardpräfix. Der Besitzer kann mit der Umgebungsvariablen
`TIA_MCP_CREDENTIAL_PREFIX` in der MCP-Client-Konfiguration (`env`) ein anderes setzen, zum Beispiel `Plant/Line1/`, oder `*`, um jedes
generische Credential-Ziel zuzulassen. Host und Worker lesen es einmal beim Start (der Worker erbt die Umgebung des Hosts); kein
Werkzeugaufruf kann es ändern, und ein ungültiger Wert (Leerzeichen an einem der Enden, Steuerzeichen, ein `*` innerhalb eines Präfixes, mehr als
128 Zeichen) verhindert den Start. Das aktive Präfix wird beim Start ausgegeben (`TIA MCP credential prefix: ...`) und
von der Doctor-Prüfung `password-sources` gemeldet. Passwortwerte werden weiterhin nie als Werkzeugargumente angenommen; `*` erweitert nur,
welche Credential-*Namen* ein Aufrufer angeben darf.

## Ein Passwort einmalig speichern

Der Benutzer tippt das Passwort in seine eigene Konsole; der Assistent sieht oder verarbeitet es nie:

```cmd
cmdkey /generic:"TiaMcp/MCP_Safety_01/PLC_F1/Safety" /user:tia /pass
```

`cmdkey` fragt das Passwort ohne Echo ab. Die Anmeldeinformation gehört dem Windows-Benutzer, unter dem der MCP-Server läuft.
Mit demselben Befehl wird sie aktualisiert; entfernt wird sie mit `cmdkey /delete:"TiaMcp/MCP_Safety_01/PLC_F1/Safety"`.

## Passwort-Zuordnungsdatei (optional)

`%APPDATA%\TiaMcpServer\passwords.json` oder der Pfad in `TIA_MCP_PASSWORD_MAP` wird einmal beim Start gelesen
(`PasswordMapFile`, `PasswordMapStore`). Sie ordnet einer PLC und einem Zweck eine Quelle zu, sodass Aufrufe sie nicht nennen müssen:

```json
{ "version": 1, "entries": [
  { "project": "MCP_Safety_01", "plc": "PLC_F1", "purpose": "safety", "credential": "TiaMcp/MCP_Safety_01/PLC_F1/Safety" },
  { "plc": "PLC_1", "purpose": "ModuleWriteAccessPassword", "env": "PLC1_WRITE_PW" } ] }
```

- `purpose`: `safety` (die drei fehlersicheren Passwortoperationen), ein Name einer Zugriffsstufe (`FullAccessIncludingFailsafe`,
  `FullAccess`, `ReadAccess`, `HMIAccess`: nur `set_plc_access_level`, für Stufen, die offener sind als die angeforderte),
  `PlcMasterSecretPassword` (die Operationen für vertrauliche Daten und die gleichnamige Download-Konfiguration) oder ein
  Kurzname eines Passwort-Konfigurationstyps (`ModuleWriteAccessPassword`, `ModuleReadAccessPassword`,
  `OnlineReadAccessPassword`, `PasswordReadAccess`, ...; niemals `TlsVerificationConfiguration`).
- `project` (der Dateiname des gebundenen Projekts ohne Endung) und `plc` (so, wie der Aufruf sie benennt) sind optional und
  unabhängig von Groß-/Kleinschreibung; ein weggelassenes Member und `"*"` bedeuten beide „jedes“. Der spezifischste Eintrag gewinnt
  (`PasswordMapFile.Specificity`): Projekt und PLC, dann Projekt (jede PLC darin), dann PLC (dieser PLC-Name in jedem
  Projekt), dann der Standard für jedes Projekt und jede PLC. Ein benanntes Projekt hat Vorrang vor einer benannten PLC, weil sich PLC-Namen wie
  `PLC_1` über Projekte hinweg wiederholen.
- Genau eines von `env` oder `credential` je Eintrag; keine wiederholte Kombination aus project/plc/purpose.
- Eine Quelle, die der Aufruf selbst angibt, gewinnt immer; bei Downloads, Online und Upload ergänzt die Zuordnung nur Konfigurationstypen, die
  der Aufruf weder mit `passwords` noch mit `decisions` beantwortet hat. Zuordnungseinträge für Downloads und `go_online` passen auf das
  Argument `plcName`, so wie es angegeben ist (ein weggelassener Name passt auf Einträge ohne `plc`); der Station-Upload verwendet Einträge ohne `plc`.
- Das Schema ist geschlossen. Jedes unbekannte Member lässt die Datei ablehnen, und ein Member, dessen Name wie ein Wertfeld aussieht
  (enthält password, pass, pwd, secret, value, token, pin, blob oder key), lässt sie mit einer Meldung ablehnen, dass die Zuordnung
  nur Namen enthält. Eine ungültige Zuordnung stoppt den Server beim Start; die Meldung wiederholt nie einen Wert aus der Datei.

## Passwortverwaltung (Windows-GUI)

`TiaMcpServer.PasswordManager` („TiaMcp Passwortverwaltung“, WinForms, `net10.0-windows`, deutsche Oberfläche) wird neben dem
Server in `password-manager\TiaMcpPasswortverwaltung.exe` ausgeliefert; `tia-mcp passwords [--prefix <prefix>] [--map <path>] [--settings <path>]`
startet es (`PasswordManagerCommand`). Der Benutzer tippt jedes Passwort in die maskierten Felder; nichts läuft über einen
Chat, ein Argument oder eine Datei.

- Es listet, ergänzt, ändert und löscht Zuordnungseinträge: Zweck (Zugriffsstufe, vertrauliche Projektierungsdaten, Safety,
  Passwortkonfigurationen für Modul/Online oder ein anderer Kurzname eines Konfigurationstyps) und Geltungsbereich (jedes Projekt oder ein
  Projekt, jede PLC oder eine PLC).
- Beim Speichern wird das Geheimnis mit `CredWriteW` geschrieben (`CRED_TYPE_GENERIC`, `CRED_PERSIST_LOCAL_MACHINE`, UTF-16LE, wie
  `cmdkey` es speichert; die nicht verwaltete Kopie wird mit Nullen überschrieben), unter `<prefix><project or "Alle Projekte">/<plc or "Alle PLCs">/<purpose>`,
  und erst danach der Eintrag (nur Namen, Platzhalter als `"*"`) in die Zuordnung, über eine temporäre Datei. Ein geänderter Geltungsbereich schreibt ein
  neues Ziel und bietet an, das alte zu löschen; das Löschen eines Eintrags bietet an, seine Anmeldeinformation zu löschen, wenn kein anderer Eintrag sie verwendet.
- „Test“ prüft, ob die Anmeldeinformation für diesen Windows-Benutzer existiert und zum Präfix des Servers passt (`CredReadW`; der Blob
  wird ungelesen mit Nullen überschrieben, nur seine Größe wird geprüft). Umgebungseinträge werden nur in der eigenen Umgebung des Programms geprüft.
- Eine ungültige Zuordnung wird nie überschrieben. Der Server liest Anmeldeinformationen nur; nur die Passwortverwaltung schreibt sie.
- Die Logik (`PasswordMapEditor`, `PasswordPurposes`, `PasswordManagerOptions`, `ICredentialStore`) liegt in Contracts
  `Shared/PasswordManagement` und wird unter Linux getestet (`PasswordManagerTests`); der Win32-Speicher und die Oberfläche liegen im
  reinen Windows-Projekt, das nicht in `TiaMcpServer.slnx` enthalten ist und nur unter Windows von `TiaMcpServer.csproj` wiederhergestellt, gebaut und kopiert wird
  (`BuildPasswordManager`, `false` überspringt es).
- Der Server liest die Zuordnung beim Start: Nach dem Hinzufügen oder Entfernen von Einträgen muss er neu gestartet werden. Ein geändertes Passwort unter demselben
  Ziel gilt sofort, weil Anmeldeinformationen zum Zeitpunkt des Aufrufs gelesen werden.
- Ihr zweiter Reiter, **Zugriffsmodus**, setzt den Zugriffsmodus des Servers in der Einstellungsdatei des Servers
  (`%APPDATA%\TiaMcpServer\settings.json` oder `TIA_MCP_SETTINGS`, `--settings`); Logik in `ServerSettingsEditor`
  (Contracts `Shared/PasswordManagement`, getestet durch `ServerSettingsTests`). Siehe
  [Zugriffsmodus in der Passwortverwaltung wählen](installation.md#choosing-the-access-mode-in-the-password-manager).
