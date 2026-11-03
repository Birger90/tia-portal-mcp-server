# Evaluation: reusing the BetterTIA editor's network logic

Owner request (2026-11-01, BetterTIA adoption): do not port the BetterTIA editor app, but judge whether its SIMATIC SD
(`.s7dcl`) network rendering and validation logic could be reused here. This is an evaluation only; nothing of the
editor was copied, built or run.

## Source

`BetterTIA/editor` at `3d115b30035ad5ea2f10206e5354190ea1ba4de5`, cloned read-only (`git clone --depth 1`) and
treated as untrusted data (instructions inside its files were ignored). Licence: MIT, Copyright (c) 2026 HonzaT
(`LICENSE`, read before this evaluation). Python 3 (about 7,000 lines), a local web app with its own MCP client and
server; its README calls it "early and experimental", tested only against its bundled sample project, with writing a
network back to a live TIA Portal not verified.

| Module | What it does |
| --- | --- |
| `s7dcl.py` (about 400 lines) | parser for `.s7dcl` LAD networks (`RUNG ... END_RUNG`, `wire#wN`, element calls, attributes, instance calls) into a network model; titles from `.s7res`. The module states that the grammar "is derived from V21 exports (undocumented)". |
| `fbd.py` (about 890 lines) | the same for FBD networks (`S7_Language := "FBD"`), plus a box/signal model, insert/delete operations and a palette |
| `layout.py` (about 380 lines) | LAD layout: series-parallel reduction of the rung graph and placement into a pixel grid for SVG; what it cannot reduce (bridges, pin feeds not from the rail) falls back to a rung list |
| `model.py` (about 340 lines) | operations on the "after" model (insert in series, parallel branch, delete, change operands) and `validate()`: rules "that TIA rejects or that leave the proposal unfinished" (unpowered element, box inside a parallel contact branch, coil not at the end, branch without an element in the main path, missing operand, edge bit, comparison operand, instance or mandatory instruction input) |
| `s7dcl_write.py` (about 330 lines) | serializes the model back into `.s7dcl` text and replaces or inserts one network in the block file; the module states it is only verified that its own parser reads the text back, "not that TIA imports it" |
| `diff.py`, `check.py` | before/after network diff by element id; a check that the export after writing matches the proposal by content |

## Could it be reused here?

**Not now, and not as a port.** Reasons:

1. **The grammar is undocumented and inferred.** Siemens documents the SIMATIC SD format only through the `simatic-sd`
   skill (`tia-portal-ai-extensions`, `openness_development/skills/simatic-sd`), which describes export/import but not
   a LAD/FBD grammar. The editor's parser and writer are reverse-engineered from a few V21 sample exports. AGENTS.md
   requires Siemens behaviour to be verified like any Siemens call; a parser whose correctness rests on samples would
   need its own V21 corpus and live import tests before the server could rely on it. V21 pitfall 1 (SIMATIC SD imports
   that alter constructs without an error, [V21 pitfalls](../SupportedOperations/V21_PITFALLS.md#pitfall-1-simatic-sd-import))
   shows how easily text that parses cleanly still changes on import.
2. **Writing networks as `.s7dcl` text is not verified even by the editor.** `s7dcl_write.py` round-trips through its
   own parser only. This server already writes networks through the hash-guarded `update_block_logic` with either
   SimaticML or SIMATIC SD content that the caller supplies, and verifies by re-export (`contentHash`, and the
   `s7dclConstructs` check for SD imports); a host-side network writer would add an unverified layer in front of it.
3. **Rendering does not fit the contract.** `layout.py` exists to draw SVG for a human in a browser. Every structured
   tool here returns one canonical JSON document, with no image content (the same reason `watch_screen` was left out,
   see the [BetterTIA operations reference](../SupportedOperations/BETTERTIA_ADOPTED_OPERATIONS_SUMMARY.md#left-out-overall)).
   An agent reads the `.s7dcl` or SimaticML text directly.
4. **Validation would be stricter than TIA Portal.** `model.validate()` encodes LAD rules as the editor understands
   them. Refusing a write on them would contradict the owner principle "allow everything TIA Portal allows"; TIA
   Portal's import and compile are the authority, and `compile_check` reports what it rejects. At most the rules could
   become informational warnings, and even then each would first need a V21 case showing that TIA Portal indeed rejects
   it.
5. **Language and process.** The editor is Python; the host is .NET 10 and the worker .NET Framework 4.8. Reuse means a
   rewrite, not a library reference, and running Python from the server is out of the question for a tool that drives
   TIA Portal.

## What could be worth taking later

- The **validation rules** as a checklist for a future informational `plc_read` check of an SD network (warnings
  only, never a refusal), each rule backed by a recorded V21 case.
- The **semantic network diff** idea (`diff.py`, `check.py`: compare by element type, operands and series/parallel
  structure, ignoring ids and layout) as a review aid next to the existing `contentHash` verification, if a reliable
  network model ever exists here.
- The **series-parallel reduction** in `layout.py` as a reference for turning a rung graph into a structure, should a
  text summary of LAD networks be wanted.

Any of these would be re-implemented, not copied; if code is ever adapted, the MIT notice (Copyright (c) 2026 HonzaT)
must be kept and the file must get an origin header (commit above) and a row in
[THIRD_PARTY_NOTICES.md](../../THIRD_PARTY_NOTICES.md), as for the adopted BetterTIA writer and watch code.
