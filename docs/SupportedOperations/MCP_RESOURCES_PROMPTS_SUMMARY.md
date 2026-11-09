# MCP resources and prompts

**Language:** English · [Deutsch](MCP_RESOURCES_PROMPTS_SUMMARY.de.md)

> **Status: not live-tested.** Resources and prompts are tested offline through the real MCP protocol (in-process server
> and client of the ModelContextProtocol C# SDK 2.3.0); no MCP client on the engineering PC has listed or used them yet.
> How a client shows them (resource picker, slash commands, attachments) is up to the client.

Besides its tools the server offers MCP **resources** (read-only reference text) and **prompts** (ready-made workflow
instructions). Neither needs TIA Portal, a worker or a bound project, and neither reads anything from the user's machine:
the resources are the reference documents embedded into the server at build time and the server's own operation
catalogs; the prompts only produce text. The tool count (13/33/33) is unchanged.

## Resources

`resources/list`, `resources/templates/list` and `resources/read`; available in every access mode (read-only included).

| URI | Content | MIME type |
| --- | --- | --- |
| `tia-mcp://docs/en/<NAME>.md`, `tia-mcp://docs/de/<NAME>.md` | every document of `docs/SupportedOperations/` in English and German (for example `tia-mcp://docs/en/PLC_OPERATIONS_SUMMARY.md`, `tia-mcp://docs/de/CLAUDE_IN_TIA.md`, this page as `MCP_RESOURCES_PROMPTS_SUMMARY.md`) | `text/markdown` |
| `tia-mcp://docs/en/server/README.md`, `.../de/server/README.md` | the server README | `text/markdown` |
| `tia-mcp://docs/en/server/FORK_FEATURES.md`, `.../de/server/FORK_FEATURES.md` | what this fork adds to the original | `text/markdown` |
| `tia-mcp://operations` | every tool with an `operations` array: availability by access mode, operation count, reference document | `application/json` |
| template `tia-mcp://operations/{tool}` | the operations of one tool with their URIs (and their fields where the catalog exposes them) | `application/json` |
| template `tia-mcp://operations/{tool}/{operation}` | one operation | `application/json` |

- Each document resource has a `name` (`docs-en-PLC_OPERATIONS_SUMMARY`, `docs-de-server-README`), its first heading
  as `title`, a `description` and its `size` in bytes. The text is the document as committed; relative links inside it
  point to repository files and do not resolve as resources.
- The documents are embedded with `TiaMcpServer/McpContext/EmbeddedDocs.props` (imported by the server and the test
  project). A new document in `docs/SupportedOperations/` becomes a resource with the next build; a test checks that every
  file is embedded in both languages with its exact text.
- Operation tools: `plc_read`, `plc_write`, `network_read`, `network_write`, `hmi_read`, `hmi_write`, `vci_read`,
  `vci_write`, `library_read`, `library_write`, `plcsim_read`, `plcsim_write`, `plc_online_read`, `plc_online_write`,
  `siemens_docs`, `tia_inbox`. A test pins this list against every production tool with an `operations` argument.
  Tools without an operations array (`bind_project`, `compile_check`, `plc_download`, `plc_tia_online`,
  `plc_station_upload`, `plcsim_test`, lifecycle tools) are described by their tool descriptions.
- `plc_read`/`plc_write` operations carry `description`: exactly the entry `plc_read describe_operations` returns
  (`PlcOperationReference.DescribeOne`, same fields, field rules and reference text). `network_*` operations carry the
  required and optional fields of the network catalog. For the other tools the resource lists the operation names of the
  tool's catalog; their fields and rules are in the tool description and the reference document named in the entry.
- Write tools are listed in every mode with `availability` "read-write and full (not offered in read-only)": the
  resource is documentation; whether a tool runs is still decided by the access mode.
- An unknown document, tool or operation is a protocol error (resource not found) that names the index to read.

## Prompts

`prompts/list` and `prompts/get`. Each prompt returns one user message in English that tells the model to answer in
the user's language and ends with the server rules: passwords only as password sources (environment variable name,
`{ "credential": "TiaMcp/..." }` or the password map), TLS trust only through `trustedCertificates` with fingerprints
the user checked, the access mode decides what may change, the safety acceptance stays with a qualified person.
Argument values are quoted into the text as data.

| Prompt | Arguments | Listed in | Workflow |
| --- | --- | --- | --- |
| `explain_block` | `blockPath` | every mode | read the block (`get_block_content`, `list_networks`, `get_block_interface`, cross-references, call tree) and explain it; read-only |
| `review_block` | `blockPath` | every mode | review for logic errors, robustness, naming, fail-safe status; findings by severity; no writes; a review is no safety acceptance |
| `revise_block_safely` | `blockPath`, `goal` | read-write, full | read content and `contentHash` → plan shown to the user → `plc_write` `dryRun:true` with `expectedContentHash` → write → `compile_check` → report; never `save_project` or `plc_download` without the user's explicit request |
| `document_project` | optional `plcName` | every mode | `get_project_status`, `browse_project_tree`, `list_tag_tables`, `get_call_tree`, `read_hardware_config`, `list_network_objects` → one Markdown document in the answer; the server writes no file |
| `check_hardware` | `deviceName` | every mode | `read_hardware_config`, network objects, CPU protection and settings, download targets, address occupancy → report; hardware compile only if the user agrees |
| `safety_overview` | `plcName` | every mode | `get_safety_administration`, `list_safety_blocks`, `get_safety_validation` → summary; no login, no writes; states that validation and acceptance stay with a qualified person |
| `download_to_plcsim` | `plcName`, `instance` | read-write, full | `plcsim_read get_instance` → `list_download_targets` (PLCSIM interface only) → `plc_download` `dryRun:true` → download after the user agreed (read-write confirms the call) → on `abort.tls` the user compares the fingerprints, retry with `trustedCertificates` only with approved ones → `get_instance` |
| `handle_tia_inbox` | none | every mode | `tia_inbox list_requests` → `get_request` → confirm the address hints → work under the access mode → `reply_request` for every request |

- A prompt is listed only in the access modes that offer the tools it needs (`revise_block_safely`: ProjectMutation and
  Compile; `download_to_plcsim`: OnlineControl and SimulationControl), so read-only lists six prompts.
- `prompts/get` validates the arguments: an unknown name, a missing or blank required argument, a value that is no
  string, longer than 400 characters or contains control characters (line breaks included) is a protocol error
  (invalid params) naming the problem. Optional arguments may be omitted or null.
- A test checks that every `plc_read`/`plc_write`/`network_read`/`plcsim_*`/`tia_inbox` operation a prompt names exists
  in the catalogs.

## Implementation

`TiaMcpServer/McpContext/`: `TiaMcpDocuments.cs` (embedded documents, `McpServerResource` per document),
`TiaMcpOperationResources.cs` (operation list and the two templates), `TiaMcpPrompts.cs` (prompt definitions,
validation, `McpServerPrompt`, `WithTiaMcpResourcesAndPrompts(mode)`). The SDK primitives used are the abstract
`McpServerResource` (`ProtocolResourceTemplate`, `ProtocolResource`, `IsMatch`, `ReadAsync`) and `McpServerPrompt`
(`ProtocolPrompt`, `GetAsync`) registered with `WithResources(IEnumerable<McpServerResource>)` and
`WithPrompts(IEnumerable<McpServerPrompt>)` of ModelContextProtocol 2.3.0. Tests:
`TiaMcpServer.Tests/McpContext/McpResourcesAndPromptsTests.cs`.

## Left out

- Resource subscriptions and list-changed notifications: the content is fixed at build time.
- Completions (`completion/complete`) for prompt arguments: block paths and device names would need the bound project,
  which this package does not read.
- Project data as resources (blocks, tag tables, hardware): left out on purpose; they stay behind the tools, the
  binding and the access mode.
