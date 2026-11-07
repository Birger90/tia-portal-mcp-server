# Leser der Siemens-Dokumentation (`siemens_docs`)

**Sprache:** [English](SIEMENS_DOCS_SUMMARY.md) · Deutsch

> **Nicht live getestet auf dem Windows-PC.** Das Tool wurde offline gebaut und getestet (Unit-Tests und
> MCP-Protokolltests gegen aufgezeichnete Antworten). Seine Endpunkte wurden am 2026-11-03 aus dem Linux-Cloud-Container
> gegen die Live-Seiten geprüft (siehe [Live-Prüfung der Endpunkte](#live-prüfung-der-endpunkte)), nicht vom
> Windows-PC des Eigentümers oder aus seinem Netz (Proxy, Firewall). Vor dem Einsatz dort die
> [Abnahmeschritte](#live-abnahme-auf-dem-windows-pc) ausführen.

`siemens_docs` lässt die KI die offizielle Siemens-Dokumentation direkt lesen:

- `docs.tia.siemens.cloud` (Präfix `tia`): TIA Portal, STEP 7, WinCC, Openness, Hardware-Handbücher, in mehreren
  Sprachen und Versionen;
- `docs.industrial-operations-x.siemens.cloud` (Präfix `iox`): SIMATIC AX, Industrial Edge, Insights Hub.

Es läuft im Prozess des MCP-Servers (`TiaMcpServer/SiemensDocs/`). Es braucht kein TIA Portal, keinen Worker und kein
gebundenes Projekt und steht in jedem Zugriffsmodus zur Verfügung (auch read-only). Seine vier Methoden `docs_search`,
`docs_read_page`, `docs_get_toc`, `docs_list_publications` sind in `OperationPolicyCatalog` `Observe` und tragen keine
Projektidentität.

Es ist eine eigene C#-Portierung der Ideen von [Czarnak/siemens-docs-mcp](https://github.com/Czarnak/siemens-docs-mcp)
(MIT). Das Python-Paket wird weder installiert noch ausgeliefert (Regel des Eigentümers: heruntergeladener fremder Code
läuft nicht auf seinem PC); siehe [THIRD_PARTY_NOTICES.de.md](../../THIRD_PARTY_NOTICES.de.md).

## Form des Tools

`siemens_docs(operations)` nimmt 1 bis 50 unabhängige Einträge `{ operationId, operation, ... }` wie die anderen
Lese-Tools; ein fehlgeschlagener Eintrag hält die anderen nicht auf. Die Antwort ist das gemeinsame Batch-Lesedokument
(`tool`, `contractVersion` `1.0`, `success`, `error`, `warnings`, `batch`) mit einem typisierten JSON-`result` je
Eintrag. Felder, die eine Operation nicht verwendet, werden vor jeder Netzwerkanfrage abgelehnt (`validation_error`).

| Operation | Felder (Pflicht fett) | Ergebnis |
| --- | --- | --- |
| `search` | **`query`** (1-500 Zeichen), `host` (`tia` Standard, `iox` oder ein anderer freigegebener Host), `product`, `version`, `locale` (Standard das Gebietsschema des Servers, `en-US`), `limit` (1-50, Standard 10), `page` (1-100, Standard 1) | `host`, `query`, `locale`, `page`, `totalResults`, `totalClusters`, `isLastPage`, `hits[]`: `title`, `publication`, `product`, `version`, `breadcrumb[]`, `excerpt` (reiner Text), `reference`, `readerUrl`, `otherVersions[]` (`publication`, `version`, `reference`: dasselbe Thema in anderen Publikationen oder Versionen, höchstens 10) |
| `read_page` | **`reference`**, `offset` (Standard 0), `maxChars` (1000-40000, Standard 20000) | `publication` (Titel, Produkt, Version, Gebietsschema, lastPublication, reference, readerUrl), `path[]` (Titel von der Publikation bis zur Seite), `title`, `reference`, `readerUrl`, `length` (ganze Seite), `offset`, `returned`, `nextOffset` (null am Ende), `markdown` |
| `get_toc` | **`reference`** (Publikation oder Thema), `depth` (1-6, Standard 2), `limit` (1-1000, Standard 300), `offset` | `publication`, `start`, `depth`, `total`, `offset`, `returned`, `nextOffset`, `nodes[]`: `level` (0 = Start), `title`, `reference`, `readerUrl`, `childCount` |
| `list_publications` | `host`, `product`, `version`, `locale`, `titleContains`, `limit` (1-200, Standard 50), `offset` | `host`, `total`, `offset`, `returned`, `nextOffset`, `publications[]` (Titel, Produkt, Version, Gebietsschema, lastPublication, reference, readerUrl), nach Titel sortiert |

`product` und `version` werden ohne Beachtung der Groß-/Kleinschreibung mit der Publikationsliste des Hosts verglichen;
ein unbekannter Wert scheitert mit ähnlichen Werten (zum Beispiel `TIA Portl` → `TIA Portal, TIAPortal`). Auf `tia`
wird die Version mit `tia:SoftwareVersionFilter` (`V21`, `V20`) verglichen, dann mit `SoftwareVersion`
(`21.00.00.00`); `iox`-Publikationen haben nur `SoftwareVersion` (`4.1.3`, `6.1`). Gültige Werte zeigt
`list_publications`.

**Referenzen.** Eine Referenz ist kompakt, `tia:<mapId>` oder `tia:<mapId>/<contentId>` (`iox:...` oder der volle
Hostname eines anderen freigegebenen Hosts als Präfix), oder eine https-Reader-URL eines freigegebenen Hosts: die
Id-Form `https://docs.tia.siemens.cloud/r/<mapId>/<contentId oder tocId>` (diese Form haben die Links in den Seiten)
oder eine Pretty-URL `https://docs.tia.siemens.cloud/r/en-us/v21/...` (Suchtreffer). Eine Referenz wird nur in Ids
zerlegt; die URL selbst wird nie abgerufen. Jedes Ergebnis enthält die kompakte `reference` und eine `readerUrl` für
Menschen.

**Blättern.** `read_page` schneidet an einem Zeilenumbruch im letzten Fünftel des Fensters, wenn es einen gibt, nie
innerhalb eines Surrogatpaars; weiter mit `offset = nextOffset`. `get_toc` und `list_publications` blättern mit
`offset`/`limit`. Das Antwortbudget des Servers (standardmäßig 60.000 Zeichen je Wert) gilt weiter für das JSON-kodierte Ergebnis, in dem
`<`, `>`, `&` und Anführungszeichen je sechs Zeichen belegen: Eine Seite mit 40.000 Zeichen voller XML kann es
überschreiten; der Eintrag wird dann mit dem Hinweis ausgelassen, `maxChars` zu senken (der Standard 20.000 lässt
für die meisten Seiten Platz).

## Umwandlung in Markdown

`SiemensDocsMarkdown` wandelt das HTML eines Themas mit einem eigenen toleranten Parser um (`SiemensDocsHtml`, kein
NuGet-Paket):

- Klassen der TIA-Hilfe: `BlocktitleFirst`/`Blocktitle` → `##`, `table_sourcecode`/`p_table_l_code` → Codeblock mit
  seinen Zeilenumbrüchen, `safety`-Tabellen → Zitatblöcke `> **Note**`, `p_table_title` → fette Tabellenüberschrift;
- MkDocs-Admonitions (`iox`) → `> **WARNING:** ...`; normale Tabellen → GFM-Tabellen (Senkrechtstriche maskiert,
  Absätze in Zellen mit `<br>` verbunden); verschachtelte Listen; `pre` → Codeblock (der Zaun wird länger als
  Backticks im Code);
- entfernt: Skripte, Styles, Navigation, Breadcrumbs, Feedback/Bewertung/Paginierung, Schaltflächen, die
  `ft-reader-*`-Elemente des Readers und das Titelbanner der TIA-Hilfe (`#nsbanner`, das der `#`-Titel wiederholt);
- Links: http(s) bleiben, wurzelrelative werden auf dem Host der Seite absolut, das leere `?section=` des Readers
  entfällt, andere Schemata (`javascript:`) werden auf ihren Text reduziert; `data:`-Bilder werden zu ihrem Alt-Text.

## Sicherheit und Grenzen

- **Host-Freigabeliste.** Nur die konfigurierten Hosts werden kontaktiert (Standard die beiden Siemens-Hosts). Jede
  Anfrage ist `https://<freigegebener Host>` plus ein fester API-Pfad aus geprüften Ids (`[\w~.-]`, nicht `.`/`..`,
  URL-maskiert). Eine Referenz mit anderem Host, `http`, Port, Benutzerangaben oder einem Pfad außerhalb von `/r/` ist
  ein `validation_error` vor jeder Anfrage. Keine Operation ruft eine beliebige URL ab.
- **Keine Weiterleitungen.** Der HTTP-Handler folgt keinen Weiterleitungen; eine 3xx-Antwort ist `docs_rejected`.
- **Ratenbegrenzung.** Anfragen an einen Host haben den Mindestabstand (Standard 300 ms); Hosts sind unabhängig.
- **Zeitlimits.** 15 s für den Verbindungsaufbau, 30 s je Anfrage, 120 s für die Publikationsliste.
- **Größengrenzen.** 8 MiB je Antwort, 64 MiB für die Publikationsliste (am 2026-11-03 16,6 MB auf
  `docs.tia.siemens.cloud`, 10,5 MB auf `docs.industrial-operations-x.siemens.cloud`), gezählt nach dem Entpacken.
- **Wiederholungen.** Eine nach 401/403 (mit neuer Web-Sitzung) und eine nach 429/5xx (Retry-After, höchstens 10 s).
- **Caches.** Publikationsliste und TOCs (höchstens 32) für 24 Stunden je Serverprozess; Einzelpublikationen höchstens
  256. Die Publikationsliste wird nur für `list_publications`, einen `product`-/`version`-Filter von `search`, eine
  Pretty-Reader-URL oder ein `read_page`/`get_toc`, nachdem sie ohnehin geladen ist, geladen; sonst dient der Endpunkt
  für eine einzelne Publikation.
- TLS prüft das System (kein Trust-All); außer den Sitzungs-Cookies des Hosts werden keine Anmeldedaten oder Cookies
  gesendet; nichts wird auf die Platte geschrieben.

## Konfiguration

Einmal beim Start aufgelöst (Kommandozeile > Umgebung > Standard) und als `TIA MCP Siemens documentation: ...`
protokolliert; ein ungültiger Wert verhindert den Start.

| Option | Umgebungsvariable | Standard | Werte |
| --- | --- | --- | --- |
| `--docs-hosts` | `TIA_MCP_DOCS_HOSTS` | `docs.tia.siemens.cloud,docs.industrial-operations-x.siemens.cloud` | kommagetrennte reine DNS-Hostnamen; die Liste ersetzt den Standard (sie kann ihn einschränken); kein Schema, Port, Pfad, keine IP-Adresse und kein `localhost`; höchstens 8; der erste ist der Standard-Host |
| `--docs-locale` | `TIA_MCP_DOCS_LOCALE` | `en-US` | ein Kulturname wie `de-DE` (normalisiert zu `de-DE`) |
| `--docs-min-interval-ms` | `TIA_MCP_DOCS_MIN_INTERVAL_MS` | `300` | 0-60000 |

Die Referenzimplementierung liest `SIEMENS_DOCS_HOSTS` (zu den Standards hinzugefügt), `SIEMENS_DOCS_LOCALE` und
`SIEMENS_DOCS_MIN_INTERVAL` (Sekunden); dieser Fork verwendet eigene `TIA_MCP_*`-Namen und die Ersetzungsregel oben.

## Fehlerkategorien

| Kategorie | Bedeutung |
| --- | --- |
| `validation_error` | Argumentform, eine nicht freigegebene oder nicht auflösbare Referenz, ein unbekanntes Produkt/eine unbekannte Version/ein unbekanntes Gebietsschema, ein Offset hinter dem Ende |
| `target_not_found` | Der Host antwortete 404, oder das Thema steht nicht im TOC der Publikation (`get_toc`) |
| `docs_unreachable` | Verbindungs-, DNS- oder TLS-Fehler oder ein Zeitlimit |
| `docs_rejected` | Ein anderer HTTP-Fehlerstatus (mit dem Status in der Meldung) oder eine Weiterleitung |
| `docs_response_invalid` | Die Antwort überschritt die Größengrenze oder war nicht das erwartete JSON (die Seite hat sich geändert); die Rohantwort wird nie wiedergegeben |

## Live-Prüfung der Endpunkte

Am 2026-11-03 mit curl aus dem Cloud-Container geprüft; gekürzte Antworten sind die Test-Fixtures
(`TiaMcpServer.Tests/SiemensDocs/Fixtures`, Herkunft in `SiemensDocsFixtures.cs`). Das Testprogramm, das danach den
C#-Dienst gegen die Live-Seiten aufrief, erhielt für alle vier Operationen dieselben Formen.

| Anfrage | Status | Was übereinstimmte, was abwich |
| --- | --- | --- |
| `GET https://docs.tia.siemens.cloud/internal/api/webapp/authentication/session` (und der `iox`-Host) | 200 | `{"sessionAuthenticated":false,"message":"No authenticated session"}` plus Load-Balancer-Cookies (`INGRESSCOOKIE`, `AWSALB`, `AWSALBCORS`). **Abweichung:** Jeder API-Endpunkt unten antwortete auch ohne diese Cookies mit 200; eine fehlgeschlagene Sitzungsanfrage ist hier deshalb nicht fatal (die Referenz bricht ab). |
| `GET /api/khub/maps` | 200 | Array aus `{title, id, mapApiEndpoint, metadata[{key,label,values}]}`, 4089 Publikationen, 16,6 MB, etwa 6 s (`iox`: 2387, 10,5 MB, 11 s; gzip angeboten). Verwendete Schlüssel: `ft:locale`, `Product`, `tia:SoftwareVersionFilter`, `SoftwareVersion`, `ft:prettyUrl` (ohne `/r/`), `ft:lastPublication`. `iox` hat kein `tia:SoftwareVersionFilter`; Pretty-URLs können geteilt sein (`en-us/notification-api` von sechs Publikationen). |
| `GET /api/khub/maps/{id}` | 200 / 404 | Eine Publikation mit denselben `metadata` (plus `readerUrl`, `prettyUrl`, ...). **Von der Referenz nicht verwendet;** hier verwendet, damit eine Referenz die 16-MB-Liste nicht braucht. Unbekannte Id: 404-JSON `{"status":404,"error":"Not Found",...}`. |
| `GET /api/khub/maps/{id}/pages` | 200 | `{configuration, paginatedToc, filteredTocNodes, translationError}`. TIA: eine Wurzel mit `pageToc` (6) und leerem `children`; tiefere Knoten verwenden `children` (`tocId`, `contentId`, `title`, `prettyUrl`, `hasRating`). `iox`: drei Abschnitte auf oberster Ebene (keine Wurzel), wie die Referenz erwartet. |
| `GET /api/khub/maps/{id}/topics/{contentId}/content?target=DESIGNED_READER` | 200 / 404 | `text/html`-Fragment; dieselben Bytes ohne `target` und ohne Cookies. **Abweichung:** Codezeilen in `p_table_l_code` sind durch `<br>` innerhalb eines Absatzes getrennt (`get_text()` der Referenz fügte ganze Listings zu einer Zeile zusammen); jedes TIA-Thema beginnt mit einer Titeltabelle `#nsbanner` (die Referenz gab sie als einzellige Tabelle aus); Tabellennamen stehen in `p_table_title` (die Referenz ließ sie weg). Interne Links haben die Form `https://docs.tia.siemens.cloud/r/<mapId>/<tocId>?section=`. |
| `POST /api/khub/clustered-search` mit `{query, contentLocale, paging{page, perPage}, filters?}` | 200 | `{facets, results[{entries[{type:"TOPIC", topic{mapId, contentId, tocId, title, mapTitle, breadcrumb, htmlExcerpt, readerUrl, metadata, ...}}]}], paging{currentPage, isLastPage, totalResultsCount, totalClustersCount}}`. `perPage` zählt Cluster; ein Cluster enthält dasselbe Thema in mehreren Versionen, **und sein erster Eintrag kann die ältere Version sein** (V20 vor V21), deshalb werden alle Einträge geliefert (`otherVersions`) statt nur des ersten. Die Filter `Product`=`TIAPortal`, `tia:SoftwareVersionFilter`=`V21` wirken; ein unbekanntes Gebietsschema liefert 200 ohne Treffer. |
| `GET /r/<mapId>`, `/r/<mapId>/<contentId>` | 301 / 200 | Reader-Seiten (eine Single-Page-App); nur als `readerUrl` für Menschen verwendet, vom Tool nie abgerufen. |

## Weggelassen

- Die Export-CLI der Referenz (`siemens-docs-export`, `writer.py`, `configs/*.yaml`, `html_to_markdown_tia.py`): Sie
  schreibt ganze Publikationen und Bilder auf die Platte; das Tool liest nur ins Gespräch.
- `reference_format` (kompakt oder URL): Jedes Ergebnis enthält beides.
- Bilder: Kein Bild wird heruntergeladen oder eingebettet; https-Bildlinks bleiben Markdown-Links.
- Eine `doctor`-Prüfung der Dokumentations-Hosts (die Einstellungen werden beim Start protokolliert).

## Live-Abnahme auf dem Windows-PC

1. Server normal starten; die Logzeile `TIA MCP Siemens documentation:` nennt beide Hosts.
2. `siemens_docs` `search` `query:"ExportAsDocuments"`, `product:"TIAPortal"`, `version:"V21"`: Treffer mit
   V21-Referenzen (der erste Aufruf mit Filter wartet auf die 16-MB-Publikationsliste).
3. `read_page` mit der `reference` des ersten Treffers: Markdown mit mehrzeiligen Codeblöcken; Seite mit `nextOffset`.
4. `get_toc` `reference:"tia:gpR5ZkKnLSuzVoGX1ovoKg"`, `depth:1`; `read_page` einer `iox`-Seite.
5. Hinter einem Firmen-Proxy: `docs_unreachable` nennt die Ursache; der Server verwendet die Proxy-Einstellungen des
   Systems.
