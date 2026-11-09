# MCP-Ressourcen und -Prompts

**Sprache:** [English](MCP_RESOURCES_PROMPTS_SUMMARY.md) · Deutsch

> **Status: nicht live getestet.** Ressourcen und Prompts sind offline über das echte MCP-Protokoll getestet (Server und
> Client des ModelContextProtocol-C#-SDK 2.3.0 im selben Prozess); auf dem Engineering-PC hat sie noch kein MCP-Client
> aufgelistet oder benutzt. Wie ein Client sie zeigt (Ressourcenauswahl, Slash-Befehle, Anhänge), entscheidet der Client.

Neben den Werkzeugen bietet der Server MCP-**Ressourcen** (nur lesbarer Referenztext) und **Prompts** (fertige
Arbeitsanweisungen). Beide brauchen weder TIA Portal noch einen Worker noch ein gebundenes Projekt, und beide lesen nichts
vom Rechner des Benutzers: Die Ressourcen sind die beim Bauen in den Server eingebetteten Referenzdokumente und die
eigenen Operationskataloge des Servers; die Prompts erzeugen nur Text. Die Werkzeugzahl (13/33/33) bleibt gleich.

## Ressourcen

`resources/list`, `resources/templates/list` und `resources/read`; in jedem Zugriffsmodus verfügbar (auch nur lesend).

| URI | Inhalt | MIME-Typ |
| --- | --- | --- |
| `tia-mcp://docs/en/<NAME>.md`, `tia-mcp://docs/de/<NAME>.md` | jedes Dokument aus `docs/SupportedOperations/` auf Englisch und Deutsch (zum Beispiel `tia-mcp://docs/en/PLC_OPERATIONS_SUMMARY.md`, `tia-mcp://docs/de/CLAUDE_IN_TIA.md`, diese Seite als `MCP_RESOURCES_PROMPTS_SUMMARY.md`) | `text/markdown` |
| `tia-mcp://docs/en/server/README.md`, `.../de/server/README.md` | die README des Servers | `text/markdown` |
| `tia-mcp://docs/en/server/FORK_FEATURES.md`, `.../de/server/FORK_FEATURES.md` | was dieser Fork gegenüber dem Original ergänzt | `text/markdown` |
| `tia-mcp://operations` | jedes Werkzeug mit einem `operations`-Array: Verfügbarkeit je Zugriffsmodus, Anzahl der Operationen, Referenzdokument | `application/json` |
| Vorlage `tia-mcp://operations/{tool}` | die Operationen eines Werkzeugs mit ihren URIs (und ihren Feldern, wo der Katalog sie nennt) | `application/json` |
| Vorlage `tia-mcp://operations/{tool}/{operation}` | eine Operation | `application/json` |

- Jede Dokumentressource hat einen `name` (`docs-en-PLC_OPERATIONS_SUMMARY`, `docs-de-server-README`), ihre erste
  Überschrift als `title`, eine `description` und ihre Größe `size` in Bytes. Der Text ist das Dokument wie eingecheckt;
  relative Links darin zeigen auf Dateien im Repository und lösen nicht als Ressourcen auf.
- Eingebettet werden die Dokumente mit `TiaMcpServer/McpContext/EmbeddedDocs.props` (vom Server und vom Testprojekt
  importiert). Ein neues Dokument in `docs/SupportedOperations/` wird mit dem nächsten Build zur Ressource; ein Test prüft,
  dass jede Datei in beiden Sprachen mit genau ihrem Text eingebettet ist.
- Operationswerkzeuge: `plc_read`, `plc_write`, `network_read`, `network_write`, `hmi_read`, `hmi_write`, `vci_read`,
  `vci_write`, `library_read`, `library_write`, `plcsim_read`, `plcsim_write`, `plc_online_read`, `plc_online_write`,
  `siemens_docs`, `tia_inbox`. Ein Test hält diese Liste gegen jedes Produktionswerkzeug mit einem Argument `operations`.
  Werkzeuge ohne Operations-Array (`bind_project`, `compile_check`, `plc_download`, `plc_tia_online`,
  `plc_station_upload`, `plcsim_test`, Lebenszyklus-Werkzeuge) beschreiben ihre Werkzeugbeschreibungen.
- Operationen von `plc_read`/`plc_write` tragen `description`: genau den Eintrag, den `plc_read describe_operations`
  liefert (`PlcOperationReference.DescribeOne`, dieselben Felder, Feldregeln und Referenztexte). Operationen von
  `network_*` tragen die Pflicht- und optionalen Felder des Netzwerkkatalogs. Für die übrigen Werkzeuge nennt die
  Ressource die Operationsnamen des Werkzeugkatalogs; Felder und Regeln stehen in der Werkzeugbeschreibung und im
  Referenzdokument, das der Eintrag nennt.
- Schreibende Werkzeuge stehen in jedem Modus in der Liste, mit `availability` „read-write and full (not offered in
  read-only)“: Die Ressource ist Dokumentation; ob ein Werkzeug läuft, entscheidet weiterhin der Zugriffsmodus.
- Ein unbekanntes Dokument, Werkzeug oder eine unbekannte Operation ist ein Protokollfehler (Ressource nicht gefunden),
  der den zu lesenden Index nennt.

## Prompts

`prompts/list` und `prompts/get`. Jeder Prompt liefert eine Benutzernachricht auf Englisch, die das Modell anweist, in der
Sprache des Benutzers zu antworten, und mit den Serverregeln endet: Passwörter nur als Passwortquellen (Name einer
Umgebungsvariablen, `{ "credential": "TiaMcp/..." }` oder die Passwortzuordnung), TLS-Vertrauen nur über
`trustedCertificates` mit Fingerabdrücken, die der Benutzer geprüft hat, der Zugriffsmodus entscheidet, was sich ändern
darf, die Sicherheitsabnahme bleibt bei einer befähigten Person. Argumentwerte werden als Daten in Anführungszeichen in den
Text gesetzt.

| Prompt | Argumente | Aufgelistet in | Ablauf |
| --- | --- | --- | --- |
| `explain_block` | `blockPath` | jedem Modus | Baustein lesen (`get_block_content`, `list_networks`, `get_block_interface`, Querverweise, Aufrufbaum) und erklären; nur lesend |
| `review_block` | `blockPath` | jedem Modus | Prüfung auf Logikfehler, Robustheit, Benennung, F-Status; Befunde nach Schwere; kein Schreiben; eine Prüfung ist keine Sicherheitsabnahme |
| `revise_block_safely` | `blockPath`, `goal` | read-write, full | Inhalt und `contentHash` lesen → Plan dem Benutzer zeigen → `plc_write` `dryRun:true` mit `expectedContentHash` → schreiben → `compile_check` → Bericht; nie `save_project` oder `plc_download` ohne ausdrücklichen Wunsch des Benutzers |
| `document_project` | optional `plcName` | jedem Modus | `get_project_status`, `browse_project_tree`, `list_tag_tables`, `get_call_tree`, `read_hardware_config`, `list_network_objects` → ein Markdown-Dokument in der Antwort; der Server schreibt keine Datei |
| `check_hardware` | `deviceName` | jedem Modus | `read_hardware_config`, Netzwerkobjekte, CPU-Schutz und -Einstellungen, Ladeziele, Adressbelegung → Bericht; Hardware-Übersetzen nur mit Zustimmung des Benutzers |
| `safety_overview` | `plcName` | jedem Modus | `get_safety_administration`, `list_safety_blocks`, `get_safety_validation` → Zusammenfassung; keine Anmeldung, kein Schreiben; sagt, dass Validierung und Abnahme bei einer befähigten Person bleiben |
| `download_to_plcsim` | `plcName`, `instance` | read-write, full | `plcsim_read get_instance` → `list_download_targets` (nur PLCSIM-Schnittstelle) → `plc_download` `dryRun:true` → Laden nach Zustimmung des Benutzers (read-write bestätigt den Aufruf) → bei `abort.tls` vergleicht der Benutzer die Fingerabdrücke, Wiederholung mit `trustedCertificates` nur mit freigegebenen → `get_instance` |
| `handle_tia_inbox` | keine | jedem Modus | `tia_inbox list_requests` → `get_request` → Adresshinweise bestätigen → unter dem Zugriffsmodus arbeiten → `reply_request` für jede Anfrage |

- Ein Prompt steht nur in den Zugriffsmodi in der Liste, die die Werkzeuge anbieten, die er braucht
  (`revise_block_safely`: ProjectMutation und Compile; `download_to_plcsim`: OnlineControl und SimulationControl); nur
  lesend sind es also sechs Prompts.
- `prompts/get` prüft die Argumente: ein unbekannter Name, ein fehlendes oder leeres Pflichtargument, ein Wert, der kein
  String ist, länger als 400 Zeichen ist oder Steuerzeichen (auch Zeilenumbrüche) enthält, ist ein Protokollfehler
  (ungültige Parameter), der das Problem nennt. Optionale Argumente dürfen fehlen oder null sein.
- Ein Test prüft, dass jede Operation von `plc_read`/`plc_write`/`network_read`/`plcsim_*`/`tia_inbox`, die ein Prompt
  nennt, in den Katalogen existiert.

## Umsetzung

`TiaMcpServer/McpContext/`: `TiaMcpDocuments.cs` (eingebettete Dokumente, ein `McpServerResource` je Dokument),
`TiaMcpOperationResources.cs` (Operationsliste und die zwei Vorlagen), `TiaMcpPrompts.cs` (Prompt-Definitionen,
Prüfung, `McpServerPrompt`, `WithTiaMcpResourcesAndPrompts(mode)`). Verwendet werden die abstrakten SDK-Bausteine
`McpServerResource` (`ProtocolResourceTemplate`, `ProtocolResource`, `IsMatch`, `ReadAsync`) und `McpServerPrompt`
(`ProtocolPrompt`, `GetAsync`), registriert mit `WithResources(IEnumerable<McpServerResource>)` und
`WithPrompts(IEnumerable<McpServerPrompt>)` aus ModelContextProtocol 2.3.0. Tests:
`TiaMcpServer.Tests/McpContext/McpResourcesAndPromptsTests.cs`.

## Ausgelassen

- Ressourcen-Abonnements und Benachrichtigungen über Listenänderungen: Der Inhalt steht beim Bauen fest.
- Vervollständigung (`completion/complete`) für Prompt-Argumente: Bausteinpfade und Gerätenamen bräuchten das gebundene
  Projekt, das dieses Paket nicht liest.
- Projektdaten als Ressourcen (Bausteine, Variablentabellen, Hardware): bewusst ausgelassen; sie bleiben hinter den
  Werkzeugen, der Bindung und dem Zugriffsmodus.
