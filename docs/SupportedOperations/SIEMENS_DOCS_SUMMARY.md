# Siemens documentation reader (`siemens_docs`)

**Language:** English · [Deutsch](SIEMENS_DOCS_SUMMARY.de.md)

> **Not live-tested on the Windows PC.** The tool was built and tested offline (unit tests and MCP protocol tests
> against recorded responses). Its endpoints were checked against the live sites from the Linux cloud container on
> 2026-11-03 (see [Live endpoint checks](#live-endpoint-checks)), not from the owner's Windows PC or its network
> (proxy, firewall). Run the [acceptance steps](#live-acceptance-on-the-windows-pc) there before relying on it.

`siemens_docs` lets the AI read the official Siemens documentation directly:

- `docs.tia.siemens.cloud` (prefix `tia`): TIA Portal, STEP 7, WinCC, Openness, hardware manuals, in several
  languages and versions;
- `docs.industrial-operations-x.siemens.cloud` (prefix `iox`): SIMATIC AX, Industrial Edge, Insights Hub.

It runs in the MCP server process (`TiaMcpServer/SiemensDocs/`). It needs no TIA Portal, no worker and no bound
project, and it is available in every access mode (read-only included). Its four methods `docs_search`,
`docs_read_page`, `docs_get_toc`, `docs_list_publications` are `Observe` in `OperationPolicyCatalog` and carry no
project identity.

It is an own C# port of the ideas of [Czarnak/siemens-docs-mcp](https://github.com/Czarnak/siemens-docs-mcp) (MIT).
The Python package is not installed or shipped (owner rule: downloaded external code does not run on his PC); see
[THIRD_PARTY_NOTICES.md](../../THIRD_PARTY_NOTICES.md#czarnaksiemens-docs-mcp-mit).

## Tool shape

`siemens_docs(operations)` takes 1 to 50 independent items `{ operationId, operation, ... }` like the other read
tools; a failed item does not stop the others. The response is the shared batch read document (`tool`,
`contractVersion` `1.0`, `success`, `error`, `warnings`, `batch`) with a typed JSON `result` per item. Members an
operation does not use are refused (`validation_error`) before any network request.

| Operation | Members (required in bold) | Result |
| --- | --- | --- |
| `search` | **`query`** (1-500 characters), `host` (`tia` default, `iox` or another allowed host), `product`, `version`, `locale` (default the server locale, `en-US`), `limit` (1-50, default 10), `page` (1-100, default 1) | `host`, `query`, `locale`, `page`, `totalResults`, `totalClusters`, `isLastPage`, `hits[]`: `title`, `publication`, `product`, `version`, `breadcrumb[]`, `excerpt` (plain text), `reference`, `readerUrl`, `otherVersions[]` (`publication`, `version`, `reference`: the same topic in other publications or versions, at most 10) |
| `read_page` | **`reference`**, `offset` (default 0), `maxChars` (1000-40000, default 20000) | `publication` (title, product, version, locale, lastPublication, reference, readerUrl), `path[]` (titles from the publication to the page), `title`, `reference`, `readerUrl`, `length` (whole page), `offset`, `returned`, `nextOffset` (null at the end), `markdown` |
| `get_toc` | **`reference`** (publication or topic), `depth` (1-6, default 2), `limit` (1-1000, default 300), `offset` | `publication`, `start`, `depth`, `total`, `offset`, `returned`, `nextOffset`, `nodes[]`: `level` (0 = start), `title`, `reference`, `readerUrl`, `childCount` |
| `list_publications` | `host`, `product`, `version`, `locale`, `titleContains`, `limit` (1-200, default 50), `offset` | `host`, `total`, `offset`, `returned`, `nextOffset`, `publications[]` (title, product, version, locale, lastPublication, reference, readerUrl), sorted by title |

`product` and `version` are matched case-insensitively against the host's publication list; an unknown value fails
with close matches (for example `TIA Portl` → `TIA Portal, TIAPortal`). On `tia` the version is matched against
`tia:SoftwareVersionFilter` (`V21`, `V20`), then `SoftwareVersion` (`21.00.00.00`); `iox` publications have only
`SoftwareVersion` (`4.1.3`, `6.1`). Use `list_publications` to see valid values.

**References.** A reference is compact, `tia:<mapId>` or `tia:<mapId>/<contentId>` (`iox:...`, or the full host name
of another allowed host as prefix), or an https reader URL of an allowed host: the id form
`https://docs.tia.siemens.cloud/r/<mapId>/<contentId or tocId>` (the links inside pages have this form) or a pretty
URL `https://docs.tia.siemens.cloud/r/en-us/v21/...` (search hits). A reference is only parsed into ids; the URL is
never fetched as given. Every result carries both the compact `reference` and a `readerUrl` for a person.

**Paging.** `read_page` cuts at a line break in the last fifth of the window when there is one, never inside a
surrogate pair; continue with `offset = nextOffset`. `get_toc` and `list_publications` page with `offset`/`limit`.
The response budget of the server (60,000 characters per value by default) still applies to the JSON-encoded result,
in which `<`, `>`, `&` and quotes take six characters each: a 40,000-character page full of XML can exceed it, and the
item is then omitted with the guidance to lower `maxChars` (the default 20,000 leaves room for most pages).

## Markdown conversion

`SiemensDocsMarkdown` converts the topic HTML with an own tolerant parser (`SiemensDocsHtml`, no NuGet package):

- TIA help classes: `BlocktitleFirst`/`Blocktitle` → `##`, `table_sourcecode`/`p_table_l_code` → fenced code with
  its line breaks, `safety` tables → `> **Note**` blockquotes, `p_table_title` → a bold caption;
- MkDocs admonitions (`iox`) → `> **WARNING:** ...`; regular tables → GFM tables (pipes escaped, cell paragraphs
  joined with `<br>`); nested lists; `pre` → fenced code (the fence grows past backticks in the code);
- removed: scripts, styles, navigation, breadcrumbs, feedback/rating/pagination, buttons, the reader's
  `ft-reader-*` elements and the TIA title banner (`#nsbanner`, repeated by the `#` title);
- links: http(s) kept, root-relative made absolute on the page's host, the reader's empty `?section=` dropped,
  other schemes (`javascript:`) reduced to their text; `data:` images become their alt text.

## Security and limits

- **Host allowlist.** Only the configured hosts are contacted (default the two Siemens hosts). Every request is
  `https://<allowed host>` plus a fixed API path built from validated ids (`[\w~.-]`, not `.`/`..`, URL-escaped).
  A reference with another host, `http`, a port, user info or a path outside `/r/` is a `validation_error` before
  any request. There is no operation that fetches an arbitrary URL.
- **No redirects.** The HTTP handler does not follow redirects; a 3xx answer is `docs_rejected`.
- **Rate limit.** Requests to one host are spaced by the minimum interval (default 300 ms); hosts are independent.
- **Timeouts.** 15 s to connect, 30 s per request, 120 s for the publication list.
- **Size caps.** 8 MiB per response, 64 MiB for the publication list (16.6 MB on `docs.tia.siemens.cloud`,
  10.5 MB on `docs.industrial-operations-x.siemens.cloud` on 2026-11-03), counted after decompression.
- **Retries.** One after 401/403 (with a new web session) and one after 429/5xx (Retry-After, at most 10 s).
- **Caches.** Publication list and TOCs (at most 32) for 24 hours per server process; single publications at most 256.
  The publication list is loaded only for `list_publications`, a `product`/`version` filter of `search`, a pretty
  reader URL, or a `read_page`/`get_toc` after it was loaded anyway; otherwise the single-publication endpoint is used.
- TLS is validated by the system (no trust-all); no credentials or cookies other than the session cookies of the
  host are sent; nothing is written to disk.

## Configuration

Resolved once at startup (command line > environment > default) and logged as `TIA MCP Siemens documentation: ...`;
an invalid value refuses startup.

| Option | Environment variable | Default | Values |
| --- | --- | --- | --- |
| `--docs-hosts` | `TIA_MCP_DOCS_HOSTS` | `docs.tia.siemens.cloud,docs.industrial-operations-x.siemens.cloud` | comma-separated plain DNS host names; the list replaces the default (it can narrow it); no scheme, port, path, IP address or `localhost`; at most 8; the first is the default host |
| `--docs-locale` | `TIA_MCP_DOCS_LOCALE` | `en-US` | a culture name such as `de-DE` (normalized to `de-DE`) |
| `--docs-min-interval-ms` | `TIA_MCP_DOCS_MIN_INTERVAL_MS` | `300` | 0-60000 |

The reference implementation reads `SIEMENS_DOCS_HOSTS` (added to the defaults), `SIEMENS_DOCS_LOCALE` and
`SIEMENS_DOCS_MIN_INTERVAL` (seconds); this fork uses its own `TIA_MCP_*` names and the replace rule above.

## Failure categories

| Category | Meaning |
| --- | --- |
| `validation_error` | Argument shape, a reference that is not allowed or cannot be resolved, an unknown product/version/locale, an offset past the end |
| `target_not_found` | The host answered 404, or the topic is not in the publication's TOC (`get_toc`) |
| `docs_unreachable` | Connection, DNS or TLS failure, or a timeout |
| `docs_rejected` | Another HTTP error status (with the status in the message) or a redirect |
| `docs_response_invalid` | The response exceeded the size cap or was not the expected JSON (the site changed); the raw response is never echoed |

## Live endpoint checks

Checked with curl from the cloud container on 2026-11-03; trimmed responses are the test fixtures
(`TiaMcpServer.Tests/SiemensDocs/Fixtures`, provenance in `SiemensDocsFixtures.cs`). The scratch program that called
the C# service against the live sites afterwards returned the same shapes for all four operations.

| Request | Status | What matched, what differed |
| --- | --- | --- |
| `GET https://docs.tia.siemens.cloud/internal/api/webapp/authentication/session` (and the `iox` host) | 200 | `{"sessionAuthenticated":false,"message":"No authenticated session"}` plus load-balancer cookies (`INGRESSCOOKIE`, `AWSALB`, `AWSALBCORS`). **Differs:** every API endpoint below also answered 200 without these cookies; a failed session request is therefore not fatal here (the reference stops). |
| `GET /api/khub/maps` | 200 | Array of `{title, id, mapApiEndpoint, metadata[{key,label,values}]}`, 4089 publications, 16.6 MB, about 6 s (`iox`: 2387, 10.5 MB, 11 s; gzip offered). Keys used: `ft:locale`, `Product`, `tia:SoftwareVersionFilter`, `SoftwareVersion`, `ft:prettyUrl` (without `/r/`), `ft:lastPublication`. `iox` has no `tia:SoftwareVersionFilter`; pretty URLs can be shared (`en-us/notification-api` by six publications). |
| `GET /api/khub/maps/{id}` | 200 / 404 | One publication with the same `metadata` (plus `readerUrl`, `prettyUrl`, ...). **Not used by the reference;** used here so a reference does not need the 16 MB list. Unknown id: 404 JSON `{"status":404,"error":"Not Found",...}`. |
| `GET /api/khub/maps/{id}/pages` | 200 | `{configuration, paginatedToc, filteredTocNodes, translationError}`. TIA: one root with `pageToc` (6) and an empty `children`; deeper nodes use `children` (`tocId`, `contentId`, `title`, `prettyUrl`, `hasRating`). `iox`: three top-level sections (no root), as the reference expects. |
| `GET /api/khub/maps/{id}/topics/{contentId}/content?target=DESIGNED_READER` | 200 / 404 | `text/html` fragment; same bytes without `target` and without cookies. **Differs:** code lines in `p_table_l_code` are separated by `<br>` inside one paragraph (the reference's `get_text()` joined whole listings into one line); every TIA topic starts with a `#nsbanner` title table (the reference rendered it as a one-cell table); table names sit in `p_table_title` (the reference dropped them). Internal links are `https://docs.tia.siemens.cloud/r/<mapId>/<tocId>?section=`. |
| `POST /api/khub/clustered-search` with `{query, contentLocale, paging{page, perPage}, filters?}` | 200 | `{facets, results[{entries[{type:"TOPIC", topic{mapId, contentId, tocId, title, mapTitle, breadcrumb, htmlExcerpt, readerUrl, metadata, ...}}]}], paging{currentPage, isLastPage, totalResultsCount, totalClustersCount}}`. `perPage` counts clusters; a cluster holds the same topic in several versions, **and its first entry can be the older version** (V20 before V21), so all entries are returned (`otherVersions`) instead of the first only. Filters `Product`=`TIAPortal`, `tia:SoftwareVersionFilter`=`V21` work; an unknown locale returns 200 with no results. |
| `GET /r/<mapId>`, `/r/<mapId>/<contentId>` | 301 / 200 | Reader pages (a single-page app); used only as `readerUrl` for people, never fetched by the tool. |

## Left out

- The reference's export CLI (`siemens-docs-export`, `writer.py`, `configs/*.yaml`, `html_to_markdown_tia.py`): it
  writes whole publications and images to disk; the tool reads only into the conversation.
- `reference_format` (compact or URL): every result carries both.
- Images: no image is downloaded or embedded; https image links stay as Markdown links.
- A `doctor` check for the documentation hosts (the settings are logged at startup).

## Live acceptance on the Windows PC

1. Start the server normally; the log line `TIA MCP Siemens documentation:` lists both hosts.
2. `siemens_docs` `search` `query:"ExportAsDocuments"`, `product:"TIAPortal"`, `version:"V21"`: hits with V21
   references (the first call with a filter waits for the 16 MB publication list).
3. `read_page` with the first hit's `reference`: Markdown with code blocks on several lines; page with `nextOffset`.
4. `get_toc` `reference:"tia:gpR5ZkKnLSuzVoGX1ovoKg"`, `depth:1`; `read_page` of an `iox` page.
5. Behind a company proxy: `docs_unreachable` names the cause; the server uses the system proxy settings.
