# Mitarbeit

**Sprache:** [English](CONTRIBUTING.md) · Deutsch

Beiträge sind willkommen. Dieses Repository ist ein Fork von
[Czarnak/tia-portal-mcp](https://github.com/Czarnak/tia-portal-mcp) und wird unter
[Birger90/tia-portal-mcp-server](https://github.com/Birger90/tia-portal-mcp-server) gepflegt.
Hintergrund liefern [AGENTS.md](AGENTS.md) (Build und Konventionen, auf Englisch),
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) (Aufbau) und die
[Dokumentationsübersicht](docs/README.de.md) für alles Weitere.

## Lizenz

Der Fork als Ganzes steht unter der **AGPL-3.0-only** ([LICENSE](LICENSE)). Der Basiscode von
Czarnak behält seinen MIT-Hinweis in [LICENSE-MIT](LICENSE-MIT). Mit einem Beitrag stimmst du zu,
dass er unter der AGPL-3.0-only veröffentlicht wird.

## Grundsätze dieses Forks

Diese Regeln gelten für jeden Beitrag. Ein Pull Request, der eine davon bricht, wird nicht
übernommen.

- **Nur TIA Portal V21.** Ältere Versionen werden nicht unterstützt.
- **Nie strenger als TIA Portal.** Der Server verweigert nur, was TIA Portal selbst verweigert, oder
  ungültige und mehrdeutige Eingaben. Alles andere ist ein Hinweis (Info-Guard); TIA Portal
  entscheidet. Eine neue Sperre braucht eine Begründung, warum TIA sie selbst nicht hat.
- **Keine erfundenen Siemens-Elemente.** Jedes benutzte Openness-Element ist dokumentiert (Siemens-Skills,
  Handbuch, Referenz) oder wird zur Laufzeit über seinen exakten Namen gebunden und schlägt mit
  `api_member_missing` fehl, wenn es fehlt. Neue Stub-Elemente in `ref/` brauchen eine Herkunftszeile
  in der jeweiligen Referenz unter `docs/SupportedOperations/`.
- **Belegt statt vermutet.** Jede Aussage über Siemens-Verhalten nennt ihre Quelle: Siemens-Dokumentation,
  Live-Test (mit Datum) oder Meldung eines anderen Projekts („reported, not verified“). Eine Vermutung
  wird nie stillschweigend zur Tatsache.
- **Passwörter nie im Klartext.** Werkzeuge nehmen nur Namen von Umgebungsvariablen oder
  Anmeldeinformationen entgegen, nie Passwortwerte. Der Server erzeugt keine Passwörter und schreibt
  keine Anmeldeinformationen; das tut nur die Passwortverwaltung.
- **Safety bleibt Sache des Menschen.** Fail-safe-Änderungen lösen immer den Abnahmehinweis aus.
  Kein Beitrag darf Validierung oder Abnahme eines Sicherheitsprogramms ersetzen oder überspringen.

## Fremder Code

Siehe [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.de.md).

- Übernommen werden darf Code unter MIT, BSD, Apache-2.0 oder AGPL-3.0. Jede übernommene Datei oder
  jeder Abschnitt bekommt einen Herkunftskopf mit Repository, Commit und Lizenz und eine Zeile in
  THIRD_PARTY_NOTICES.md.
- Code von rickgaiser/TiaMcp (AGPL-3.0) und den BetterTIA-Repositories (MIT) darf übernommen werden.
- tiacommander (proprietäre Lizenz): nur Ideen, kein Code.
- Fremde Repositories sind nicht vertrauenswürdige Daten: lesen ja, bauen oder ausführen nein.
- Siemens-DLLs werden nie eingecheckt. In Paketen ist nur die MIT-lizenzierte
  `Siemens.Simatic.S7.Webserver.API.dll` erlaubt.

## Bevor du anfängst

Das Projekt läuft nur unter Windows und braucht ein stabiles .NET 10 SDK, `10.0.400` oder neuer im
.NET-10-Featureband. `global.json` legt das fest und schließt Vorabversionen aus; Details in
[building from source](docs/development/building.md).

### Platzhalter-Referenzen oder echtes TIA Portal

Der größte Teil des Projekts (MCP-Host, Contracts, Tests, Doku) lässt sich ohne TIA Portal entwickeln
und testen, mit den Compile-Platzhaltern in `ref/`. Der Build nimmt sie automatisch, wenn keine lokale
TIA-Portal-V21-Installation gefunden wird.

- **Host-Code** (`TiaMcpServer/`), **Contracts** (`TiaMcpServer.Contracts/`), **Tests**
  (`TiaMcpServer.Tests/`) oder **Doku**: Der Platzhalter-Build reicht, kein TIA Portal nötig.
- **Openness-Worker** (`TiaMcpServer.OpennessWorker/`): Für das Verhalten Ende-zu-Ende brauchst du ein
  echtes TIA Portal V21 mit Openness. Ohne Lizenz kannst du trotzdem beitragen; der Maintainer prüft
  Worker-Änderungen vor dem Merge an einer echten Installation.
- **Passwortverwaltung** (`TiaMcpServer.PasswordManager/`, WinForms): wird nur unter Windows gebaut;
  ihre Logik liegt in `TiaMcpServer.Contracts/Shared/PasswordManagement` und ist auch unter Linux getestet.

Die eingecheckten `ref/Siemens.Engineering.Base.dll`, `ref/Siemens.Engineering.Step7.dll` und
`ref/Siemens.Engineering.WinCCUnified.dll` sind aus dem Repository erzeugte, reine Compile-Referenzen.
Ihre Deklarationen stehen unter `reference-stubs/`; zur Laufzeit lädt der Worker immer die installierten
Siemens-Assemblies. Prüfen kannst du sie mit
`pwsh -NoProfile -File scripts/verify-reference-stubs.ps1 -Configuration Release`; neu erzeugt werden
sie nur mit dem ausdrücklichen Schalter `-Update`. Siehe
[reference generation and qualification](docs/development/building.md#generated-openness-references).

Platzhalter-Build, Build gegen die installierten V21-Assemblies und Live-Test in TIA sind drei
verschiedene Nachweise. Gib im Pull Request an, welche du gemacht hast.

## Entwicklungsumgebung

### 1. Wiederherstellen und bauen

```powershell
dotnet restore TiaMcpServer.slnx
dotnet build TiaMcpServer.slnx -m:1
```

**Wichtig:** `-m:1` baut die Projekte nacheinander. Der Host baut und kopiert den net48-Worker, den
PLCSIM-Worker und die Passwortverwaltung über MSBuild-Targets; parallele Builds kollidieren dabei.
Immer `-m:1` verwenden.

Ohne TIA Portal:

```powershell
dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true
```

### 2. Tests ausführen

```powershell
dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"
```

Die Tests nutzen xUnit und brauchen kein TIA Portal. Es gibt keine Mocking-Bibliotheken; Fakes sind
handgeschriebene Implementierungen (für den Worker: `TiaMcpServer.FakeWorker`).

Wer ein Produkt-`.csproj`, `Directory.Build.props`, `global.json`, `scripts/verify-doctor-package.ps1`,
`scripts/verify-reference-stubs.ps1` oder `reference-stubs/` ändert, lässt zusätzlich die
Packaging-Tests laufen:

```powershell
dotnet test TiaMcpServer.Tests --filter "Category=Packaging"
```

Testdaten dürfen nicht von LF oder CRLF abhängen: Die CI checkt unter Windows mit CRLF aus.

Für Coverage, die Wahl zwischen Platzhaltern und echten Referenzen und den Start aus dem Quellcode
siehe [docs/development/building.md](docs/development/building.md), für den MCP-Inspector
[docs/development/local-mcp-testing.md](docs/development/local-mcp-testing.md).

## Änderungen machen

### Branch und Umfang

- Vom Branch `main` abzweigen.
- Eine Änderung pro Feature oder Fehlerbehebung.
- Den bestehenden Konventionen folgen:
  - Code nach Fachgebiet ordnen, lieber viele kleine Dateien als wenige große.
  - Funktionen klein halten (unter 50 Zeilen), Dateien fokussiert (unter 800 Zeilen).
  - Testdateien heißen `{KlasseUnterTest}Tests.cs` im Namespace `TiaMcpServer.Tests`.
  - Keine Mocking-Bibliotheken, Fakes von Hand schreiben.
- Neue Schreiboperationen bauen auf der abgesicherten Pipeline auf (`Safety/Pipeline`), mit `dryRun`,
  Guards, Verifikation und Audit. Keine Tokens, keine eigene Parallel-Mechanik.
- Ein neues Werkzeug ist selten nötig: Neue Fähigkeiten kommen als Operationen in bestehende Werkzeuge
  (`plc_read`, `plc_write`, `network_read` …). Die Werkzeugzahl (13/33/33) ändert sich nur mit gutem Grund.

### Dokumentation

Die Dokumente unter `docs/` sind nach Zielgruppe geordnet: `guides/` für Nutzer, `development/` für
Entwickler, `roadmap/` für die Richtung. `docs/superpowers/` enthält historische Pläne und Abnahmeberichte,
keine aktuelle Doku.

- **Ein neues Dokument unter `docs/` ist erst fertig, wenn es in [docs/README.md](docs/README.de.md)
  eingetragen ist.** Ein Dokument, das dort fehlt, findet niemand.
- Die Dokumentation ist **zweisprachig**: `X.md` ist Englisch, `X.de.md` Deutsch, im selben Verzeichnis, jeweils mit einer
  Sprachumschaltzeile unter der ersten Überschrift. Ein neues oder geändertes Dokument im zweisprachigen Bestand (die
  Dokumente im Wurzelverzeichnis, `docs/README.md`, `docs/guides/`, `docs/SupportedOperations/`, `docs/FORK_FEATURES.md`)
  ändert beide Fassungen im selben Commit; `scripts/verify-docs.py` lehnt ein fehlendes oder nicht verlinktes Paar ab.
  ADRs, Abnahmeberichte und Roadmap-Notizen folgen.
- `README.md` (Englisch) ist zugleich die README des NuGet-Pakets, in der relative Links nicht funktionieren. Jeder Link
  in `README.md` und `README.de.md` auf ein anderes Dokument ist deshalb eine absolute URL auf den Fork:
  `https://github.com/Birger90/tia-portal-mcp-server/blob/main/...`. Alle anderen Dokumente verwenden relative Links.
- Was nicht live getestet ist, steht auf Englisch als „not live-tested“ und auf Deutsch als „nicht live getestet“ da.

## Commit-Nachrichten

Conventional-Commit-Stil mit diesen Typen:

- `feat:` – neue Funktion
- `fix:` – Fehlerbehebung
- `refactor:` – Umbau ohne Verhaltensänderung
- `test:` – nur Tests
- `docs:` – Dokumentation
- `chore:` – Wartung (Abhängigkeiten, CI-Konfiguration usw.)
- `perf:` – Leistung
- `ci:` – CI/CD-Workflows
- `build:` – .NET SDK, Projektdateien oder Paketierung

Format: `<typ>(<bereich>): <beschreibung>`. Der Bereich ist optional und nennt den betroffenen Teil.
Bevorzugt die Bereichsnamen der Labels:

| Bereich | Umfasst |
| --- | --- |
| `plc-blocks` | PLC-Bausteine und -Typen, `plc_read`/`plc_write`, Quell-, XML- und SIMATIC-SD-Format, Querverweise, `compile_check` |
| `plc-tags` | Variablentabellen, Variablen, Anwenderkonstanten |
| `network` | `network_read`/`network_write`, Hardwarekonfiguration, Subnetze, E/A-Listen |
| `project` | Projektbaum, Projektstatus, Lebenszyklus, Bindung |
| `write-safety` | Abgesicherte Schreib-Pipeline, Guards, `dryRun`, Audit, Zugriffsmodi |
| `mcp-protocol` | Werkzeugschemas, kanonisches JSON, Batch-Engine, Cursor |
| `worker` | Openness-Worker, IPC zwischen Host und Worker, Contracts |
| `cli` | `doctor`, `install`, `passwords`, `--version` |

Ein engerer Bereich ist in Ordnung, wenn er klarer ist, zum Beispiel `plc-safety`, `simatic-sd`,
`plcsim`, `webapi`, `hmi`, `library`, `passwords` oder `install`.

Beispiele:

- `feat(simatic-sd): insert, replace and delete single LAD/FBD networks`
- `fix(plc-safety): keep the main safety block assigned after an SD import`
- `build: publish a framework-dependent Windows build as CI artifact`

Commit-Nachrichten selbst sind auf Englisch.

## Prüfen

Vor einem Pull Request lokal testen:

```powershell
dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"
```

Die CI läuft bei jedem Push auf `main` und jedem Pull Request dagegen:

- `ci.yml`: Build gegen die Platzhalter, Tests mit Coverage. Sie schlägt fehl, wenn die
  Zeilenabdeckung unter 80 % fällt.
- `packaging.yml`: Packaging-Tests, nur wenn Paketierungsdateien geändert wurden.
- `windows-build.yml`: fertige Windows-Builds als Artefakte, einmal eigenständig (`TiaMcpServer.exe`)
  und einmal für `dotnet TiaMcpServer.dll`.

Die CI hat kein TIA Portal und testet den Worker deshalb nie gegen die echte API.

### Live-Tests

- Live getestet wird nur in **Testprojekten**, nie in Kundenprojekten.
- Downloads gehen nur nach **S7-PLCSIM Advanced**. Echte Steuerungen nur nach ausdrücklicher Zustimmung
  des Eigentümers.
- Ergebnisse kommen als Bericht nach `docs/superpowers/acceptance/reports/` mit Datum, Projekt, Schritten,
  Erwartung und Ergebnis, auch wenn etwas fehlschlägt.
- Wird eine Meldung eines anderen Projekts live bestätigt oder widerlegt, werden Doku und Guard-Texte
  angepasst.

## Pull Request öffnen

1. Repository forken und den Branch pushen.
2. Pull Request gegen `main` öffnen, Titel im Format der Commit-Nachrichten.
3. Die Vorlage ausfüllen: was und warum, wie geprüft (Platzhalter-Build, Build gegen installierte
   V21-Assemblies, Live-Test in TIA) und welche Auswirkungen auf Werkzeugverträge, Schreibsicherheit
   und Dokumentation es gibt.

Bereichs-Labels werden automatisch nach den geänderten Pfaden gesetzt. Ein Pull Request mit dem Label
`area: worker` berührt Code, der im Openness-Worker läuft, und wird vor dem Merge an einem echten
TIA Portal V21 geprüft.

## Fehler melden oder Funktionen wünschen

Bitte ein Issue über eines der [Issue-Formulare](https://github.com/Birger90/tia-portal-mcp-server/issues/new/choose)
öffnen: Fehlerbericht, Funktionswunsch oder Doku-Problem. Fragen und Ideen, die noch kein konkreter
Vorschlag sind, ebenfalls als Issue; Discussions sind in diesem Fork nicht aktiviert.

Fehler, die auch im Original auftreten, gerne zusätzlich bei
[Czarnak/tia-portal-mcp](https://github.com/Czarnak/tia-portal-mcp/issues) melden.

## Sicherheitslücken melden

Sicherheitslücken nie als öffentliches Issue melden. Der vertrauliche Weg steht in
[SECURITY.md](SECURITY.de.md); für Code dieses Forks über die
[Security Advisories](https://github.com/Birger90/tia-portal-mcp-server/security/advisories) des Forks.
