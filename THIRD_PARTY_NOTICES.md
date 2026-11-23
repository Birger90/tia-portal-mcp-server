# Third-party code in this fork

**Language:** English · [Deutsch](THIRD_PARTY_NOTICES.de.md)

This fork is based on [Czarnak/tia-portal-mcp](https://github.com/Czarnak/tia-portal-mcp)
(MIT; its notice is kept in `LICENSE-MIT`). Because it contains code from rickgaiser/TiaMcp
(AGPL-3.0), the fork as a whole is licensed under the GNU Affero General Public License v3.0 only
(`LICENSE`). Code from other projects may only enter under the rules below.

## Publication (owner decision 2026-11-01)

- The fork is published at <https://github.com/Birger90/tia-portal-mcp-server> under
  AGPL-3.0-only, with its complete corresponding source.
- Anyone who conveys the fork or a package built from it, or offers it to users over a network
  (§13), must do so under AGPL-3.0 with the corresponding source.
- The MIT notice of the base code stays in `LICENSE-MIT` and is shipped in the package next to
  `LICENSE`.

## rickgaiser/TiaMcp (AGPL-3.0)

- Source: <https://github.com/rickgaiser/TiaMcp>, licence GNU Affero General Public License v3.0.
- Owner decision 2026-11-01: code from this project may be adopted. The same day the owner decided
  to publish the fork under AGPL-3.0-only (see above), which is the condition AGPL-3.0 sets for
  conveying a work that contains such code.
- Every adopted file or section keeps a header naming the origin, the upstream commit and the
  licence, for example:

  ```
  // Derived from rickgaiser/TiaMcp <path> at <commit> (AGPL-3.0-only). See THIRD_PARTY_NOTICES.md.
  ```

- Adopted code is held to this fork's rules: every Siemens member it calls is verified against the
  Siemens sources listed in `AGENTS.md`, and it goes through the guarded write pipeline and tests.
- TIA Portal behaviour reported by that project (crashes, silent import errors) is treated as
  "reported, not verified" until checked live on V21.

### Adopted so far

| Fork file | Upstream file at `8e9de1c` | What was adopted |
| --- | --- | --- |
| `TiaMcpServer.Contracts/Plc/Interface/S7dclImportHazards.cs` | `src/TiaMcp.Formats/S7dclSource.cs` | The system type list, the declaration, network and language patterns, the nested-index scan and the SimaticML AT-overlay query (`FindWriteProblems`, `NestedIndexLine`, `AtOverlays`, the line pattern of `WithAtMarkers`) and the content of its finding texts, reworded as reported behaviour. Typed findings, the declared-overlay check, the textual-interface check and the re-export comparison are this fork's. |
| `TiaMcpServer.Tests/Plc/S7dclImportHazardsTests.cs` | `tests/TiaMcp.Tests/S7dclSourceTests.cs` | The sample block, the declaration and parameter cases and the AT overlay sample. |
| `TiaMcpServer.OpennessWorker/Openness/Hmi/HmiTextListFiles.cs` (`ExportYaml`, `ImportYaml`; upstream at `5bcb2ab`, 2026-11-12) | `src/TiaMcp.Openness/TiaConnection.HmiTextLists.cs` (`ExportHmiTextLists`, `WriteHmiTextLists`) | The V21 members `HmiTextListComposition.Export(DirectoryInfo, String)` (returns the written files) and `Import(DirectoryInfo, String)` (returns a Boolean), the base file name `textlists` with the suffixes `.hmi.yml`/`.TextLibrary.hmi.yml` TIA Portal adds, the check that both files came back and the failure on a false import. Runtime binding, the fallback order after the SimaticML route, file placement with hashes, the sibling check of an import and the tests are this fork's. The entry-level read and write of Unified text lists (YAML parser and patcher) is not adopted yet. |

Ideas only (no code): the export-counting rules of `src/TiaMcp.Openness/TiaConnection.cs`
(`ExportsSinceImport`) behind `BlockExportLedger`, and the no-`Attach()` listing of
`ListRunningInstances` behind `otherClientCount` of `list_portals`, and the re-attach after TIA Portal reopened the project of commit `38e63db` ("Attach again by itself when TIA Portal
reopened the project") behind `ReadReattachPolicy` (reads only, the same open project, once; reimplemented).

## BetterTIA (MIT)

- Source: the four repositories of <https://github.com/BetterTIA> (owner request 2026-11-01), cloned read-only and
  never built or run: `watch` at `1978f24b330f265d8ed2a67e9b1d673d1b4122d8`, `writer` at
  `fc8179c9b59d364961f2255d07d05af893c32837`, `reader` at `246d7c54772a502e854630e003e26fa37f92aeaf`, `editor` at
  `3d115b30035ad5ea2f10206e5354190ea1ba4de5`. Each repository's licence file was read before anything was copied: all
  four are MIT.
- MIT permits copying and adapting with the copyright and permission notice kept. Every adapted file carries an
  origin header with the repository, path, commit and copyright line; the notices are reproduced below.
- Their Siemens calls are held to this fork's rules like any other code: members outside the compile stubs and the
  Siemens skills are bound at runtime, and TIA Portal behaviour they report is "reported by BetterTIA, not verified".
  See the [BetterTIA operations reference](docs/SupportedOperations/BETTERTIA_ADOPTED_OPERATIONS_SUMMARY.md).

### Adopted so far

| Fork file | Upstream file | What was adopted |
| --- | --- | --- |
| `TiaMcpServer.Contracts/Plc/Interface/PlcDbSnapshotDocument.cs` | watch `src/BtiaWatch/WatchTools.cs` (`FormatSnapshot`, `IsAtOrBelow`, `TopLevel`) | The snapshot export reader (the `Snapshot`/`Value`/`SnapshotDate` layout), the at-or-below member filter and the top-level hint; typed values and the value cap are this fork's. |
| `TiaMcpServer.Contracts/Plc/Interface/SimaticMlNetworkTexts.cs` | writer `src/TiaMcpServer/Siemens/SimaticMl.cs` (`Networks`, `Texts`, `SetText`, `SetNetworkText`, `ReplaceAll`, `ReplaceNetworkText`) | The network text model, the one-pass longest-first replacement and the GRAPH text scope; typed change records with re-locatable keys and the creation of a missing language item are this fork's. |
| `TiaMcpServer.Contracts/Plc/Interface/SimaticMlMemberRenames.cs` | writer `src/TiaMcpServer/Siemens/SimaticMl.cs` (`RenameMembers`: the `Symbol`/`Instance` component walk, `Segment`, `Operand`, `PlainName`) | The local-access walk and the GRAPH action operand patterns; limiting it to the object's own document, the temporary-name swaps on the existing interface edit model and the cross-section name check are this fork's. |
| `TiaMcpServer.Contracts/Plc/PlcBatchEdits.cs` (`PlcBatchRenamePlan`) | writer `src/TiaMcpServer/Siemens/BatchRename.cs` (`BatchRename.Plan`) | The ordering with temporary names for swaps, rotations and chains; the collision check against names outside the batch was dropped (TIA Portal decides). |
| `TiaMcpServer.OpennessWorker/Openness/Plc/PlcBatchEditService.cs` | writer `src/TiaMcpServer/Siemens/Portal.AnetFork.cs` (`RenameTags`, `RenameObjects`, `RenameBatch`) | The lookup by name (tags across tables; blocks, then technology objects, then types) and the batch sequence; re-written with the generic Name attribute, one ExclusiveAccess transaction and TIA Portal's fail-safe rule. |
| `TiaMcpServer.OpennessWorker/Openness/Plc/PlcBatchEditService.cs` (`ReplaceTagComments`) | writer `src/TiaMcpServer/Siemens/Portal.AnetFork.cs` (`EditTagComments`, `EditComments`) | The walk over `Comment.Items` per language with the replacement; the transaction and read-back are this fork's. |
| `TiaMcpServer.OpennessWorker/Openness/Hmi/HmiScreenWriter.cs` | writer `src/TiaMcpServer/Siemens/Portal.AnetFork.HmiScreens.cs`, `Portal.AnetFork.Hmi.cs` (screen and item create/delete, `SetHmiItemAttribute`, `SetHmiDynamizationTag`, `ReplaceInHmiScripts`, `RenameHmiTags`, `RenameHmiScreens`, the `Children`/`Spots` screen walk) | The call sequences and the script walk (dynamizations, event and property-event handlers, four item levels); re-written with runtime binding, exact type names, read-back and one transaction for batches. |
| `TiaMcpServer.OpennessWorker/Openness/Block/PlcBlockEditService.Snapshot.cs` | watch `src/BtiaWatch/OpennessPortal.cs` (`ExportDbSnapshot`) | The call sequence `Interface` → `ValueService.CreateSnapshot()`, block → `InterfaceSnapshot.Export(FileInfo, ExportOptions.WithReadOnly)` and the offline-refusal rule; re-written with runtime binding. |
| `TiaMcpServer.FakeWorker/Plc/SimaticSdFixtures.cs` (`BetterTiaFbSafety`, `BetterTiaFcBranches`; test data only) | editor `tests/samples/Demo_Line/20261012-120000/PLC_1/Blocks/Safety/FB_Safety.s7dcl`/`.s7res`, `Conveyor/FC_Branches.s7dcl`/`.s7res` at `3d115b3` | The two V21 SIMATIC SD exports, byte for byte (fail-safe FBD with `S7_Safety`, LAD branches and an FBD network), as fixtures of the SIMATIC SD parser and the FakeWorker; no code was adopted. |
| `TiaMcpServer.Contracts/Plc/ChangeDetection/PlcChangeDetection.cs` (`PlcDownloadReadiness`), `TiaMcpServer.OpennessWorker/Openness/Plc/ChangeDetection/PlcChangeDetectionService.cs` (`plc_read check_download_readiness`) | `CheckDownloadReadiness` (CHANGELOG 2.5.3) | The idea only: a download readiness check before a download. Reimplemented from `PlcBlock.IsConsistent` and `PlcChecksumProvider.Software` (Siemens.Engineering.Step7.xml, V21); it never compiles, unlike a compile-based check. |

### Notices

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

The same MIT text applies with `Copyright (c) 2025 J.Heilingbrunner` (BetterTIA/writer, `LICENSE.txt`) to the files
adopted from `writer`.

## 1748477582/tia-portal-openness-mcp (MIT)

- Source: <https://github.com/1748477582/tia-portal-openness-mcp> at `8e7ca425d5d257132ac8cd5ed9cbc318a7c340e2`, cloned
  read-only and never built or run (owner request 2026-11-01). Its `LICENSE` was read before anything was copied: MIT,
  `Copyright (c) 2026 1748477582`.
- Test data and one feature idea were taken (no code); its skill text (`tools/tiaportal-mcp/skill/SKILL.md` §9a) is cited as reported V21 behaviour,
  "reported, not verified", in the [SIMATIC SD reference](docs/SupportedOperations/PLC_SIMATIC_SD_SUMMARY.md).

### Adopted so far

| Fork file | Upstream file | What was adopted |
| --- | --- | --- |
| `TiaMcpServer.FakeWorker/Plc/SimaticSdFixtures.cs` (`CookbookFbLadV3`, `CookbookFcLad`, `CookbookMixed`; test data only) | `tools/tiaportal-mcp/skill/lad-cookbook/MCPVerify_FB_LAD_v3.s7dcl`/`.s7res`, `MCPVerify_FC_LAD.s7dcl`/`.s7res`, `MCPVerify_Mixed_LADSCL.s7dcl`/`.s7res` | Three V21 SIMATIC SD document pairs, byte for byte (UTF-8 BOM, CRLF, multi-line network pragmas, `S7_Templates`, a mixed LAD/SCL block), as fixtures; no code was adopted. |

### Notice

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

- Source: <https://github.com/heilingbrunner/tiaportal-mcp> at `72d2ade` (2026-11-01), cloned read-only and never built
  or run. Its `LICENSE.txt` was read first: MIT, `Copyright (c) 2025 J.Heilingbrunner` (the same notice as BetterTIA/writer
  above applies).
- Only feature ideas were taken; no code was copied. Every Siemens member is taken from the Siemens V21 reference.

### Adopted so far

| Fork file | Idea from | What was adopted |
| --- | --- | --- |
| `TiaMcpServer.Contracts/Plc/PlcOpcUaExport.cs`, `TiaMcpServer.OpennessWorker/Openness/Plc/OpcUaExportService.cs` (`plc_write export_opcua_xml`) | commit `f4eb4d7` "feat: add ExportOpcUaXml tool" (`src/TiaMcpServer/Siemens/Portal.OpcUa.cs`) | The feature: export the OPC UA data of one PLC as XML. Reimplemented from `Project.HwUtilities.Find("OPCUAExportProvider")` and `OpcUaExportProvider.Export(DeviceItem, FileInfo)` as documented in `totally-integrated-claude` `skills/tia-import-export/references/plc-blocks.md` §13 and `skills/tia-devices-general/references/device-item-operations.md` (type listed in `api-baselines/v21/base.json`), bound at runtime; the guarded `plc_write` pipeline, file placement and tests are this fork's. |

## asckye/TIA_Portal_Openness_MCP (MIT)

- Source: <https://github.com/asckye/TIA_Portal_Openness_MCP> at `4a0592e8` (2026-11-05), cloned read-only and never
  built or run. Its `LICENSE` was read first: MIT, `Copyright (c) 2026 bulaofen0036-coder`.
- Only feature ideas were taken; no code was copied. Every Siemens member is taken from the Siemens V21 reference.

### Adopted so far

| Fork file | Idea from | What was adopted |
| --- | --- | --- |
| `TiaMcpServer.Contracts/Shared/ServiceOwnerChain.cs`, `TiaMcpServer.OpennessWorker/Openness/Plc/Loadable/PlcLoadableService.cs` (`plc_write generate_loadable`) | commit `93eeb016` (VMFIX-5b, "PLC service lookup", `src/Adapters/Native/Plc/PlcOrganisationWorker.cs`) | The idea: look for a service that V21 does not attach to a documented owner on the PLC software, then on the CPU device item and its ancestors, then on the device. Reimplemented as a Siemens-free ordered search over `GetServiceInfos()`, applied to `LoadableProvider`. |
| `TiaMcpServer/Plc/TechnologyObjects/` (info guard `plc_technology_object_create_may_terminate`) | commit `93eeb016` (`src/Adapters.Contracts/PlcB4Capabilities.cs`) | The observation reported there (not verified here): on V21 a technology-object create with a type/version pair outside TO_PositioningAxis 5.0, TO_SpeedAxis 5.0 and PID_Compact 2.3 terminated TIA Portal. asckye refuses such creates; this fork only informs (owner rule: never stricter than TIA Portal). |

## jessenic/TIA-MCP-Server (MIT)

- Source: <https://github.com/jessenic/TIA-MCP-Server> at `e817033` (2026-11-13), cloned read-only and never built or run.
  Its `LICENSE` was read first: MIT, `Copyright (c) 2026 TIA Openness MCP Contributors`.
- Only the feature and its call order were taken; no code was copied. Every Siemens member is taken from the V21 member
  list in `totally-integrated-claude` `skills/tia-hmi-operations/references/unified-dynamization.md` and bound at runtime.
  TIA Portal behaviour jessenic reports (the Singlebit pair created on `ConditionType` assignment) is "reported by
  jessenic, not verified".

### Adopted so far

| Fork file | Idea from | What was adopted |
| --- | --- | --- |
| `TiaMcpServer.OpennessWorker/Openness/Hmi/HmiScreenEvents.cs` (`hmi_write set_dynamization_value_converter`, value converter rows of `list_screen_item_dynamizations`) | `src/Tia/TiaManager.Hmi.cs` (`SetHmiTagDynamization`, mapping table part), `src/Tia/TiaManager.HmiJson.cs` (`ApplyValueConverter`, `SetExpressionDynamization`) | The feature (mapping tables with range, simple and bitmask entries, formulas of expression dynamizations) and the order: select the formula before writing it; delete every entry, set `ConditionType`, then create; bitmask entries through the one `Entries.Create(BitDynamizationType)` unless TIA Portal already created them. Runtime binding, value conversion by the live type, the read-back of every value and of the entry count, the guarded `hmi_write` pipeline and the tests are this fork's. |

## Sparkz400/TIA_MCP (Apache-2.0)

- Source: <https://github.com/Sparkz400/TIA_MCP> at `5cb6722` (2026-11-20), cloned read-only and never built or run. Its
  `LICENSE` (Apache License 2.0) and `NOTICE` were read first: "TIA MCP, Copyright 2026 Nico Schürmann. This product includes
  software developed by Nico Schürmann (https://github.com/Sparkz400/TIA_MCP)." Apache-2.0 is compatible with this fork's
  AGPL-3.0-only (GPLv3 §7); the notice is kept here.
- Only the design was taken; no code was copied. Every Siemens member comes from the Siemens V21 documentation named below.

### Adopted so far

| Fork file | Idea from | What was adopted |
| --- | --- | --- |
| `TiaMcpServer.Contracts/Project/UmacProjectLogin.cs`, `TiaMcpServer/ProjectLifecycle/UmacOpenItem.cs`, `TiaPortalSession.OpenProject` (`open_project` `umacUserName`) | commit `315493b` "Name other Openness clients on attach and open UMAC-protected projects" (`src/TiaMcp.Openness.V21/OpennessSession.Projects.cs`) | Opening a UMAC-protected project with `ProjectComposition.Open(FileInfo, UmacDelegate)`: answer TIA Portal's login callback once, refuse a second one so a refused password is never resent, and report how often TIA Portal asked. Reimplemented with runtime binding and the fork's password sources (no credential envelope); members from Siemens "TIA Portal Openness V21 - Functions for Projects and Project Data" as quoted in `totally-integrated-claude` `skills/tia-project-general/references/umac-and-auth.md`. |

## Czarnak/siemens-docs-mcp (MIT)

- Source: <https://github.com/Czarnak/siemens-docs-mcp> at `a0a0a24` (2026-11-02), package `siemens_docs_mcp` 0.1.2,
  cloned read-only. Its `LICENSE` was read first: MIT, `Copyright (c) 2026 Łukasz Czarnacki`.
- Owner rule: downloaded external code never runs on the owner's PC. The Python package is therefore not installed or
  shipped; its design was **ported** to reviewed C# in `TiaMcpServer/SiemensDocs/` (the `siemens_docs` tool). For a
  converter comparison it was run once, isolated (`python -I`, from another directory), only in the cloud container.
- Every endpoint and JSON shape was re-checked against the live sites on 2026-11-03; where they differ, the live site
  was followed (see the [Siemens documentation reference](docs/SupportedOperations/SIEMENS_DOCS_SUMMARY.md)).
- Each ported file carries an origin header naming the upstream module and the licence.

### Ported so far

| Fork file | Upstream module | What was ported |
| --- | --- | --- |
| `TiaMcpServer/SiemensDocs/SiemensDocsHttp.cs` | `siemens_docs_mcp/client.py` | The Fluid Topics endpoints, the web-session request before the first API call, per-host minimum interval, one retry after 401/403 (new session) and after 429/5xx (Retry-After). The host allowlist on every request, no redirects, size caps and the failure categories are this fork's. |
| `TiaMcpServer/SiemensDocs/SiemensDocsCatalog.cs` | `siemens_docs_mcp/catalog.py`, `toc.py` | Publication metadata (product, version keys `tia:SoftwareVersionFilter`/`SoftwareVersion`, pretty URL), case-insensitive filters with close matches, compact references `tia:`/`iox:`, reader-URL resolution (id form, pretty URL tried longest first), TOC parsing with a synthetic root for multi-section publications. The single-map lookup and the bounded caches are this fork's. |
| `TiaMcpServer/SiemensDocs/SiemensDocsMarkdown.cs` | `siemens_docs_mcp/converter.py`, `content.py` | The Siemens class mapping (BlocktitleFirst/Blocktitle, table_sourcecode/p_table_l_code, safety tables, MkDocs admonitions), the chrome selectors, GFM tables and nested lists. The HTML parser (`SiemensDocsHtml.cs`) and the fixes found on the live HTML (code line breaks, title banner, table captions) are this fork's. |
| `TiaMcpServer/SiemensDocs/SiemensDocsService.cs`, `SiemensDocsOptions.cs` | `siemens_docs_mcp/tools.py`, `server.py` | The four operations, their limits and the settings (hosts, locale, minimum interval, there `SIEMENS_DOCS_*`, here `--docs-*`/`TIA_MCP_DOCS_*`). The batch tool shape, offset paging of TOC and publication lists and the structured results are this fork's. |

### Notice

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

The SIMATIC SD operations follow the Siemens V21 skill `openness_development/skills/simatic-sd/SKILL.md`
(MIT, Siemens AG): its export/import calls, the §4 network parsing, §5 language detection, §6 normalization and §7
insertion rules were re-implemented in `TiaMcpServer.Contracts/Plc/SimaticSd/` (no file was copied). See the
[SIMATIC SD reference](docs/SupportedOperations/PLC_SIMATIC_SD_SUMMARY.md).

## a4webdev/tiacommander-mcp (proprietary EULA)

Ideas only. No code and no text may be copied.

This is not legal advice.
