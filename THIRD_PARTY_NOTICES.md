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

Ideas only (no code): the export-counting rules of `src/TiaMcp.Openness/TiaConnection.cs`
(`ExportsSinceImport`) behind `BlockExportLedger`, and the no-`Attach()` listing of
`ListRunningInstances` behind `otherClientCount` of `list_portals`.

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
- Only test data was taken; its skill text (`tools/tiaportal-mcp/skill/SKILL.md` §9a) is cited as reported V21 behaviour,
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

## Siemens AG `tia-portal-ai-extensions` (MIT)

The SIMATIC SD operations follow the Siemens V21 skill `openness_development/skills/simatic-sd/SKILL.md`
(MIT, Siemens AG): its export/import calls, the §4 network parsing, §5 language detection, §6 normalization and §7
insertion rules were re-implemented in `TiaMcpServer.Contracts/Plc/SimaticSd/` (no file was copied). See the
[SIMATIC SD reference](docs/SupportedOperations/PLC_SIMATIC_SD_SUMMARY.md).

## a4webdev/tiacommander-mcp (proprietary EULA)

Ideas only. No code and no text may be copied.

This is not legal advice.
