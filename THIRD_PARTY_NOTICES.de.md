# Code Dritter in diesem Fork

**Sprache:** [English](THIRD_PARTY_NOTICES.md) · Deutsch

Dieser Fork basiert auf [Czarnak/tia-portal-mcp](https://github.com/Czarnak/tia-portal-mcp)
(MIT; dessen Hinweis bleibt in `LICENSE-MIT` erhalten). Da er Code aus rickgaiser/TiaMcp
(AGPL-3.0) enthält, ist der Fork als Ganzes ausschließlich unter der GNU Affero General Public License v3.0
lizenziert (`LICENSE`). Code aus anderen Projekten darf nur nach den folgenden Regeln aufgenommen werden.

## Veröffentlichung (Entscheidung des Eigentümers 2026-11-01)

- Der Fork ist unter <https://github.com/Birger90/tia-portal-mcp-server> unter
  AGPL-3.0-only veröffentlicht, mit seinem vollständigen korrespondierenden Quellcode.
- Wer den Fork oder ein daraus gebautes Paket weitergibt oder ihn Nutzern über ein Netzwerk anbietet
  (§13), muss dies unter AGPL-3.0 mit dem korrespondierenden Quellcode tun.
- Der MIT-Hinweis des Basiscodes bleibt in `LICENSE-MIT` und wird im Paket neben
  `LICENSE` ausgeliefert.

## rickgaiser/TiaMcp (AGPL-3.0)

- Quelle: <https://github.com/rickgaiser/TiaMcp>, Lizenz GNU Affero General Public License v3.0.
- Entscheidung des Eigentümers 2026-11-01: Code aus diesem Projekt darf übernommen werden. Am selben Tag entschied der Eigentümer,
  den Fork unter AGPL-3.0-only zu veröffentlichen (siehe oben); das ist die Bedingung, die AGPL-3.0 für die
  Weitergabe eines Werks stellt, das solchen Code enthält.
- Jede übernommene Datei bzw. jeder übernommene Abschnitt behält einen Kopf, der die Herkunft, den Upstream-Commit und die
  Lizenz nennt, zum Beispiel:

  ```
  // Derived from rickgaiser/TiaMcp <path> at <commit> (AGPL-3.0-only). See THIRD_PARTY_NOTICES.md.
  ```

- Übernommener Code unterliegt den Regeln dieses Forks: Jedes Siemens-Member, das er aufruft, wird gegen die
  in `AGENTS.md` aufgeführten Siemens-Quellen verifiziert, und er durchläuft die abgesicherte Schreib-Pipeline und die Tests.
- Von diesem Projekt berichtetes Verhalten von TIA Portal (Abstürze, stille Importfehler) gilt als
  „berichtet, nicht verifiziert“, bis es live an V21 geprüft ist.

### Bisher übernommen

| Datei im Fork | Upstream-Datei bei `8e9de1c` | Was übernommen wurde |
| --- | --- | --- |
| `TiaMcpServer.Contracts/Plc/Interface/S7dclImportHazards.cs` | `src/TiaMcp.Formats/S7dclSource.cs` | Die Liste der Systemtypen, die Deklarations-, Netzwerk- und Sprachmuster, der Scan nach verschachtelten Indizes und die SimaticML-Abfrage für AT-Überlagerungen (`FindWriteProblems`, `NestedIndexLine`, `AtOverlays`, das Zeilenmuster von `WithAtMarkers`) sowie der Inhalt ihrer Befundtexte, umformuliert als berichtetes Verhalten. Typisierte Befunde, die Prüfung deklarierter Überlagerungen, die Prüfung der textuellen Schnittstelle und der Vergleich mit dem erneuten Export stammen von diesem Fork. |
| `TiaMcpServer.Tests/Plc/S7dclImportHazardsTests.cs` | `tests/TiaMcp.Tests/S7dclSourceTests.cs` | Der Beispielbaustein, die Deklarations- und Parameterfälle und das Beispiel für AT-Überlagerungen. |

Nur Ideen (kein Code): die Zählregeln für Exporte aus `src/TiaMcp.Openness/TiaConnection.cs`
(`ExportsSinceImport`) hinter `BlockExportLedger` und die Auflistung ohne `Attach()` von
`ListRunningInstances` hinter `otherClientCount` von `list_portals`.

## BetterTIA (MIT)

- Quelle: die vier Repositories von <https://github.com/BetterTIA> (Wunsch des Eigentümers 2026-11-01), schreibgeschützt geklont und
  nie gebaut oder ausgeführt: `watch` bei `1978f24b330f265d8ed2a67e9b1d673d1b4122d8`, `writer` bei
  `fc8179c9b59d364961f2255d07d05af893c32837`, `reader` bei `246d7c54772a502e854630e003e26fa37f92aeaf`, `editor` bei
  `3d115b30035ad5ea2f10206e5354190ea1ba4de5`. Die Lizenzdatei jedes Repositorys wurde gelesen, bevor etwas kopiert wurde: Alle
  vier stehen unter MIT.
- MIT erlaubt Kopieren und Anpassen, sofern der Copyright- und Erlaubnishinweis erhalten bleibt. Jede angepasste Datei trägt einen
  Herkunftskopf mit Repository, Pfad, Commit und Copyright-Zeile; die Hinweise sind unten wiedergegeben.
- Ihre Siemens-Aufrufe unterliegen wie jeder andere Code den Regeln dieses Forks: Members außerhalb der Compile-Stubs und der
  Siemens-Skills werden zur Laufzeit gebunden, und von ihnen berichtetes Verhalten von TIA Portal gilt als „von BetterTIA berichtet, nicht verifiziert“.
  Siehe die [Referenz der BetterTIA-Operationen](docs/SupportedOperations/BETTERTIA_ADOPTED_OPERATIONS_SUMMARY.de.md).

### Bisher übernommen

| Datei im Fork | Upstream-Datei | Was übernommen wurde |
| --- | --- | --- |
| `TiaMcpServer.Contracts/Plc/Interface/PlcDbSnapshotDocument.cs` | watch `src/BtiaWatch/WatchTools.cs` (`FormatSnapshot`, `IsAtOrBelow`, `TopLevel`) | Der Leser für den Snapshot-Export (das Layout `Snapshot`/`Value`/`SnapshotDate`), der Member-Filter „auf oder unterhalb“ und der Hinweis auf die oberste Ebene; typisierte Werte und die Wertbegrenzung stammen von diesem Fork. |
| `TiaMcpServer.Contracts/Plc/Interface/SimaticMlNetworkTexts.cs` | writer `src/TiaMcpServer/Siemens/SimaticMl.cs` (`Networks`, `Texts`, `SetText`, `SetNetworkText`, `ReplaceAll`, `ReplaceNetworkText`) | Das Modell der Netzwerktexte, die Ersetzung in einem Durchlauf mit den längsten Treffern zuerst und der GRAPH-Textbereich; typisierte Änderungsdatensätze mit wieder auffindbaren Schlüsseln und das Anlegen eines fehlenden Sprach-Items stammen von diesem Fork. |
| `TiaMcpServer.Contracts/Plc/Interface/SimaticMlMemberRenames.cs` | writer `src/TiaMcpServer/Siemens/SimaticMl.cs` (`RenameMembers`: der Durchlauf über die Komponenten `Symbol`/`Instance`, `Segment`, `Operand`, `PlainName`) | Der Durchlauf über lokale Zugriffe und die Muster für GRAPH-Aktionsoperanden; die Beschränkung auf das eigene Dokument des Objekts, die Tauschvorgänge über temporäre Namen auf dem bestehenden Modell der Schnittstellenbearbeitung und die abschnittsübergreifende Namensprüfung stammen von diesem Fork. |
| `TiaMcpServer.Contracts/Plc/PlcBatchEdits.cs` (`PlcBatchRenamePlan`) | writer `src/TiaMcpServer/Siemens/BatchRename.cs` (`BatchRename.Plan`) | Die Reihenfolge mit temporären Namen für Vertauschungen, Rotationen und Ketten; die Kollisionsprüfung gegen Namen außerhalb des Batches wurde weggelassen (TIA Portal entscheidet). |
| `TiaMcpServer.OpennessWorker/Openness/Plc/PlcBatchEditService.cs` | writer `src/TiaMcpServer/Siemens/Portal.AnetFork.cs` (`RenameTags`, `RenameObjects`, `RenameBatch`) | Die Suche nach Namen (Variablen über Variablentabellen hinweg; Bausteine, dann Technologieobjekte, dann Typen) und die Batch-Abfolge; neu geschrieben mit dem generischen Name-Attribut, einer ExclusiveAccess-Transaktion und der Regel von TIA Portal für fehlersichere Objekte. |
| `TiaMcpServer.OpennessWorker/Openness/Plc/PlcBatchEditService.cs` (`ReplaceTagComments`) | writer `src/TiaMcpServer/Siemens/Portal.AnetFork.cs` (`EditTagComments`, `EditComments`) | Der Durchlauf über `Comment.Items` je Sprache mit der Ersetzung; die Transaktion und das Zurücklesen stammen von diesem Fork. |
| `TiaMcpServer.OpennessWorker/Openness/Hmi/HmiScreenWriter.cs` | writer `src/TiaMcpServer/Siemens/Portal.AnetFork.HmiScreens.cs`, `Portal.AnetFork.Hmi.cs` (Anlegen/Löschen von Bildern und Items, `SetHmiItemAttribute`, `SetHmiDynamizationTag`, `ReplaceInHmiScripts`, `RenameHmiTags`, `RenameHmiScreens`, der Bild-Durchlauf über `Children`/`Spots`) | Die Aufrufabfolgen und der Skript-Durchlauf (Dynamisierungen, Ereignis- und Property-Event-Handler, vier Item-Ebenen); neu geschrieben mit Laufzeitbindung, exakten Typnamen, Zurücklesen und einer Transaktion für Batches. |
| `TiaMcpServer.OpennessWorker/Openness/Block/PlcBlockEditService.Snapshot.cs` | watch `src/BtiaWatch/OpennessPortal.cs` (`ExportDbSnapshot`) | Die Aufrufabfolge `Interface` → `ValueService.CreateSnapshot()`, Baustein → `InterfaceSnapshot.Export(FileInfo, ExportOptions.WithReadOnly)` und die Regel zur Ablehnung im Offline-Zustand; neu geschrieben mit Laufzeitbindung. |
| `TiaMcpServer.FakeWorker/Plc/SimaticSdFixtures.cs` (`BetterTiaFbSafety`, `BetterTiaFcBranches`; nur Testdaten) | editor `tests/samples/Demo_Line/20261012-120000/PLC_1/Blocks/Safety/FB_Safety.s7dcl`/`.s7res`, `Conveyor/FC_Branches.s7dcl`/`.s7res` bei `3d115b3` | Die zwei SIMATIC-SD-Exporte aus V21, Byte für Byte (fehlersicheres FUP mit `S7_Safety`, KOP-Verzweigungen und ein FUP-Netzwerk), als Fixtures des SIMATIC-SD-Parsers und des FakeWorker; es wurde kein Code übernommen. |

### Hinweise

```
MIT License

Copyright (c) 2026 HonzaT

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

Derselbe MIT-Text gilt mit `Copyright (c) 2025 J.Heilingbrunner` (BetterTIA/writer, `LICENSE.txt`) für die
aus `writer` übernommenen Dateien.

## 1748477582/tia-portal-openness-mcp (MIT)

- Quelle: <https://github.com/1748477582/tia-portal-openness-mcp> bei `8e7ca425d5d257132ac8cd5ed9cbc318a7c340e2`, schreibgeschützt
  geklont und nie gebaut oder ausgeführt (Wunsch des Eigentümers 2026-11-01). Dessen `LICENSE` wurde gelesen, bevor etwas kopiert wurde: MIT,
  `Copyright (c) 2026 1748477582`.
- Übernommen wurden nur Testdaten; dessen Skill-Text (`tools/tiaportal-mcp/skill/SKILL.md` §9a) wird als berichtetes Verhalten von V21,
  „berichtet, nicht verifiziert“, in der [SIMATIC-SD-Referenz](docs/SupportedOperations/PLC_SIMATIC_SD_SUMMARY.de.md) zitiert.

### Bisher übernommen

| Datei im Fork | Upstream-Datei | Was übernommen wurde |
| --- | --- | --- |
| `TiaMcpServer.FakeWorker/Plc/SimaticSdFixtures.cs` (`CookbookFbLadV3`, `CookbookFcLad`, `CookbookMixed`; nur Testdaten) | `tools/tiaportal-mcp/skill/lad-cookbook/MCPVerify_FB_LAD_v3.s7dcl`/`.s7res`, `MCPVerify_FC_LAD.s7dcl`/`.s7res`, `MCPVerify_Mixed_LADSCL.s7dcl`/`.s7res` | Drei SIMATIC-SD-Dokumentpaare aus V21, Byte für Byte (UTF-8-BOM, CRLF, mehrzeilige Netzwerk-Pragmas, `S7_Templates`, ein gemischter KOP/SCL-Baustein), als Fixtures; es wurde kein Code übernommen. |

### Hinweis

```
MIT License

Copyright (c) 2026 1748477582

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## heilingbrunner/tiaportal-mcp (MIT)

- Quelle: <https://github.com/heilingbrunner/tiaportal-mcp> bei `72d2ade` (2026-11-01), schreibgeschützt geklont und nie gebaut
  oder ausgeführt. Dessen `LICENSE.txt` wurde zuerst gelesen: MIT, `Copyright (c) 2025 J.Heilingbrunner` (es gilt derselbe Hinweis wie für BetterTIA/writer
  oben).
- Übernommen wurden nur Feature-Ideen; es wurde kein Code kopiert. Jedes Siemens-Member stammt aus der Siemens-V21-Referenz.

### Bisher übernommen

| Datei im Fork | Idee aus | Was übernommen wurde |
| --- | --- | --- |
| `TiaMcpServer.Contracts/Plc/PlcOpcUaExport.cs`, `TiaMcpServer.OpennessWorker/Openness/Plc/OpcUaExportService.cs` (`plc_write export_opcua_xml`) | Commit `f4eb4d7` "feat: add ExportOpcUaXml tool" (`src/TiaMcpServer/Siemens/Portal.OpcUa.cs`) | Das Feature: die OPC-UA-Daten einer PLC als XML exportieren. Neu implementiert aus `Project.HwUtilities.Find("OPCUAExportProvider")` und `OpcUaExportProvider.Export(DeviceItem, FileInfo)`, wie dokumentiert in `totally-integrated-claude` `skills/tia-import-export/references/plc-blocks.md` §13 und `skills/tia-devices-general/references/device-item-operations.md` (Typ aufgeführt in `api-baselines/v21/base.json`), zur Laufzeit gebunden; die abgesicherte `plc_write`-Pipeline, die Dateiablage und die Tests stammen von diesem Fork. |

## Czarnak/siemens-docs-mcp (MIT)

- Quelle: <https://github.com/Czarnak/siemens-docs-mcp> bei `a0a0a24` (2026-11-02), Paket `siemens_docs_mcp` 0.1.2,
  schreibgeschützt geklont. Dessen `LICENSE` wurde zuerst gelesen: MIT, `Copyright (c) 2026 Łukasz Czarnacki`.
- Regel des Eigentümers: heruntergeladener fremder Code läuft nie auf seinem PC. Das Python-Paket wird deshalb weder
  installiert noch ausgeliefert; sein Entwurf wurde nach geprüftem C# in `TiaMcpServer/SiemensDocs/` **portiert** (das
  Tool `siemens_docs`). Für einen Vergleich des Konverters lief es einmal isoliert (`python -I`, aus einem anderen
  Verzeichnis), nur im Cloud-Container.
- Jeder Endpunkt und jede JSON-Form wurde am 2026-11-03 gegen die Live-Seiten geprüft; wo sie abweichen, gilt die
  Live-Seite (siehe die [Referenz zur Siemens-Dokumentation](docs/SupportedOperations/SIEMENS_DOCS_SUMMARY.de.md)).
- Jede portierte Datei trägt einen Herkunftskopf mit dem Upstream-Modul und der Lizenz.

### Bisher portiert

| Datei im Fork | Upstream-Modul | Was portiert wurde |
| --- | --- | --- |
| `TiaMcpServer/SiemensDocs/SiemensDocsHttp.cs` | `siemens_docs_mcp/client.py` | Die Fluid-Topics-Endpunkte, die Web-Sitzungsanfrage vor dem ersten API-Aufruf, der Mindestabstand je Host, eine Wiederholung nach 401/403 (neue Sitzung) und nach 429/5xx (Retry-After). Die Host-Freigabeliste bei jeder Anfrage, keine Weiterleitungen, Größengrenzen und die Fehlerkategorien stammen von diesem Fork. |
| `TiaMcpServer/SiemensDocs/SiemensDocsCatalog.cs` | `siemens_docs_mcp/catalog.py`, `toc.py` | Publikations-Metadaten (Produkt, Versionsschlüssel `tia:SoftwareVersionFilter`/`SoftwareVersion`, Pretty-URL), Filter ohne Beachtung der Groß-/Kleinschreibung mit ähnlichen Werten, kompakte Referenzen `tia:`/`iox:`, Auflösung von Reader-URLs (Id-Form, Pretty-URL längste zuerst), TOC-Parsen mit künstlicher Wurzel für Publikationen mit mehreren Abschnitten. Die Einzel-Map-Abfrage und die begrenzten Caches stammen von diesem Fork. |
| `TiaMcpServer/SiemensDocs/SiemensDocsMarkdown.cs` | `siemens_docs_mcp/converter.py`, `content.py` | Die Zuordnung der Siemens-Klassen (BlocktitleFirst/Blocktitle, table_sourcecode/p_table_l_code, Sicherheitstabellen, MkDocs-Admonitions), die Selektoren für Bedienelemente, GFM-Tabellen und verschachtelte Listen. Der HTML-Parser (`SiemensDocsHtml.cs`) und die am Live-HTML gefundenen Korrekturen (Zeilenumbrüche im Code, Titelbanner, Tabellenüberschriften) stammen von diesem Fork. |
| `TiaMcpServer/SiemensDocs/SiemensDocsService.cs`, `SiemensDocsOptions.cs` | `siemens_docs_mcp/tools.py`, `server.py` | Die vier Operationen, ihre Grenzen und die Einstellungen (Hosts, Gebietsschema, Mindestabstand, dort `SIEMENS_DOCS_*`, hier `--docs-*`/`TIA_MCP_DOCS_*`). Die Batch-Form des Tools, das Blättern mit Offset in TOC und Publikationslisten und die strukturierten Ergebnisse stammen von diesem Fork. |

### Hinweis

```
MIT License

Copyright (c) 2026 Łukasz Czarnacki

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Siemens AG `tia-portal-ai-extensions` (MIT)

Die SIMATIC-SD-Operationen folgen dem Siemens-V21-Skill `openness_development/skills/simatic-sd/SKILL.md`
(MIT, Siemens AG): Dessen Export-/Importaufrufe, das Parsen der Netzwerke nach §4, die Spracherkennung nach §5, die Normalisierung nach §6 und die Einfügeregeln nach §7
wurden in `TiaMcpServer.Contracts/Plc/SimaticSd/` neu implementiert (es wurde keine Datei kopiert). Siehe die
[SIMATIC-SD-Referenz](docs/SupportedOperations/PLC_SIMATIC_SD_SUMMARY.de.md).

## a4webdev/tiacommander-mcp (proprietäre EULA)

Nur Ideen. Es dürfen weder Code noch Text kopiert werden.

Dies ist keine Rechtsberatung.
